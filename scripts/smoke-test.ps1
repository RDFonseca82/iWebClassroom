[CmdletBinding()]
param([int]$Port = 57080)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$adminToken = 'smoke-admin-token'
$enrollmentCode = 'smoke-enrollment-code'
$baseUrl = "http://127.0.0.1:$Port"
$testDataRoot = Join-Path $repositoryRoot 'artifacts\smoke-test'
$testDataDirectory = Join-Path $testDataRoot ([Guid]::NewGuid().ToString('N'))

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
$startInfo.Environment['Classroom__DataDirectory'] = $testDataDirectory
$server = [Diagnostics.Process]::Start($startInfo)

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        try { $health = Invoke-RestMethod -Uri "$baseUrl/health" } catch { $health = $null }
    } while (-not $health -and [DateTime]::UtcNow -lt $deadline)
    if (-not $health) { throw 'O servidor não ficou disponível dentro do tempo esperado.' }

    $teacherAccessPath = Join-Path $testDataDirectory 'teacher-access.json'
    if (-not (Test-Path -LiteralPath $teacherAccessPath)) { throw 'O servidor não criou o PIN de professor inicial.' }
    $teacherPin = (Get-Content -Raw -LiteralPath $teacherAccessPath | ConvertFrom-Json).security.teacherPin
    if ($teacherPin -notmatch '^\d{6}$') { throw 'O PIN de professor inicial não tem o formato esperado.' }

    $roomId = "SALA-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
    $student = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/enrollment" -ContentType 'application/json' -Body (@{
        machineId = "smoke-student-$([Guid]::NewGuid())"; displayName = 'Smoke Student'; role = 0
        classroomId = $roomId; enrollmentCode = $enrollmentCode
    } | ConvertTo-Json)
    $teacher = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/enrollment" -ContentType 'application/json' -Body (@{
        machineId = "smoke-teacher-$([Guid]::NewGuid())"; displayName = 'Smoke Teacher'; role = 1
        classroomId = $roomId; enrollmentCode = $enrollmentCode
    } | ConvertTo-Json)
    $outsideStudent = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/enrollment" -ContentType 'application/json' -Body (@{
        machineId = "smoke-outside-$([Guid]::NewGuid())"; displayName = 'Outside Student'; role = 0
        classroomId = 'OUTRA-SALA'; enrollmentCode = $enrollmentCode
    } | ConvertTo-Json)
    $headers = @{ 'X-Admin-Token' = $adminToken }
    $agents = @(Invoke-RestMethod -Uri "$baseUrl/api/v1/agents" -Headers $headers)
    $command = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/commands" -Headers $headers -ContentType 'application/json' -Body (@{
        targetAgentIds = @($student.agentId); kind = 0; payload = @{ message = 'Teste' }
    } | ConvertTo-Json -Depth 5)

    $teacherHeaders = @{ 'X-Teacher-Pin' = $teacherPin }
    $overview = Invoke-RestMethod -Uri "$baseUrl/api/v1/teacher/overview" -Headers $teacherHeaders
    $settingsBefore = Invoke-RestMethod -Uri "$baseUrl/api/v1/settings" -Headers $headers
    $settingsAfter = Invoke-RestMethod -Method Put -Uri "$baseUrl/api/v1/settings" -Headers $headers -ContentType 'application/json' -Body (@{
        security = @{ require_https = $false }
        classroom = @{
            maximum_frame_bytes = 1250000
            minimum_frame_interval_milliseconds = 200
            default_lesson_duration_minutes = 75
            allowed_application_ids = @('notepad', 'calculator')
        }
        update = @{
            enabled = $true
            manifest_url = 'https://github.com/RDFonseca82/iWebClassroom/releases/latest/download/server-stable.json'
            signer_thumbprint = ''
            check_interval_hours = 12
        }
    } | ConvertTo-Json -Depth 5)
    Start-Sleep -Milliseconds 500
    $overviewAfterSettings = Invoke-RestMethod -Uri "$baseUrl/api/v1/teacher/overview" -Headers $teacherHeaders
    $lesson = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/lessons" -Headers $teacherHeaders -ContentType 'application/json' -Body (@{
        roomId = $roomId; teacherAgentId = $teacher.agentId; durationMinutes = 90
    } | ConvertTo-Json)
    $unauthorizedOverview = Invoke-WebRequest -Uri "$baseUrl/api/v1/teacher/overview" -SkipHttpErrorCheck
    $duplicateLesson = Invoke-WebRequest -Method Post -Uri "$baseUrl/api/v1/lessons" -Headers $teacherHeaders -ContentType 'application/json' -SkipHttpErrorCheck -Body (@{
        roomId = $roomId; teacherAgentId = $teacher.agentId; durationMinutes = 90
    } | ConvertTo-Json)
    $outsideCommand = Invoke-WebRequest -Method Post -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)/commands" -Headers $teacherHeaders -ContentType 'application/json' -SkipHttpErrorCheck -Body (@{
        targetAgentIds = @($outsideStudent.agentId); kind = 0; payload = @{ message = 'Não autorizado' }
    } | ConvertTo-Json -Depth 5)
    $lessonCommand = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)/commands" -Headers $teacherHeaders -ContentType 'application/json' -Body (@{
        targetAgentIds = @($student.agentId); kind = 0; payload = @{ message = 'Teste da aula' }
    } | ConvertTo-Json -Depth 5)
    Invoke-RestMethod -Method Delete -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)" -Headers $teacherHeaders
    $finalOverview = Invoke-RestMethod -Uri "$baseUrl/api/v1/teacher/overview" -Headers $teacherHeaders

    if ($health.status -ne 'healthy' -or
        $agents.Count -lt 1 -or
        $overview.agents.Count -lt 3 -or
        $lesson.memberAgentIds.Count -ne 2 -or
        $unauthorizedOverview.StatusCode -ne 401 -or
        $duplicateLesson.StatusCode -ne 409 -or
        $outsideCommand.StatusCode -ne 400 -or
        $settingsBefore.cloud.connected -ne $false -or
        $settingsAfter.configuration.classroom.default_lesson_duration_minutes -ne 75 -or
        $overviewAfterSettings.defaultLessonDurationMinutes -ne 75 -or
        $command.targetCount -ne 1 -or
        $lessonCommand.targetCount -ne 1 -or
        @($finalOverview.lessons | Where-Object roomId -eq $roomId).Count -ne 0) {
        throw ("Uma ou mais verificações do protocolo falharam: health={0}; agents={1}; overviewAgents={2}; members={3}; unauthorized={4}; duplicate={5}; outside={6}; cloudConnected={7}; duration={8}; liveDuration={9}; adminTargets={10}; lessonTargets={11}; activeLessons={12}." -f
            $health.status,
            $agents.Count,
            $overview.agents.Count,
            $lesson.memberAgentIds.Count,
            $unauthorizedOverview.StatusCode,
            $duplicateLesson.StatusCode,
            $outsideCommand.StatusCode,
            $settingsBefore.cloud.connected,
            $settingsAfter.configuration.classroom.default_lesson_duration_minutes,
            $overviewAfterSettings.defaultLessonDurationMinutes,
            $command.targetCount,
            $lessonCommand.targetCount,
            @($finalOverview.lessons | Where-Object roomId -eq $roomId).Count)
    }
    Write-Host 'Smoke test passed.'
}
finally {
    if ($server -and -not $server.HasExited) { $server.Kill($true); $server.WaitForExit() }
    $resolvedRoot = [IO.Path]::GetFullPath($testDataRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedData = [IO.Path]::GetFullPath($testDataDirectory)
    if ($resolvedData.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedData)) {
        Remove-Item -LiteralPath $resolvedData -Recurse -Force
    }
}
