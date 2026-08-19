[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root "src\TaskLens.App\TaskLens.App.csproj"
$appManifest = Join-Path $root "src\TaskLens.App\Package.appxmanifest"
$testProject = Join-Path $root "tests\TaskLens.Core.Tests\TaskLens.Core.Tests.csproj"
$installerProject = Join-Path $root "installer\TaskLens.Installer.wixproj"
$artifacts = Join-Path $root "artifacts"
$buildId = Get-Date -Format "yyyyMMdd-HHmmss"
$publishDir = Join-Path $artifacts "staging\win32-$([Guid]::NewGuid().ToString('N'))"
$installerDir = Join-Path $artifacts "win32\installer"
$portableZip = Join-Path $artifacts "win32\TaskLens-win-x64.zip"
$msixDir = Join-Path $artifacts "msix\sideload\$buildId"
$storeUploadDir = Join-Path $artifacts "msix\store\$buildId"
$certificateDir = Join-Path $artifacts "certificate"
$buildNumber = [int](& git -C $root rev-list --count HEAD)
if (& git -C $root status --porcelain) {
    $buildNumber++
}
if ($buildNumber -gt 65535) {
    throw "The Git commit count exceeds the MSIX build component limit."
}
$packageVersion = "1.0.$buildNumber.0"
$originalManifestBytes = [IO.File]::ReadAllBytes($appManifest)
$originalManifest = [Text.Encoding]::UTF8.GetString($originalManifestBytes)
$certificate = $null

Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $publishDir, $installerDir, $msixDir, $storeUploadDir, $certificateDir | Out-Null

try {
    [xml]$manifestXml = $originalManifest
    $manifestXml.Package.Identity.Version = $packageVersion
    $manifestXml.Save($appManifest)

    dotnet test $testProject -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed."
    }

    $certificate = Get-ChildItem "Cert:\CurrentUser\My" |
        Where-Object {
            $_.Subject -eq "CN=Vipul Bhojwani" -and
            $_.FriendlyName -eq "TaskLens Development Signing" -and
            $_.HasPrivateKey -and
            $_.NotAfter -gt (Get-Date).AddDays(30)
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if (-not $certificate) {
        $certificate = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject "CN=Vipul Bhojwani" `
            -FriendlyName "TaskLens Development Signing" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -KeyExportPolicy Exportable `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -TextExtension @("2.5.29.19={critical}{text}ca=false") `
            -NotAfter (Get-Date).AddYears(2)
    }

    Export-Certificate `
        -Cert $certificate `
        -FilePath (Join-Path $certificateDir "TaskLens-Development.cer") | Out-Null
    dotnet publish $appProject `
        -c $Configuration `
        -r "win-$Platform" `
        -p:Platform=$Platform `
        -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true `
        -p:SelfContained=true `
        -p:PublishTrimmed=false `
        -p:PublishSingleFile=false `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "Win32 publish failed."
    }

    $signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" `
        -Recurse `
        -Filter signtool.exe |
        Where-Object FullName -Match "\\x64\\signtool\.exe$" |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $signTool) {
        throw "signtool.exe was not found in the Windows SDK."
    }

    Get-ChildItem $publishDir -Recurse -File |
        Where-Object Extension -In ".exe", ".dll" |
        ForEach-Object {
            & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint $_.FullName | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Signing failed for $($_.FullName)."
            }
        }

    Compress-Archive `
        -Path (Join-Path $publishDir "*") `
        -DestinationPath $portableZip `
        -Force

    dotnet build $installerProject `
        -c $Configuration `
        -p:PublishDir=$publishDir `
        -p:ArtifactDir="$installerDir\"
    if ($LASTEXITCODE -ne 0) {
        throw "MSI build failed."
    }

    $msi = Get-ChildItem $installerDir -Recurse -Filter *.msi | Select-Object -First 1
    if (-not $msi) {
        throw "The MSI build completed without producing an installer."
    }

    & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint $msi.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "MSI signing failed."
    }
    $msiSignature = Get-AuthenticodeSignature $msi.FullName
    if ($msiSignature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw "The MSI does not contain the expected signature."
    }

    dotnet publish $appProject `
        -c $Configuration `
        -r "win-$Platform" `
        -p:Platform=$Platform `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageSigningEnabled=true `
        -p:PackageCertificateThumbprint=$($certificate.Thumbprint) `
        -p:PublishTrimmed=false `
        -p:AppxPackageDir="$msixDir\" `
        -p:AppxBundle=Never `
        -p:UapAppxPackageBuildMode=Sideloading
    if ($LASTEXITCODE -ne 0) {
        throw "MSIX build failed."
    }

    $msix = Get-ChildItem $msixDir -Recurse -Filter *.msix | Select-Object -First 1
    if (-not $msix) {
        throw "The MSIX build completed without producing a package."
    }
    $msixSignature = Get-AuthenticodeSignature $msix.FullName
    if ($msixSignature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw "The MSIX does not contain the expected signature."
    }

    $msbuild = Join-Path ${env:ProgramFiles} `
        "Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    if (-not (Test-Path $msbuild)) {
        throw "Visual Studio MSBuild was not found."
    }

    & $msbuild $appProject `
        /restore `
        /t:Build `
        /p:Configuration=$Configuration `
        /p:Platform=$Platform `
        /p:RuntimeIdentifier="win-$Platform" `
        /p:GenerateAppxPackageOnBuild=true `
        /p:AppxPackageSigningEnabled=false `
        /p:PublishTrimmed=false `
        /p:AppxPackageDir="$storeUploadDir\" `
        /p:AppxBundle=Never `
        /p:UapAppxPackageBuildMode=StoreUpload `
        /m `
        /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Store upload build failed."
    }

    $storeUpload = Get-ChildItem $storeUploadDir -Recurse -Filter *.msixupload |
        Select-Object -First 1
    if (-not $storeUpload) {
        throw "The Store build completed without producing an .msixupload."
    }

    Write-Host ""
    Write-Host "TaskLens artifacts:"
    Write-Host "  Package version: $packageVersion"
    Write-Host "  Portable Win32:  $portableZip"
    Write-Host "  Win32 installer: $($msi.FullName)"
    Write-Host "  Signed MSIX:     $($msix.FullName)"
    Write-Host "  Store upload:    $($storeUpload.FullName)"
    Write-Host "  Test certificate: $(Join-Path $certificateDir 'TaskLens-Development.cer')"
}
finally {
    [IO.File]::WriteAllBytes($appManifest, $originalManifestBytes)
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}
