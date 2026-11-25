[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$PortalRoot,

    [string]$ArtifactsRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts')
)

$ErrorActionPreference = 'Stop'
$resolvedPortalRoot = (Resolve-Path -LiteralPath $PortalRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedPortalRoot 'artisan'))) {
    throw "The target is not an iWebClassroom Laravel portal: $resolvedPortalRoot"
}

$manifestPath = Join-Path $ArtifactsRoot 'server-stable.json'
$packageName = "iWebClassroom-Server-$Version-win-x64.msi"
$packagePath = Join-Path $ArtifactsRoot $packageName
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Missing manifest: $manifestPath" }
if (-not (Test-Path -LiteralPath $packagePath)) { throw "Missing package: $packagePath" }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.component -ne 'server' -or $manifest.version -ne $Version) {
    throw "The server manifest does not match version $Version."
}

$actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
if ($actualHash -ne $manifest.sha256) {
    throw 'The MSI SHA-256 does not match server-stable.json.'
}

$releaseDirectory = Join-Path $resolvedPortalRoot 'storage\app\iwebclassroom\releases'
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$destinationPackage = Join-Path $releaseDirectory $packageName
Copy-Item -LiteralPath $packagePath -Destination $destinationPackage -Force
if ((Get-FileHash -LiteralPath $destinationPackage -Algorithm SHA256).Hash -ne $actualHash) {
    throw 'The MSI copied to the portal failed SHA-256 verification.'
}

$catalog = [ordered]@{
    component = 'server'
    channel = 'stable'
    version = $Version
    tag = "v$Version"
    release_url = "https://github.com/RDFonseca82/iWebClassroom/releases/tag/v$Version"
    published_at_utc = $manifest.publishedAtUtc
    package_filename = $packageName
    manifest = [ordered]@{
        component = 'server'
        channel = 'stable'
        version = $Version
        sha256 = $manifest.sha256
        signerThumbprint = $manifest.signerThumbprint
        mandatory = [bool]$manifest.mandatory
        publishedAtUtc = $manifest.publishedAtUtc
    }
}
$catalogJson = $catalog | ConvertTo-Json -Depth 5
$versionCatalog = Join-Path $releaseDirectory "server-$Version.json"
$currentCatalog = Join-Path $releaseDirectory 'server-current.json'
$versionTemporary = Join-Path $releaseDirectory ".server-$Version.$([Guid]::NewGuid().ToString('N')).tmp"
$currentTemporary = Join-Path $releaseDirectory ".server-current.$([Guid]::NewGuid().ToString('N')).tmp"
$catalogJson | Set-Content -LiteralPath $versionTemporary -Encoding UTF8
$catalogJson | Set-Content -LiteralPath $currentTemporary -Encoding UTF8
Move-Item -LiteralPath $versionTemporary -Destination $versionCatalog -Force
Move-Item -LiteralPath $currentTemporary -Destination $currentCatalog -Force

if ([string]::IsNullOrWhiteSpace([string]$manifest.signerThumbprint)) {
    Write-Warning 'The release was published to the Cloud catalogue but is unsigned and cannot be approved automatically.'
}

Write-Host "Published server $Version to $releaseDirectory"
Write-Host "MSI SHA-256: $actualHash"
