[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ProductVersion,

    [Parameter(Mandatory)]
    [string]$CertificatePath,

    [Parameter(Mandatory)]
    [string]$CertificatePassword,

    [string]$Configuration = "Release",

    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root "src\TaskLens.App\TaskLens.App.csproj"
$testProject = Join-Path $root "tests\TaskLens.Core.Tests\TaskLens.Core.Tests.csproj"
$installerProject = Join-Path $root "installer\TaskLens.Installer.wixproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts\store-msi"
}

$stagingDirectory = Join-Path ([IO.Path]::GetTempPath()) "tasklens-store-$([Guid]::NewGuid().ToString('N'))"
$installerDirectory = Join-Path $stagingDirectory "installer"
$publishDirectory = Join-Path $stagingDirectory "publish"
$securePassword = ConvertTo-SecureString $CertificatePassword -AsPlainText -Force
$certificate = $null
$removeImportedCertificate = $false

try {
    New-Item -ItemType Directory -Force $installerDirectory, $publishDirectory, $OutputDirectory | Out-Null

    $pfx = Get-PfxData -FilePath $CertificatePath -Password $securePassword
    $certificatePathInStore = "Cert:\CurrentUser\My\$($pfx.EndEntityCertificates[0].Thumbprint)"
    $removeImportedCertificate = -not (Test-Path $certificatePathInStore)
    $certificate = Import-PfxCertificate `
        -FilePath $CertificatePath `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -Password $securePassword `
        -Exportable
    if (-not $certificate.HasPrivateKey) {
        throw "The signing certificate does not contain a private key."
    }

    dotnet test $testProject -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed."
    }

    dotnet publish $appProject `
        -c $Configuration `
        -r win-x64 `
        -p:Platform=x64 `
        -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true `
        -p:SelfContained=true `
        -p:PublishTrimmed=false `
        -p:PublishSingleFile=false `
        -o $publishDirectory
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

    $signableFiles = Get-ChildItem $publishDirectory -Recurse -File |
        Where-Object Extension -In ".exe", ".dll"
    foreach ($file in $signableFiles) {
        $signature = Get-AuthenticodeSignature $file.FullName
        if ($signature.Status -eq "Valid") {
            continue
        }

        & $signTool sign `
            /fd SHA256 `
            /sha1 $certificate.Thumbprint `
            /tr "http://timestamp.digicert.com" `
            /td SHA256 `
            $file.FullName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Signing failed for $($file.FullName)."
        }
    }

    $invalidPayloads = $signableFiles |
        Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne "Valid" }
    if ($invalidPayloads) {
        throw "One or more Win32 payloads do not have valid Authenticode signatures."
    }

    dotnet build $installerProject `
        -c $Configuration `
        -p:Platform=x64 `
        -p:ProductVersion=$ProductVersion `
        -p:PublishDir=$publishDirectory `
        -p:ArtifactDir="$installerDirectory\"
    if ($LASTEXITCODE -ne 0) {
        throw "MSI build failed."
    }

    $msi = Get-ChildItem $installerDirectory -Recurse -Filter *.msi |
        Select-Object -First 1
    if (-not $msi) {
        throw "The MSI build completed without producing an installer."
    }

    & $signTool sign `
        /fd SHA256 `
        /sha1 $certificate.Thumbprint `
        /tr "http://timestamp.digicert.com" `
        /td SHA256 `
        $msi.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "MSI signing failed."
    }

    $msiSignature = Get-AuthenticodeSignature $msi.FullName
    if ($msiSignature.Status -ne "Valid" -or
        $msiSignature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw "The MSI does not contain the expected valid signature."
    }

    $outputMsi = Join-Path $OutputDirectory "TaskLens.Installer.msi"
    Copy-Item $msi.FullName $outputMsi -Force
    $hash = (Get-FileHash $outputMsi -Algorithm SHA256).Hash

    Write-Output "MSI_PATH=$outputMsi"
    Write-Output "MSI_SHA256=$hash"
    Write-Output "SIGNER_SUBJECT=$($certificate.Subject)"
    Write-Output "SIGNER_ISSUER=$($certificate.Issuer)"
}
finally {
    Remove-Item $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    if ($certificate -and $removeImportedCertificate) {
        Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }
}
