[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TenantId,

    [Parameter(Mandatory)]
    [string]$ClientId,

    [Parameter(Mandatory)]
    [string]$ClientSecret,

    [Parameter(Mandatory)]
    [string]$SellerId,

    [Parameter(Mandatory)]
    [string]$ProductId,

    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string]$PackageUrl,

    [Parameter(Mandatory)]
    [string]$MetadataPath,

    [string]$AssetsDirectory = "",

    [int]$ReadyTimeoutMinutes = 30
)

$ErrorActionPreference = "Stop"
$baseUrl = "https://api.store.microsoft.com"

$tokenResponse = Invoke-RestMethod `
    -Method Post `
    -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
    -ContentType "application/x-www-form-urlencoded" `
    -Body @{
        client_id = $ClientId
        client_secret = $ClientSecret
        grant_type = "client_credentials"
        scope = "https://api.store.microsoft.com/.default"
    }

$headers = @{
    Authorization = "Bearer $($tokenResponse.access_token)"
    "X-Seller-Account-Id" = $SellerId
}

function Invoke-StoreApi {
    param(
        [Parameter(Mandatory)]
        [string]$Method,

        [Parameter(Mandatory)]
        [string]$Path,

        [object]$Body
    )

    $arguments = @{
        Method = $Method
        Uri = "$baseUrl$Path"
        Headers = $headers
    }
    if ($null -ne $Body) {
        $arguments.ContentType = "application/json"
        $arguments.Body = $Body | ConvertTo-Json -Depth 20
    }

    $response = Invoke-RestMethod @arguments
    if ($response.PSObject.Properties.Name -contains "isSuccess" -and
        -not $response.isSuccess) {
        $messages = $response.errors |
            ForEach-Object { "[$($_.target)] $($_.code): $($_.message)" }
        throw "Store API request failed: $($messages -join '; ')"
    }

    return $response
}

function Wait-ForDraftReady {
    $deadline = (Get-Date).AddMinutes($ReadyTimeoutMinutes)
    do {
        $status = Invoke-StoreApi `
            -Method Get `
            -Path "/submission/v1/product/$ProductId/status"
        if ($status.responseData.ongoingSubmissionId) {
            throw "Submission $($status.responseData.ongoingSubmissionId) is already in progress."
        }
        if ($status.responseData.isReady) {
            return
        }

        Start-Sleep -Seconds 20
    } while ((Get-Date) -lt $deadline)

    throw "The Partner Center draft did not become ready within $ReadyTimeoutMinutes minutes."
}

$metadata = Get-Content $MetadataPath -Raw | ConvertFrom-Json
Invoke-StoreApi `
    -Method Patch `
    -Path "/submission/v1/product/$ProductId/metadata" `
    -Body $metadata | Out-Null

$packageBody = @{
    packages = @(
        @{
            packageUrl = $PackageUrl
            languages = @("en-us")
            architectures = @("X64")
            isSilentInstall = $false
            installerParameters = "/qn /norestart"
            packageType = "msi"
        }
    )
}
Invoke-StoreApi `
    -Method Put `
    -Path "/submission/v1/product/$ProductId/packages" `
    -Body $packageBody | Out-Null
Invoke-StoreApi `
    -Method Post `
    -Path "/submission/v1/product/$ProductId/packages/commit" | Out-Null

if (-not [string]::IsNullOrWhiteSpace($AssetsDirectory) -and
    (Test-Path $AssetsDirectory)) {
    $logo = Get-ChildItem (Join-Path $AssetsDirectory "logos") -File |
        Sort-Object Name |
        Select-Object -First 1
    $screenshots = @(
        Get-ChildItem (Join-Path $AssetsDirectory "screenshots") -File |
            Sort-Object Name
    )

    if ($logo -and $screenshots.Count -gt 0) {
        $createBody = @{
            language = "en-us"
            createAssetRequest = @{
                Logo = 1
                Screenshot = $screenshots.Count
            }
        }
        $created = Invoke-StoreApi `
            -Method Post `
            -Path "/submission/v1/product/$ProductId/listings/assets/create" `
            -Body $createBody
        $assets = $created.responseData.listingAssets

        $uploadedLogos = @()
        for ($index = 0; $index -lt $assets.storeLogos.Count; $index++) {
            $asset = $assets.storeLogos[$index]
            $uploadHeaders = @{}
            if ($asset.httpHeaders) {
                $asset.httpHeaders.PSObject.Properties |
                    ForEach-Object { $uploadHeaders[$_.Name] = [string]$_.Value }
            }
            Invoke-WebRequest `
                -Method $asset.httpMethod `
                -Uri $asset.primaryAssetUploadUrl `
                -Headers $uploadHeaders `
                -InFile $logo.FullName | Out-Null
            $uploadedLogos += @{
                id = $asset.id
                assetUrl = $asset.primaryAssetUploadUrl
            }
        }

        $uploadedScreenshots = @()
        for ($index = 0; $index -lt $assets.screenshots.Count; $index++) {
            $asset = $assets.screenshots[$index]
            $uploadHeaders = @{}
            if ($asset.httpHeaders) {
                $asset.httpHeaders.PSObject.Properties |
                    ForEach-Object { $uploadHeaders[$_.Name] = [string]$_.Value }
            }
            Invoke-WebRequest `
                -Method $asset.httpMethod `
                -Uri $asset.primaryAssetUploadUrl `
                -Headers $uploadHeaders `
                -InFile $screenshots[$index].FullName | Out-Null
            $uploadedScreenshots += @{
                id = $asset.id
                assetUrl = $asset.primaryAssetUploadUrl
            }
        }

        $commitBody = @{
            listingAssets = @{
                language = "en-us"
                storeLogos = $uploadedLogos
                screenshots = $uploadedScreenshots
            }
        }
        Invoke-StoreApi `
            -Method Put `
            -Path "/submission/v1/product/$ProductId/listings/assets/commit" `
            -Body $commitBody | Out-Null
    }
}

Wait-ForDraftReady
$submission = Invoke-StoreApi `
    -Method Post `
    -Path "/submission/v1/product/$ProductId/submit"
$submissionId = $submission.responseData.submissionId
if (-not $submissionId) {
    throw "Partner Center did not return a submission ID."
}

Write-Output "SUBMISSION_ID=$submissionId"
Write-Output "SUBMISSION_STATUS_URL=$baseUrl/submission/v1/product/$ProductId/submission/$submissionId/status"
