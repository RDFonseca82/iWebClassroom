[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$SourcePath,
    [Parameter(Mandatory)] [string]$AdminToken,
    [Parameter(Mandatory)] [string]$EnrollmentCode,
    [string]$Destination = "$env:ProgramFiles\iWebClassroom\Server",
    [string]$SignerThumbprint = '',
    [string]$ServiceName = 'iWebClassroom.Server'
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Execute este script numa consola PowerShell elevada.'
}

$source = (Resolve-Path -LiteralPath $SourcePath).Path
$serverExecutable = Join-Path $source 'iWebClassroom.Server.exe'
if (-not (Test-Path -LiteralPath $serverExecutable)) { throw "Não foi encontrado $serverExecutable" }

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $Destination -Recurse -Force

$dataDirectory = Join-Path $env:ProgramData 'iWebClassroom\Server'
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$settings = @{
    Security = @{ RequireHttps = $true; AdminToken = $AdminToken; EnrollmentCode = $EnrollmentCode }
    Classroom = @{ DataDirectory = $dataDirectory; ReleaseDirectory = (Join-Path $dataDirectory 'releases') }
    Update = @{
        Enabled = -not [string]::IsNullOrWhiteSpace($SignerThumbprint)
        ManifestUrl = 'https://github.com/RDFonseca82/iWebClassroom/releases/latest/download/server-stable.json'
        SignerThumbprint = $SignerThumbprint
        CheckIntervalHours = 6
        ServiceName = $ServiceName
    }
}
$settingsPath = Join-Path $Destination 'appsettings.Local.json'
$settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
& icacls.exe $settingsPath /inheritance:r /grant:r '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
& icacls.exe $dataDirectory /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null

$installedExecutable = Join-Path $Destination 'iWebClassroom.Server.exe'
$binaryPath = ('"{0}"' -f $installedExecutable)
New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName 'iWebClassroom Server' -Description 'Servidor central iWebClassroom' -StartupType Automatic | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/30000/restart/30000/restart/30000 | Out-Null
Start-Service -Name $ServiceName

Write-Host 'Servidor instalado. Endpoint seguro: https://<nome-ou-IP>:7443 (todas as interfaces).'
