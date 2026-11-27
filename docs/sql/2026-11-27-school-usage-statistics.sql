/* iWebClassroom Cloud - estatísticas agregadas de utilização
   SQL Server, idempotente. Execute na base de dados do portal cloud. */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.server_usage_statistics', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.server_usage_statistics
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_server_usage_statistics PRIMARY KEY,
        school_id BIGINT NOT NULL,
        school_server_id BIGINT NOT NULL,
        usage_date DATE NOT NULL,
        lessons_created BIGINT NOT NULL CONSTRAINT DF_sus_lessons_created DEFAULT (0),
        fixed_lessons BIGINT NOT NULL CONSTRAINT DF_sus_fixed_lessons DEFAULT (0),
        temporary_lessons BIGINT NOT NULL CONSTRAINT DF_sus_temporary_lessons DEFAULT (0),
        completed_lessons BIGINT NOT NULL CONSTRAINT DF_sus_completed_lessons DEFAULT (0),
        total_lesson_minutes BIGINT NOT NULL CONSTRAINT DF_sus_lesson_minutes DEFAULT (0),
        student_joins BIGINT NOT NULL CONSTRAINT DF_sus_student_joins DEFAULT (0),
        unique_teachers BIGINT NOT NULL CONSTRAINT DF_sus_unique_teachers DEFAULT (0),
        unique_computers BIGINT NOT NULL CONSTRAINT DF_sus_unique_computers DEFAULT (0),
        peak_online_computers BIGINT NOT NULL CONSTRAINT DF_sus_peak_online DEFAULT (0),
        agent_starts BIGINT NOT NULL CONSTRAINT DF_sus_agent_starts DEFAULT (0),
        agent_online_seconds BIGINT NOT NULL CONSTRAINT DF_sus_agent_seconds DEFAULT (0),
        applications_launched BIGINT NOT NULL CONSTRAINT DF_sus_applications DEFAULT (0),
        screen_shares BIGINT NOT NULL CONSTRAINT DF_sus_screen_shares DEFAULT (0),
        blackouts BIGINT NOT NULL CONSTRAINT DF_sus_blackouts DEFAULT (0),
        student_views BIGINT NOT NULL CONSTRAINT DF_sus_student_views DEFAULT (0),
        media_presentations BIGINT NOT NULL CONSTRAINT DF_sus_media DEFAULT (0),
        received_at DATETIMEOFFSET(0) NOT NULL,
        created_at DATETIME2(0) NULL,
        updated_at DATETIME2(0) NULL,
        CONSTRAINT FK_sus_school FOREIGN KEY (school_id) REFERENCES dbo.schools(id),
        CONSTRAINT FK_sus_server FOREIGN KEY (school_server_id) REFERENCES dbo.school_servers(id) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.server_usage_statistics') AND name = N'server_usage_statistics_server_date_unique')
    CREATE UNIQUE INDEX server_usage_statistics_server_date_unique ON dbo.server_usage_statistics(school_server_id, usage_date);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.server_usage_statistics') AND name = N'server_usage_statistics_school_date_index')
    CREATE INDEX server_usage_statistics_school_date_index ON dbo.server_usage_statistics(school_id, usage_date);

IF OBJECT_ID(N'dbo.server_teacher_usage_statistics', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.server_teacher_usage_statistics
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_server_teacher_usage_statistics PRIMARY KEY,
        server_usage_statistic_id BIGINT NOT NULL,
        teacher_external_id NVARCHAR(160) NOT NULL,
        teacher_name NVARCHAR(200) NOT NULL,
        lessons BIGINT NOT NULL CONSTRAINT DF_stus_lessons DEFAULT (0),
        minutes BIGINT NOT NULL CONSTRAINT DF_stus_minutes DEFAULT (0),
        CONSTRAINT FK_stus_usage FOREIGN KEY (server_usage_statistic_id) REFERENCES dbo.server_usage_statistics(id) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.server_teacher_usage_statistics') AND name = N'server_teacher_usage_unique')
    CREATE UNIQUE INDEX server_teacher_usage_unique ON dbo.server_teacher_usage_statistics(server_usage_statistic_id, teacher_external_id);

COMMIT TRANSACTION;
