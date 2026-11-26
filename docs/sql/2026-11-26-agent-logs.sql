/*
  iWebClassroom Cloud - associate server logs with the originating agent/computer.
  SQL Server / Azure SQL. Safe to run more than once.
*/

IF OBJECT_ID(N'dbo.server_logs', N'U') IS NULL
BEGIN
    THROW 50001, 'The dbo.server_logs table does not exist. Run 2026-11-26-server-logs.sql first.', 1;
END;
GO

IF COL_LENGTH(N'dbo.server_logs', N'agent_id') IS NULL
    ALTER TABLE dbo.server_logs ADD agent_id UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.server_logs', N'machine_id') IS NULL
    ALTER TABLE dbo.server_logs ADD machine_id NVARCHAR(200) NULL;
GO

IF COL_LENGTH(N'dbo.server_logs', N'computer_name') IS NULL
    ALTER TABLE dbo.server_logs ADD computer_name NVARCHAR(160) NULL;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.server_logs')
      AND name = N'server_logs_server_agent_occurred_index'
)
BEGIN
    CREATE INDEX server_logs_server_agent_occurred_index
        ON dbo.server_logs (school_server_id, agent_id, occurred_at);
END;
GO
