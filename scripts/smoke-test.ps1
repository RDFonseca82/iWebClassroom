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
$cloudTeacherId = 'smoke-teacher-cloud'
New-Item -ItemType Directory -Path $testDataDirectory -Force | Out-Null
@{
    teachers = @(@{
        id = $cloudTeacherId
        name = 'Smoke Cloud Teacher'
        email = 'teacher@smoke.test'
    })
    classrooms = @()
    updated_at_utc = [DateTimeOffset]::UtcNow.ToString('o')
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $testDataDirectory 'cloud-directory.json') -Encoding UTF8

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

    $roomId = "SALA-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
    $student = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/enrollment" -ContentType 'application/json' -Body (@{
        machineId = "smoke-student-$([Guid]::NewGuid())"; displayName = 'Smoke Student'; role = 0
        classroomId = $roomId; enrollmentCode = $enrollmentCode
    } | ConvertTo-Json)
    $teacher = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/enrollment" -ContentType 'application/json' -Body (@{
        machineId = "smoke-teacher-$([Guid]::NewGuid())"; displayName = 'Smoke Teacher'; role = 1
        classroomId = $roomId; enrollmentCode = $enrollmentCode
    } | ConvertTo-Json)
    $teacherAgentHeaders = @{ 'X-Agent-Id' = $teacher.agentId; 'X-Agent-Token' = $teacher.agentToken }
    Invoke-RestMethod -Method Put -Uri "$baseUrl/api/v1/agent/capabilities" -Headers $teacherAgentHeaders -ContentType 'application/json' -Body (@{
        displayName = 'Smoke Teacher Host'
        displays = @(@{ index = 0; name = 'Monitor principal'; width = 1920; height = 1080; isPrimary = $true })
    } | ConvertTo-Json -Depth 5)
    $outsideStudent = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/enrollment" -ContentType 'application/json' -Body (@{
        machineId = "smoke-outside-$([Guid]::NewGuid())"; displayName = 'Outside Student'; role = 0
        classroomId = 'OUTRA-SALA'; enrollmentCode = $enrollmentCode
    } | ConvertTo-Json)
    $sharedStudent = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/enrollment" -ContentType 'application/json' -Body (@{
        machineId = "smoke-shared-$([Guid]::NewGuid())"; displayName = 'Shared Student'; role = 0
        classroomId = 'PARTILHADO'; enrollmentCode = $enrollmentCode; isShared = $true
    } | ConvertTo-Json)
    $pendingDevice = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/enrollment" -ContentType 'application/json' -Body (@{
        machineId = "smoke-pending-$([Guid]::NewGuid())"; displayName = 'Pending Endpoint'; role = 1
        classroomId = 'IGNORADA'; enrollmentCode = ''
    } | ConvertTo-Json)
    $pendingHeaders = @{
        'X-Agent-Id' = $pendingDevice.agentId; 'X-Agent-Token' = $pendingDevice.agentToken
    }
    $pendingProfile = Invoke-RestMethod -Uri "$baseUrl/api/v1/agent/profile" -Headers $pendingHeaders
    Invoke-RestMethod -Method Put -Uri "$baseUrl/api/v1/agent/capabilities" -Headers $pendingHeaders -ContentType 'application/json' -Body (@{
        displayName = 'Corrected Endpoint Host'
        displays = @(@{ index = 0; name = 'Monitor'; width = 1366; height = 768; isPrimary = $true })
    } | ConvertTo-Json -Depth 5)
    $correctedPendingProfile = Invoke-RestMethod -Uri "$baseUrl/api/v1/agent/profile" -Headers $pendingHeaders
    $agentLogEventId = [Guid]::NewGuid()
    $agentLogUpload = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/agent/logs" -Headers @{
        'X-Agent-Id' = $sharedStudent.agentId; 'X-Agent-Token' = $sharedStudent.agentToken
    } -ContentType 'application/json' -Body (@{
        entries = @(@{
            event_id = $agentLogEventId
            occurred_at_utc = [DateTimeOffset]::UtcNow.ToString('o')
            level = 'Warning'
            category = 'Smoke'
            event_code = 'agent_smoke'
            message = 'Agent log transport smoke test.'
            exception = $null
        })
    } | ConvertTo-Json -Depth 5)
    $agentLogRecord = Get-Content -LiteralPath (Get-ChildItem -LiteralPath (Join-Path $testDataDirectory 'logs') -Filter 'server-*.jsonl' | Select-Object -First 1).FullName |
        ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object event_id -eq $agentLogEventId |
        Select-Object -First 1
    $agentUsageUpload = Invoke-WebRequest -Method Post -Uri "$baseUrl/api/v1/agent/statistics" -Headers @{
        'X-Agent-Id' = $sharedStudent.agentId; 'X-Agent-Token' = $sharedStudent.agentToken
    } -ContentType 'application/json' -Body (@{
        date = [DateTime]::Today.ToString('yyyy-MM-dd'); processStarts = 1; connectedSeconds = 120
    } | ConvertTo-Json)
    $headers = @{ 'X-Admin-Token' = $adminToken }
    $agents = @(Invoke-RestMethod -Uri "$baseUrl/api/v1/agents" -Headers $headers)
    $command = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/commands" -Headers $headers -ContentType 'application/json' -Body (@{
        targetAgentIds = @($student.agentId); kind = 0; payload = @{ message = 'Teste' }
    } | ConvertTo-Json -Depth 5)

    $teacherHeaders = $headers
    $overview = Invoke-RestMethod -Uri "$baseUrl/api/v1/teacher/overview" -Headers $teacherHeaders
    $settingsBefore = Invoke-RestMethod -Uri "$baseUrl/api/v1/settings" -Headers $headers
    $invalidCloudLogin = Invoke-WebRequest -Method Post -Uri "$baseUrl/api/v1/admin/cloud-login" -ContentType 'application/json' -SkipHttpErrorCheck -Body (@{
        username = ''; password = ''
    } | ConvertTo-Json)
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
    $blackoutCommand = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)/commands" -Headers $teacherHeaders -ContentType 'application/json' -Body (@{
        targetAgentIds = @($student.agentId); kind = 5; payload = @{}
    } | ConvertTo-Json -Depth 5)
    $shareCommand = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)/commands" -Headers $teacherHeaders -ContentType 'application/json' -Body (@{
        targetAgentIds = @($teacher.agentId); kind = 2; payload = @{ monitorIndex = 0; viewerAgentIds = @($student.agentId) }
    } | ConvertTo-Json -Depth 7)
    $studentViewCommand = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)/commands" -Headers $teacherHeaders -ContentType 'application/json' -Body (@{
        targetAgentIds = @($student.agentId); kind = 7; payload = @{}
    } | ConvertTo-Json -Depth 5)
    $studentShareCommand = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)/commands" -Headers $teacherHeaders -ContentType 'application/json' -Body (@{
        targetAgentIds = @($student.agentId); kind = 7; payload = @{ monitorIndex = 0; viewerAgentIds = @($teacher.agentId) }
    } | ConvertTo-Json -Depth 7)
    $applicationCommand = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)/commands" -Headers $teacherHeaders -ContentType 'application/json' -Body (@{
        targetAgentIds = @($student.agentId); kind = 1; payload = @{ applicationId = 'notepad' }
    } | ConvertTo-Json -Depth 5)
    $mediaPath = Join-Path $testDataDirectory 'smoke.png'
    [IO.File]::WriteAllBytes($mediaPath, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII='))
    $mediaClient = [Net.Http.HttpClient]::new()
    $mediaForm = [Net.Http.MultipartFormDataContent]::new()
    try {
        $mediaClient.DefaultRequestHeaders.Add('X-Admin-Token', $adminToken)
        $mediaBytes = [Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($mediaPath))
        $mediaBytes.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('image/png')
        $mediaForm.Add($mediaBytes, 'media', 'smoke.png')
        $mediaForm.Add([Net.Http.StringContent]::new((ConvertTo-Json -InputObject @($student.agentId) -Compress)), 'targetAgentIds')
        $mediaResponse = $mediaClient.PostAsync("$baseUrl/api/v1/lessons/$($lesson.lessonId)/media", $mediaForm).GetAwaiter().GetResult()
        $mediaStatus = [int]$mediaResponse.StatusCode
        $mediaResult = $mediaResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    }
    finally {
        $mediaForm.Dispose()
        $mediaClient.Dispose()
    }
    $mediaDownload = Invoke-WebRequest -Uri ($baseUrl + $mediaResult.downloadUrl) -Headers @{
        'X-Agent-Id' = $student.agentId; 'X-Agent-Token' = $student.agentToken
    } -SkipHttpErrorCheck
    $screenWithoutFrame = Invoke-WebRequest -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)/screens/$($student.agentId)" -Headers $teacherHeaders -SkipHttpErrorCheck
    $oldExpiry = [DateTimeOffset]$lesson.expiresAtUtc
    $extendedLesson = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)/extend" -Headers $teacherHeaders -ContentType 'application/json' -Body (@{
        minutes = 15
    } | ConvertTo-Json)
    $teacherOverviewAgent = @($overview.agents | Where-Object agentId -eq $teacher.agentId)[0]
    $extensionAdvanced = [DateTimeOffset]$extendedLesson.expiresAtUtc -gt $oldExpiry
    Invoke-RestMethod -Method Delete -Uri "$baseUrl/api/v1/lessons/$($lesson.lessonId)" -Headers $teacherHeaders
    $finalOverview = Invoke-RestMethod -Uri "$baseUrl/api/v1/teacher/overview" -Headers $teacherHeaders

    $teacherSessionHeaders = $headers
    $temporaryLesson = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/lessons" -Headers $teacherSessionHeaders -ContentType 'application/json' -Body (@{
        roomId = 'TEMP-SMOKE'; roomName = 'Sala temporária smoke'; teacherAgentId = $teacher.agentId
        durationMinutes = 45; isTemporary = $true
    } | ConvertTo-Json)
    $sharedHeaders = @{ 'X-Agent-Id' = $sharedStudent.agentId; 'X-Agent-Token' = $sharedStudent.agentToken }
    $roomResolution = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/rooms/resolve" -Headers $sharedHeaders -ContentType 'application/json' -Body (@{
        code = $temporaryLesson.joinCode; studentName = 'Aluno Smoke'
    } | ConvertTo-Json)
    $joinedTemporaryLesson = $roomResolution.lesson
    Invoke-RestMethod -Method Delete -Uri "$baseUrl/api/v1/lessons/$($temporaryLesson.lessonId)" -Headers $teacherSessionHeaders
    $usageStatistics = Get-Content -LiteralPath (Join-Path $testDataDirectory 'usage-statistics.json') | ConvertFrom-Json
    $todayUsage = $usageStatistics.days.([DateTime]::Today.ToString('yyyy-MM-dd'))

    if ($health.status -ne 'healthy' -or
        $agents.Count -lt 1 -or
        $overview.agents.Count -lt 3 -or
        $lesson.memberAgentIds.Count -ne 2 -or
        $unauthorizedOverview.StatusCode -ne 401 -or
        $duplicateLesson.StatusCode -ne 409 -or
        $outsideCommand.StatusCode -ne 400 -or
        $invalidCloudLogin.StatusCode -ne 400 -or
        $settingsBefore.cloud.connected -ne $false -or
        $null -ne $settingsBefore.license.license -or
        $settingsAfter.configuration.classroom.default_lesson_duration_minutes -ne 75 -or
        $overviewAfterSettings.defaultLessonDurationMinutes -ne 75 -or
        $command.targetCount -ne 1 -or
        $lessonCommand.targetCount -ne 1 -or
        $blackoutCommand.targetCount -ne 1 -or
        $shareCommand.targetCount -ne 1 -or
        $studentViewCommand.targetCount -ne 1 -or
        $studentShareCommand.targetCount -ne 1 -or
        $applicationCommand.targetCount -ne 1 -or
        $mediaStatus -ne 202 -or
        $mediaDownload.StatusCode -ne 200 -or
        $mediaDownload.RawContentLength -lt 1 -or
        $screenWithoutFrame.StatusCode -ne 204 -or
        -not $extensionAdvanced -or
        $overview.applications.Count -lt 2 -or
        $teacherOverviewAgent.displays.Count -ne 1 -or
        $teacherOverviewAgent.displayName -ne 'Smoke Teacher Host' -or
        @($finalOverview.lessons | Where-Object roomId -eq $roomId).Count -ne 0 -or
        $overview.teacher.teacherId -ne 'local-administrator' -or
        $temporaryLesson.joinCode -notmatch '^\d{6}$' -or
        $roomResolution.isLocal -ne $true -or
        $pendingProfile.isAssigned -ne $false -or
        $pendingProfile.classroomId -ne 'PENDENTE' -or
        $pendingProfile.displayName -ne 'Pending Endpoint' -or
        $correctedPendingProfile.displayName -ne 'Corrected Endpoint Host' -or
        $agentLogUpload.accepted -ne 1 -or
        $agentLogRecord.agent_id -ne $sharedStudent.agentId -or
        $agentLogRecord.computer_name -ne 'Shared Student' -or
        $agentUsageUpload.StatusCode -ne 202 -or
        $todayUsage.lessonsCreated -lt 2 -or
        $todayUsage.completedLessons -lt 2 -or
        $todayUsage.agentStarts -lt 1 -or
        $todayUsage.agentOnlineSeconds -lt 120 -or
        -not $joinedTemporaryLesson.memberAgentIds.Contains($sharedStudent.agentId) -or
        $joinedTemporaryLesson.participants[0].studentName -ne 'Aluno Smoke') {
        throw ("Uma ou mais verificações do protocolo falharam: health={0}; agents={1}; overviewAgents={2}; members={3}; unauthorized={4}; duplicate={5}; outside={6}; cloudLogin={7}; cloudConnected={8}; duration={9}; liveDuration={10}; adminTargets={11}; lessonTargets={12}; activeLessons={13}; teacher={14}; joinCode={15}; local={16}; joined={17}; participant={18}; blackout={19}; share={20}; studentView={21}; app={22}; media={23}/{24}/{25}; screen={26}; extended={27}; apps={28}; displays={29}; studentShare={30}; usageStatus={31}; lessons={32}; completed={33}; starts={34}; seconds={35}." -f
            $health.status,
            $agents.Count,
            $overview.agents.Count,
            $lesson.memberAgentIds.Count,
            $unauthorizedOverview.StatusCode,
            $duplicateLesson.StatusCode,
            $outsideCommand.StatusCode,
            $invalidCloudLogin.StatusCode,
            $settingsBefore.cloud.connected,
            $settingsAfter.configuration.classroom.default_lesson_duration_minutes,
            $overviewAfterSettings.defaultLessonDurationMinutes,
            $command.targetCount,
            $lessonCommand.targetCount,
            @($finalOverview.lessons | Where-Object roomId -eq $roomId).Count,
            $overview.teacher.teacherId,
            $temporaryLesson.joinCode,
            $roomResolution.isLocal,
            $joinedTemporaryLesson.memberAgentIds.Contains($sharedStudent.agentId),
            $joinedTemporaryLesson.participants[0].studentName,
            $blackoutCommand.targetCount,
            $shareCommand.targetCount,
            $studentViewCommand.targetCount,
            $applicationCommand.targetCount,
            $mediaStatus,
            $mediaDownload.StatusCode,
            $mediaDownload.RawContentLength,
            $screenWithoutFrame.StatusCode,
            $extensionAdvanced,
            $overview.applications.Count,
            $teacherOverviewAgent.displays.Count,
            $studentShareCommand.targetCount,
            $agentUsageUpload.StatusCode,
            $todayUsage.lessonsCreated,
            $todayUsage.completedLessons,
            $todayUsage.agentStarts,
            $todayUsage.agentOnlineSeconds)
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
