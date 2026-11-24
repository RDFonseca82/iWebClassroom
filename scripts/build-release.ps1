[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [string]$SigningCertificateThumbprint = ''
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
$installerBuildRoot = Join-Path $artifactsRoot 'installer-build'
New-Item -ItemType Directory -Path $agentOutput, $serverOutput, $updaterOutput, $installerBuildRoot | Out-Null

& $dotnet publish (Join-Path $repositoryRoot 'src\iWebClassroom.Updater\iWebClassroom.Updater.csproj') -c Release -r $Runtime --self-contained true -p:Version=$Version -p:NuGetAudit=false -o $updaterOutput
if ($LASTEXITCODE -ne 0) { throw 'Publishing the updater failed.' }
& $dotnet publish (Join-Path $repositoryRoot 'src\iWebClassroom.Agent\iWebClassroom.Agent.csproj') -c Release -r $Runtime --self-contained true -p:Version=$Version -p:NuGetAudit=false -o $agentOutput
if ($LASTEXITCODE -ne 0) { throw 'Publishing the agent failed.' }
& $dotnet publish (Join-Path $repositoryRoot 'src\iWebClassroom.Server\iWebClassroom.Server.csproj') -c Release -r $Runtime --self-contained true -p:Version=$Version -p:NuGetAudit=false -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw 'Publishing the server failed.' }

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

$agentInstallerOutput = Join-Path $installerBuildRoot 'agent'
$serverInstallerOutput = Join-Path $installerBuildRoot 'server'
& $dotnet build (Join-Path $repositoryRoot 'installer\iWebClassroom.Agent.Setup\iWebClassroom.Agent.Setup.wixproj') -c Release -p:ProductVersion=$Version -p:PublishDir=$agentOutput -p:OutputPath=$agentInstallerOutput -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'The first agent MSI build failed; retrying once.'
    & $dotnet build (Join-Path $repositoryRoot 'installer\iWebClassroom.Agent.Setup\iWebClassroom.Agent.Setup.wixproj') -c Release -p:ProductVersion=$Version -p:PublishDir=$agentOutput -p:OutputPath=$agentInstallerOutput -p:NuGetAudit=false
}
if ($LASTEXITCODE -ne 0) { throw 'Building the agent MSI failed.' }
& $dotnet build (Join-Path $repositoryRoot 'installer\iWebClassroom.Server.Setup\iWebClassroom.Server.Setup.wixproj') -c Release -p:ProductVersion=$Version -p:PublishDir=$serverOutput -p:OutputPath=$serverInstallerOutput -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'The first server MSI build failed; retrying once.'
    & $dotnet build (Join-Path $repositoryRoot 'installer\iWebClassroom.Server.Setup\iWebClassroom.Server.Setup.wixproj') -c Release -p:ProductVersion=$Version -p:PublishDir=$serverOutput -p:OutputPath=$serverInstallerOutput -p:NuGetAudit=false
}
if ($LASTEXITCODE -ne 0) { throw 'Building the server MSI failed.' }

$agentInstaller = Join-Path $artifactsRoot "iWebClassroom-Agent-$Version-win-x64.msi"
$serverInstaller = Join-Path $artifactsRoot "iWebClassroom-Server-$Version-win-x64.msi"
$agentInstallerSource = Join-Path $agentInstallerOutput (Split-Path -Leaf $agentInstaller)
$serverInstallerSource = Join-Path $serverInstallerOutput (Split-Path -Leaf $serverInstaller)
if (-not (Test-Path -LiteralPath $agentInstallerSource)) { throw "Missing agent installer: $agentInstallerSource" }
if (-not (Test-Path -LiteralPath $serverInstallerSource)) { throw "Missing server installer: $serverInstallerSource" }
Copy-Item -LiteralPath $agentInstallerSource -Destination $agentInstaller -Force
Copy-Item -LiteralPath $serverInstallerSource -Destination $serverInstaller -Force
$installerFiles = @($agentInstaller, $serverInstaller)

if (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
    $normalizedThumbprint = $SigningCertificateThumbprint.Replace(' ', '')
    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
        Select-Object -First 1
    if (-not $certificate) { throw "Signing certificate $normalizedThumbprint was not found." }
    foreach ($installer in $installerFiles) {
        $signature = Set-AuthenticodeSignature -FilePath $installer -Certificate $certificate -TimestampServer 'http://timestamp.digicert.com' -HashAlgorithm SHA256
        if ($signature.Status -ne 'Valid') { throw "Failed to sign ${installer}: $($signature.StatusMessage)" }
    }
}

$publishedAt = [DateTimeOffset]::UtcNow.ToString('o')
@{
    component = 'agent'; channel = 'stable'; version = $Version
    downloadUrl = "https://github.com/RDFonseca82/iWebClassroom/releases/download/v$Version/$(Split-Path -Leaf $agentInstaller)"
    sha256 = (Get-FileHash -LiteralPath $agentInstaller -Algorithm SHA256).Hash
    signerThumbprint = $SigningCertificateThumbprint.Replace(' ', '')
    mandatory = $false; publishedAtUtc = $publishedAt
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactsRoot 'agent-stable.json') -Encoding UTF8
@{
    component = 'server'; channel = 'stable'; version = $Version
    downloadUrl = "https://github.com/RDFonseca82/iWebClassroom/releases/download/v$Version/$(Split-Path -Leaf $serverInstaller)"
    sha256 = (Get-FileHash -LiteralPath $serverInstaller -Algorithm SHA256).Hash
    signerThumbprint = $SigningCertificateThumbprint.Replace(' ', '')
    mandatory = $false; publishedAtUtc = $publishedAt
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactsRoot 'server-stable.json') -Encoding UTF8

Write-Host "Release artifacts created in $artifactsRoot"
