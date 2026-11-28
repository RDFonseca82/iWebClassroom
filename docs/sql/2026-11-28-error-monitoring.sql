/*
  iWebClassroom Cloud - central error monitoring workflow.
  SQL Server / Azure SQL. Safe to run more than once.
*/

IF OBJECT_ID(N'dbo.server_logs', N'U') IS NULL
BEGIN
    THROW 50001, 'The dbo.server_logs table does not exist. Run 2026-11-26-server-logs.sql first.', 1;
END;
GO

IF COL_LENGTH(N'dbo.server_logs', N'status') IS NULL
BEGIN
    ALTER TABLE dbo.server_logs ADD status NVARCHAR(20) NOT NULL
        CONSTRAINT DF_server_logs_status DEFAULT N'open';
END;
GO

IF COL_LENGTH(N'dbo.server_logs', N'acknowledged_at') IS NULL
    ALTER TABLE dbo.server_logs ADD acknowledged_at DATETIMEOFFSET(7) NULL;
GO

IF COL_LENGTH(N'dbo.server_logs', N'resolved_at') IS NULL
    ALTER TABLE dbo.server_logs ADD resolved_at DATETIMEOFFSET(7) NULL;
GO

IF COL_LENGTH(N'dbo.server_logs', N'handled_by_user_id') IS NULL
    ALTER TABLE dbo.server_logs ADD handled_by_user_id BIGINT NULL;
GO

IF COL_LENGTH(N'dbo.server_logs', N'resolution_notes') IS NULL
    ALTER TABLE dbo.server_logs ADD resolution_notes NVARCHAR(MAX) NULL;
GO

IF COL_LENGTH(N'dbo.server_logs', N'component_version') IS NULL
    ALTER TABLE dbo.server_logs ADD component_version NVARCHAR(50) NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.server_logs')
      AND name = N'FK_server_logs_users_handled_by_user_id'
)
BEGIN
    ALTER TABLE dbo.server_logs ADD CONSTRAINT FK_server_logs_users_handled_by_user_id
        FOREIGN KEY (handled_by_user_id) REFERENCES dbo.users(id) ON DELETE SET NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.server_logs')
      AND name = N'IX_server_logs_version_occurred'
)
BEGIN
    CREATE INDEX IX_server_logs_version_occurred
        ON dbo.server_logs (component_version, occurred_at);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.server_logs')
      AND name = N'IX_server_logs_status_occurred'
)
BEGIN
    CREATE INDEX IX_server_logs_status_occurred
        ON dbo.server_logs (status, occurred_at);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.server_logs')
      AND name = N'IX_server_logs_school_status_occurred'
)
BEGIN
    CREATE INDEX IX_server_logs_school_status_occurred
        ON dbo.server_logs (school_id, status, occurred_at);
END;
GO
