param(
    [string] $UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe",
    [string] $ProjectRoot = "",
    [string] $OutputDirectory = "",
    [string] $CloudOrganization = $env:UNITY_CLOUD_ORG,
    [string] $Username = $env:UNITY_USERNAME,
    [string] $Password = $env:UNITY_PASSWORD,
    [switch] $Publish,
    [switch] $IncludePackageManager
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $ProjectRoot "dist\signed-upm"
}

if ([string]::IsNullOrWhiteSpace($CloudOrganization)) {
    throw "UNITY_CLOUD_ORG or -CloudOrganization is required for Unity -upmPack signing."
}

if ([string]::IsNullOrWhiteSpace($Username)) {
    throw "UNITY_USERNAME or -Username is required for Unity -upmPack signing."
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "UNITY_PASSWORD or -Password is required for Unity -upmPack signing."
}

if (!(Test-Path -LiteralPath $UnityPath)) {
    throw "Unity executable was not found: $UnityPath"
}

$catalogPath = Join-Path $ProjectRoot "Packages\VLiveKit\package-catalog.json"
if (!(Test-Path -LiteralPath $catalogPath)) {
    throw "Package catalog was not found: $catalogPath"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Test-BlockedPackage($item) {
    $joined = @(
        $item.name,
        $item.displayName,
        $item.packageFolderPath,
        $item.assetFolderPath,
        $item.repositoryUrl,
        $item.documentationUrl
    ) -join " "

    return $joined -match "(?i)thirdpartyassets|thirdpartyutilities"
}

function Find-PackageRoot($item) {
    $assetPackageJson = Join-Path $ProjectRoot ($item.assetFolderPath + "\package.json")
    if (Test-Path -LiteralPath $assetPackageJson) {
        return Split-Path $assetPackageJson -Parent
    }

    $packageFolder = Join-Path $ProjectRoot $item.packageFolderPath
    $candidate = Get-ChildItem -LiteralPath $packageFolder -Recurse -Filter package.json -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch "\\Library\\|\\PackageCache\\|\\node_modules\\|\\ThirdParty\\" } |
        Select-Object -First 1

    if ($candidate) {
        return Split-Path $candidate.FullName -Parent
    }

    return $null
}

function Get-PackageJson($packageRoot) {
    $path = Join-Path $packageRoot "package.json"
    if (!(Test-Path -LiteralPath $path)) {
        throw "package.json was not found in $packageRoot"
    }

    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$items = @($catalog.packages) | Where-Object {
    !(Test-BlockedPackage $_) -and ($IncludePackageManager -or $_.name -ne "com.toshi.vlivekit")
}

foreach ($item in $items) {
    $packageRoot = Find-PackageRoot $item
    if ([string]::IsNullOrWhiteSpace($packageRoot)) {
        throw "Could not locate package root for $($item.name)"
    }

    $packageJson = Get-PackageJson $packageRoot
    $outputPath = Join-Path $OutputDirectory ($packageJson.name + "-" + $packageJson.version + ".tgz")
    $logPath = Join-Path $OutputDirectory ($packageJson.name + "-" + $packageJson.version + ".unity.log")

    Write-Host "Signing $($packageJson.name)@$($packageJson.version)"
    & $UnityPath `
        -batchmode `
        -nographics `
        -quit `
        -username $Username `
        -password $Password `
        -upmPack $packageRoot $outputPath `
        -cloudOrganization $CloudOrganization `
        -logFile $logPath

    if ($LASTEXITCODE -ne 0) {
        throw "Unity -upmPack failed for $($packageJson.name). See $logPath"
    }

    if (!(Test-Path -LiteralPath $outputPath)) {
        throw "Unity -upmPack did not create $outputPath"
    }

    $signatureEntry = tar -tf $outputPath | Select-String -SimpleMatch ".attestation.p7m"
    if (!$signatureEntry) {
        throw "Signed tarball is missing .attestation.p7m: $outputPath"
    }

    if ($Publish) {
        npm.cmd publish $outputPath --cache (Join-Path $ProjectRoot ".npm-cache")
        if ($LASTEXITCODE -ne 0) {
            throw "npm publish failed for $outputPath"
        }
    }
}
