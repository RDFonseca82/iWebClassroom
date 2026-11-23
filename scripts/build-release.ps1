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

$agentInstallerOutput = Join-Path $installerBuildRoot 'agent'
$serverInstallerOutput = Join-Path $installerBuildRoot 'server'
& $dotnet build (Join-Path $repositoryRoot 'installer\iWebClassroom.Agent.Setup\iWebClassroom.Agent.Setup.wixproj') -c Release -p:ProductVersion=$Version -p:PublishDir=$agentOutput -p:OutputPath=$agentInstallerOutput
& $dotnet build (Join-Path $repositoryRoot 'installer\iWebClassroom.Server.Setup\iWebClassroom.Server.Setup.wixproj') -c Release -p:ProductVersion=$Version -p:PublishDir=$serverOutput -p:OutputPath=$serverInstallerOutput

$installerFiles = [Collections.Generic.List[string]]::new()
foreach ($culture in @('pt-PT', 'en-US', 'es-ES')) {
    foreach ($component in @('Agent', 'Server')) {
        $buildRoot = if ($component -eq 'Agent') { $agentInstallerOutput } else { $serverInstallerOutput }
        $baseName = "iWebClassroom-$component-$Version-win-x64.msi"
        $source = Join-Path $buildRoot "$culture\$baseName"
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing localized installer: $source" }
        $destination = Join-Path $artifactsRoot "iWebClassroom-$component-$Version-win-x64-$culture.msi"
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $installerFiles.Add($destination)
    }
}

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

$agentStableInstaller = Join-Path $artifactsRoot "iWebClassroom-Agent-$Version-win-x64-pt-PT.msi"
$serverStableInstaller = Join-Path $artifactsRoot "iWebClassroom-Server-$Version-win-x64-pt-PT.msi"
$publishedAt = [DateTimeOffset]::UtcNow.ToString('o')
@{
    component = 'agent'; channel = 'stable'; version = $Version
    downloadUrl = "https://github.com/RDFonseca82/iWebClassroom/releases/download/v$Version/$(Split-Path -Leaf $agentStableInstaller)"
    sha256 = (Get-FileHash -LiteralPath $agentStableInstaller -Algorithm SHA256).Hash
    signerThumbprint = $SigningCertificateThumbprint.Replace(' ', '')
    mandatory = $false; publishedAtUtc = $publishedAt
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactsRoot 'agent-stable.json') -Encoding UTF8
@{
    component = 'server'; channel = 'stable'; version = $Version
    downloadUrl = "https://github.com/RDFonseca82/iWebClassroom/releases/download/v$Version/$(Split-Path -Leaf $serverStableInstaller)"
    sha256 = (Get-FileHash -LiteralPath $serverStableInstaller -Algorithm SHA256).Hash
    signerThumbprint = $SigningCertificateThumbprint.Replace(' ', '')
    mandatory = $false; publishedAtUtc = $publishedAt
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactsRoot 'server-stable.json') -Encoding UTF8

Write-Host "Release artifacts created in $artifactsRoot"
