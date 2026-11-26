/* iWebClassroom Cloud - catálogo de aplicações por escola
   SQL Server, execução idempotente. Execute na base de dados do portal cloud. */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.school_applications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.school_applications
    (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_school_applications PRIMARY KEY,
        school_id BIGINT NOT NULL,
        identifier NVARCHAR(64) NOT NULL,
        display_name NVARCHAR(160) NOT NULL,
        file_name NVARCHAR(500) NOT NULL,
        arguments NVARCHAR(500) NULL,
        created_by_user_id BIGINT NULL,
        created_at DATETIME2(0) NULL,
        updated_at DATETIME2(0) NULL,
        CONSTRAINT FK_school_applications_school FOREIGN KEY (school_id) REFERENCES dbo.schools(id) ON DELETE CASCADE,
        CONSTRAINT FK_school_applications_creator FOREIGN KEY (created_by_user_id) REFERENCES dbo.users(id) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.school_applications')
      AND name = N'school_applications_school_id_identifier_unique'
)
BEGIN
    CREATE UNIQUE INDEX school_applications_school_id_identifier_unique
        ON dbo.school_applications(school_id, identifier);
END;

INSERT INTO dbo.school_applications
    (school_id, identifier, display_name, file_name, arguments, created_by_user_id, created_at, updated_at)
SELECT school.id, defaults.identifier, defaults.display_name, defaults.file_name, NULL, NULL, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM dbo.schools AS school
CROSS JOIN (VALUES
    (N'notepad', N'Bloco de Notas', N'%WINDIR%\System32\notepad.exe'),
    (N'calculator', N'Calculadora', N'%WINDIR%\System32\calc.exe')
) AS defaults(identifier, display_name, file_name)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.school_applications AS existing
    WHERE existing.school_id = school.id
      AND existing.identifier = defaults.identifier
);

COMMIT TRANSACTION;
