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
$agentManifestPath = Join-Path $ArtifactsRoot 'agent-stable.json'
$packageName = "iWebClassroom-Server-$Version-win-x64.msi"
$agentPackageName = "iWebClassroom-Agent-$Version-win-x64.msi"
$packagePath = Join-Path $ArtifactsRoot $packageName
$agentPackagePath = Join-Path $ArtifactsRoot $agentPackageName
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Missing manifest: $manifestPath" }
if (-not (Test-Path -LiteralPath $agentManifestPath)) { throw "Missing manifest: $agentManifestPath" }
if (-not (Test-Path -LiteralPath $packagePath)) { throw "Missing package: $packagePath" }
if (-not (Test-Path -LiteralPath $agentPackagePath)) { throw "Missing package: $agentPackagePath" }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$agentManifest = Get-Content -LiteralPath $agentManifestPath -Raw | ConvertFrom-Json
if ($manifest.component -ne 'server' -or $manifest.version -ne $Version) {
    throw "The server manifest does not match version $Version."
}
if ($agentManifest.component -ne 'agent' -or $agentManifest.version -ne $Version) {
    throw "The agent manifest does not match version $Version."
}

$actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
$actualAgentHash = (Get-FileHash -LiteralPath $agentPackagePath -Algorithm SHA256).Hash
if ($actualHash -ne $manifest.sha256) {
    throw 'The MSI SHA-256 does not match server-stable.json.'
}
if ($actualAgentHash -ne $agentManifest.sha256) {
    throw 'The agent MSI SHA-256 does not match agent-stable.json.'
}

$releaseDirectory = Join-Path $resolvedPortalRoot 'storage\app\iwebclassroom\releases'
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$destinationPackage = Join-Path $releaseDirectory $packageName
$destinationAgentPackage = Join-Path $releaseDirectory $agentPackageName
Copy-Item -LiteralPath $packagePath -Destination $destinationPackage -Force
Copy-Item -LiteralPath $agentPackagePath -Destination $destinationAgentPackage -Force
if ((Get-FileHash -LiteralPath $destinationPackage -Algorithm SHA256).Hash -ne $actualHash) {
    throw 'The MSI copied to the portal failed SHA-256 verification.'
}
if ((Get-FileHash -LiteralPath $destinationAgentPackage -Algorithm SHA256).Hash -ne $actualAgentHash) {
    throw 'The agent MSI copied to the portal failed SHA-256 verification.'
}

$catalog = [ordered]@{
    component = 'server'
    channel = 'stable'
    version = $Version
    tag = "v$Version"
    release_url = "https://github.com/RDFonseca82/iWebClassroom/releases/tag/v$Version"
    published_at_utc = $manifest.publishedAtUtc
    package_filename = $packageName
    agent_package_filename = $agentPackageName
    manifest = [ordered]@{
        component = 'server'
        channel = 'stable'
        version = $Version
        sha256 = $manifest.sha256
        signerThumbprint = $manifest.signerThumbprint
        mandatory = [bool]$manifest.mandatory
        publishedAtUtc = $manifest.publishedAtUtc
    }
    agent_manifest = [ordered]@{
        component = 'agent'
        channel = 'stable'
        version = $Version
        sha256 = $agentManifest.sha256
        signerThumbprint = $agentManifest.signerThumbprint
        mandatory = [bool]$agentManifest.mandatory
        publishedAtUtc = $agentManifest.publishedAtUtc
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
Write-Host "Server MSI SHA-256: $actualHash"
Write-Host "Agent MSI SHA-256: $actualAgentHash"
