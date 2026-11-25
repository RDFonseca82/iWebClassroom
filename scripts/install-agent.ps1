[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$SourcePath,
    [Parameter(Mandatory)] [ValidatePattern('^https?://')] [string]$ServerUrl,
    [ValidateSet('pt-PT', 'en-US', 'es-ES')] [string]$Language = 'pt-PT',
    [ValidateSet('Fixed', 'Shared')] [string]$AssignmentMode = 'Fixed',
    [ValidatePattern('^[A-Za-z0-9_-]{1,50}$')] [string]$ClassroomId = 'SALA-1',
    [string]$Destination = "$env:ProgramFiles\iWebClassroom\Agent",
    [string]$SignerThumbprint = ''
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Execute este script numa consola PowerShell elevada.'
}

$source = (Resolve-Path -LiteralPath $SourcePath).Path
$agentExecutable = Join-Path $source 'iWebClassroom.Agent.exe'
if (-not (Test-Path -LiteralPath $agentExecutable)) { throw "Não foi encontrado $agentExecutable" }

Get-Process -Name 'iWebClassroom.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $Destination -Recurse -Force

$agentDataDirectory = Join-Path $env:ProgramData 'iWebClassroom\Agent'
New-Item -ItemType Directory -Path $agentDataDirectory -Force | Out-Null
$authenticatedUsers = [Security.Principal.SecurityIdentifier]::new('S-1-5-11').Translate([Security.Principal.NTAccount])
$dataAcl = Get-Acl -LiteralPath $agentDataDirectory
$dataRule = [Security.AccessControl.FileSystemAccessRule]::new(
    $authenticatedUsers,
    [Security.AccessControl.FileSystemRights]::Modify,
    [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow)
$dataAcl.SetAccessRule($dataRule)
Set-Acl -LiteralPath $agentDataDirectory -AclObject $dataAcl

$updatePolicy = @{
    enabled = -not [string]::IsNullOrWhiteSpace($SignerThumbprint)
    channel = 'stable'
    signerThumbprint = $SignerThumbprint
}
$updatePolicy | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Destination 'update.json') -Encoding UTF8

$installedExecutable = Join-Path $Destination 'iWebClassroom.Agent.exe'
$runPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runPath -Name 'iWebClassroom Agent' -Value ('"{0}"' -f $installedExecutable)
$agentRegistryPath = 'HKLM:\SOFTWARE\iWebClassroom\Agent'
New-Item -Path $agentRegistryPath -Force | Out-Null
Set-ItemProperty -Path $agentRegistryPath -Name 'ServerUrl' -Value $ServerUrl.Trim().TrimEnd('/')
Set-ItemProperty -Path $agentRegistryPath -Name 'InstallLanguage' -Value $Language
Set-ItemProperty -Path $agentRegistryPath -Name 'AssignmentMode' -Value $AssignmentMode
Set-ItemProperty -Path $agentRegistryPath -Name 'ClassroomId' -Value $ClassroomId
Start-Process -FilePath $installedExecutable

Write-Host 'Agente instalado. Conclua a inscrição na janela que foi aberta.'
