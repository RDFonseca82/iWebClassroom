SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.server_logs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.server_logs
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_server_logs PRIMARY KEY,
        school_id BIGINT NOT NULL,
        school_server_id BIGINT NOT NULL,
        event_id UNIQUEIDENTIFIER NOT NULL,
        occurred_at DATETIMEOFFSET(7) NOT NULL,
        level NVARCHAR(20) NOT NULL,
        category NVARCHAR(200) NOT NULL,
        event_code NVARCHAR(100) NULL,
        message NVARCHAR(MAX) NOT NULL,
        exception NVARCHAR(MAX) NULL,
        received_at DATETIMEOFFSET(7) NOT NULL,
        CONSTRAINT FK_server_logs_schools_school_id
            FOREIGN KEY (school_id) REFERENCES dbo.schools(id),
        CONSTRAINT FK_server_logs_school_servers_school_server_id
            FOREIGN KEY (school_server_id) REFERENCES dbo.school_servers(id) ON DELETE CASCADE,
        CONSTRAINT UQ_server_logs_server_event UNIQUE (school_server_id, event_id)
    );

    CREATE INDEX IX_server_logs_school_occurred
        ON dbo.server_logs (school_id, occurred_at);
    CREATE INDEX IX_server_logs_server_level_occurred
        ON dbo.server_logs (school_server_id, level, occurred_at);
END;

COMMIT TRANSACTION;
