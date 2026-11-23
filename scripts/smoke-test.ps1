[CmdletBinding()]
param([int]$Port = 57080)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$adminToken = 'smoke-admin-token'
$enrollmentCode = 'smoke-enrollment-code'
$baseUrl = "http://127.0.0.1:$Port"

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $dotnet
$startInfo.WorkingDirectory = $repositoryRoot
$startInfo.UseShellExecute = $false
$startInfo.ArgumentList.Add('run')
$startInfo.ArgumentList.Add('--project')
$startInfo.ArgumentList.Add('src\iWebClassroom.Server\iWebClassroom.Server.csproj')
$startInfo.ArgumentList.Add('--no-build')
$startInfo.ArgumentList.Add('--configuration')
$startInfo.ArgumentList.Add('Release')
$startInfo.ArgumentList.Add('--urls')
$startInfo.ArgumentList.Add($baseUrl)
$startInfo.Environment['IWEBCLASSROOM_ADMIN_TOKEN'] = $adminToken
$startInfo.Environment['IWEBCLASSROOM_ENROLLMENT_CODE'] = $enrollmentCode
$startInfo.Environment['Security__RequireHttps'] = 'false'
$server = [Diagnostics.Process]::Start($startInfo)

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        try { $health = Invoke-RestMethod -Uri "$baseUrl/health" } catch { $health = $null }
    } while (-not $health -and [DateTime]::UtcNow -lt $deadline)
    if (-not $health) { throw 'O servidor não ficou disponível dentro do tempo esperado.' }

    $enrollment = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/enrollment" -ContentType 'application/json' -Body (@{
        machineId = "smoke-$([Guid]::NewGuid())"; displayName = 'Smoke Student'; role = 0
        classroomId = 'SALA-TESTE'; enrollmentCode = $enrollmentCode
    } | ConvertTo-Json)
    $headers = @{ 'X-Admin-Token' = $adminToken }
    $agents = @(Invoke-RestMethod -Uri "$baseUrl/api/v1/agents" -Headers $headers)
    $command = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/commands" -Headers $headers -ContentType 'application/json' -Body (@{
        targetAgentIds = @($enrollment.agentId); kind = 0; payload = @{ message = 'Teste' }
    } | ConvertTo-Json -Depth 5)

    if ($health.status -ne 'healthy' -or $agents.Count -lt 1 -or $command.targetCount -ne 1) {
        throw 'Uma ou mais verificações do protocolo falharam.'
    }
    Write-Host 'Smoke test passed.'
}
finally {
    if ($server -and -not $server.HasExited) { $server.Kill($true); $server.WaitForExit() }
}
