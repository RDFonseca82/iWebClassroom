[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }

if (Test-Path -LiteralPath $artifactsRoot) {
    $resolvedArtifacts = (Resolve-Path -LiteralPath $artifactsRoot).Path
    if (-not $resolvedArtifacts.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear an artifacts directory outside the repository."
    }
    Remove-Item -LiteralPath $resolvedArtifacts -Recurse -Force
}

$agentOutput = Join-Path $artifactsRoot 'agent'
$serverOutput = Join-Path $artifactsRoot 'server'
$updaterOutput = Join-Path $artifactsRoot 'updater'
New-Item -ItemType Directory -Path $agentOutput, $serverOutput, $updaterOutput | Out-Null

& $dotnet publish (Join-Path $repositoryRoot 'src\iWebClassroom.Updater\iWebClassroom.Updater.csproj') -c Release -r $Runtime --self-contained true -p:Version=$Version -o $updaterOutput
& $dotnet publish (Join-Path $repositoryRoot 'src\iWebClassroom.Agent\iWebClassroom.Agent.csproj') -c Release -r $Runtime --self-contained true -p:Version=$Version -o $agentOutput
& $dotnet publish (Join-Path $repositoryRoot 'src\iWebClassroom.Server\iWebClassroom.Server.csproj') -c Release -r $Runtime --self-contained true -p:Version=$Version -o $serverOutput

$updaterFiles = @(
    'iWebClassroom.Updater.exe',
    'iWebClassroom.Updater.dll',
    'iWebClassroom.Updater.deps.json',
    'iWebClassroom.Updater.runtimeconfig.json'
)
foreach ($name in $updaterFiles) {
    $source = Join-Path $updaterOutput $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing updater output: $source" }
    Copy-Item -LiteralPath $source -Destination $agentOutput -Force
    Copy-Item -LiteralPath $source -Destination $serverOutput -Force
}

Compress-Archive -Path (Join-Path $agentOutput '*') -DestinationPath (Join-Path $artifactsRoot "iWebClassroom-Agent-$Version-$Runtime.zip") -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $serverOutput '*') -DestinationPath (Join-Path $artifactsRoot "iWebClassroom-Server-$Version-$Runtime.zip") -CompressionLevel Optimal

Write-Host "Release artifacts created in $artifactsRoot"
