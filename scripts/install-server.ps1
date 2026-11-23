[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$SourcePath,
    [Parameter(Mandatory)] [string]$AdminToken,
    [Parameter(Mandatory)] [string]$EnrollmentCode,
    [string]$Destination = "$env:ProgramFiles\iWebClassroom\Server",
    [string]$ListenUrl = 'http://127.0.0.1:7080',
    [string]$SignerThumbprint = '',
    [string]$TaskName = 'iWebClassroom Server'
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Execute este script numa consola PowerShell elevada.'
}

$source = (Resolve-Path -LiteralPath $SourcePath).Path
$serverExecutable = Join-Path $source 'iWebClassroom.Server.exe'
if (-not (Test-Path -LiteralPath $serverExecutable)) { throw "Não foi encontrado $serverExecutable" }

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $Destination -Recurse -Force

$dataDirectory = Join-Path $env:ProgramData 'iWebClassroom\Server'
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$settings = @{
    Security = @{ RequireHttps = $false; AdminToken = $AdminToken; EnrollmentCode = $EnrollmentCode }
    Classroom = @{ DataDirectory = $dataDirectory; ReleaseDirectory = (Join-Path $dataDirectory 'releases') }
    Update = @{
        Enabled = -not [string]::IsNullOrWhiteSpace($SignerThumbprint)
        ManifestUrl = 'https://github.com/RDFonseca82/iWebClassroom/releases/latest/download/server-stable.json'
        SignerThumbprint = $SignerThumbprint
        CheckIntervalHours = 6
        ScheduledTaskName = $TaskName
    }
}
$settingsPath = Join-Path $Destination 'appsettings.Local.json'
$settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
& icacls.exe $settingsPath /inheritance:r /grant:r '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
& icacls.exe $dataDirectory /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null

$installedExecutable = Join-Path $Destination 'iWebClassroom.Server.exe'
$action = New-ScheduledTaskAction -Execute $installedExecutable -Argument "--urls $ListenUrl" -WorkingDirectory $Destination
$trigger = New-ScheduledTaskTrigger -AtStartup
$taskPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$taskSettings = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $taskSettings | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "Servidor instalado. Endpoint local: $ListenUrl"
Write-Host 'Configure agora o proxy HTTPS e WebSockets no IIS/ARR.'
