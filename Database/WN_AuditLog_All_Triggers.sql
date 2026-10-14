-- ==============================================================================
-- WorkNest Audit Log - All 77 Table Audit Triggers
-- Target: Live / Development / Testing Databases
--
-- Notes:
-- - Ensure dbo.WN_AuditLog exists first (run WN_AuditLog_Live_Setup.sql).
-- - Each trigger is guarded with IF OBJECT_ID(..., 'U') IS NOT NULL so tables
--   that do not exist in the target database are safely skipped without error.
-- - Triggers use 'CREATE OR ALTER TRIGGER' for safe, repeatable execution.
-- - Includes dbo.WN_LoginHistory audit trigger.
-- ==============================================================================

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- [1/77] Trigger: TR_WN_AccessCards_Audit (Table: dbo.WN_AccessCards)
IF OBJECT_ID(N'[dbo].[WN_AccessCards]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_AccessCards_Audit]
ON [dbo].[WN_AccessCards]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_AccessCards'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_AccessCards_Audit] created/updated on [dbo].[WN_AccessCards].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_AccessCards] does not exist.';
END
GO

-- [2/77] Trigger: TR_WN_AccessStatus_Audit (Table: dbo.WN_AccessStatus)
IF OBJECT_ID(N'[dbo].[WN_AccessStatus]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_AccessStatus_Audit]
ON [dbo].[WN_AccessStatus]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_AccessStatus'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_AccessStatus_Audit] created/updated on [dbo].[WN_AccessStatus].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_AccessStatus] does not exist.';
END
GO

-- [3/77] Trigger: TR_WN_Amenities_Audit (Table: dbo.WN_Amenities)
IF OBJECT_ID(N'[dbo].[WN_Amenities]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Amenities_Audit]
ON [dbo].[WN_Amenities]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Amenities'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Amenities_Audit] created/updated on [dbo].[WN_Amenities].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Amenities] does not exist.';
END
GO

-- [4/77] Trigger: TR_WN_AmountFields_Audit (Table: dbo.WN_AmountFields)
IF OBJECT_ID(N'[dbo].[WN_AmountFields]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_AmountFields_Audit]
ON [dbo].[WN_AmountFields]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_AmountFields'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_AmountFields_Audit] created/updated on [dbo].[WN_AmountFields].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_AmountFields] does not exist.';
END
GO

-- [5/77] Trigger: TR_WN_BillingPeriods_Audit (Table: dbo.WN_BillingPeriods)
IF OBJECT_ID(N'[dbo].[WN_BillingPeriods]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BillingPeriods_Audit]
ON [dbo].[WN_BillingPeriods]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_BillingPeriods'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_BillingPeriods_Audit] created/updated on [dbo].[WN_BillingPeriods].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_BillingPeriods] does not exist.';
END
GO

-- [6/77] Trigger: TR_WN_BookingAttendants_Audit (Table: dbo.WN_BookingAttendants)
IF OBJECT_ID(N'[dbo].[WN_BookingAttendants]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingAttendants_Audit]
ON [dbo].[WN_BookingAttendants]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_BookingAttendants'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_BookingAttendants_Audit] created/updated on [dbo].[WN_BookingAttendants].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_BookingAttendants] does not exist.';
END
GO

-- [7/77] Trigger: TR_WN_BookingDetails_Audit (Table: dbo.WN_BookingDetails)
IF OBJECT_ID(N'[dbo].[WN_BookingDetails]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingDetails_Audit]
ON [dbo].[WN_BookingDetails]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_BookingDetails'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_BookingDetails_Audit] created/updated on [dbo].[WN_BookingDetails].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_BookingDetails] does not exist.';
END
GO

-- [8/77] Trigger: TR_WN_BookingLines_Audit (Table: dbo.WN_BookingLines)
IF OBJECT_ID(N'[dbo].[WN_BookingLines]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingLines_Audit]
ON [dbo].[WN_BookingLines]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_BookingLines'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_BookingLines_Audit] created/updated on [dbo].[WN_BookingLines].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_BookingLines] does not exist.';
END
GO

-- [9/77] Trigger: TR_WN_BookingMeetingRoomEntitlements_Audit (Table: dbo.WN_BookingMeetingRoomEntitlements)
IF OBJECT_ID(N'[dbo].[WN_BookingMeetingRoomEntitlements]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingMeetingRoomEntitlements_Audit]
ON [dbo].[WN_BookingMeetingRoomEntitlements]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_BookingMeetingRoomEntitlements'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_BookingMeetingRoomEntitlements_Audit] created/updated on [dbo].[WN_BookingMeetingRoomEntitlements].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_BookingMeetingRoomEntitlements] does not exist.';
END
GO

-- [10/77] Trigger: TR_WN_Bookings_Audit (Table: dbo.WN_Bookings)
IF OBJECT_ID(N'[dbo].[WN_Bookings]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Bookings_Audit]
ON dbo.WN_Bookings
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Bookings'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Bookings_Audit] created/updated on [dbo].[WN_Bookings].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Bookings] does not exist.';
END
GO

-- [11/77] Trigger: TR_WN_BookingStatuses_Audit (Table: dbo.WN_BookingStatuses)
IF OBJECT_ID(N'[dbo].[WN_BookingStatuses]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingStatuses_Audit]
ON [dbo].[WN_BookingStatuses]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_BookingStatuses'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                N''NO_PK'',
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                N''NO_PK'',
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON 1=1;
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                N''NO_PK'',
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_BookingStatuses_Audit] created/updated on [dbo].[WN_BookingStatuses].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_BookingStatuses] does not exist.';
END
GO

-- [12/77] Trigger: TR_WN_BookTour_Audit (Table: dbo.WN_BookTour)
IF OBJECT_ID(N'[dbo].[WN_BookTour]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookTour_Audit]
ON [dbo].[WN_BookTour]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_BookTour'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_BookTour_Audit] created/updated on [dbo].[WN_BookTour].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_BookTour] does not exist.';
END
GO

-- [13/77] Trigger: TR_WN_ChallanCounter_Audit (Table: dbo.WN_ChallanCounter)
IF OBJECT_ID(N'[dbo].[WN_ChallanCounter]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ChallanCounter_Audit]
ON [dbo].[WN_ChallanCounter]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_ChallanCounter'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[CounterDate] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[CounterDate] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[CounterDate] = d.[CounterDate];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[CounterDate] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_ChallanCounter_Audit] created/updated on [dbo].[WN_ChallanCounter].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_ChallanCounter] does not exist.';
END
GO

-- [14/77] Trigger: TR_WN_Challans_Audit (Table: dbo.WN_Challans)
IF OBJECT_ID(N'[dbo].[WN_Challans]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Challans_Audit]
ON [dbo].[WN_Challans]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Challans'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Challans_Audit] created/updated on [dbo].[WN_Challans].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Challans] does not exist.';
END
GO

-- [15/77] Trigger: TR_WN_ChallanValidityLog_Audit (Table: dbo.WN_ChallanValidityLog)
IF OBJECT_ID(N'[dbo].[WN_ChallanValidityLog]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ChallanValidityLog_Audit]
ON [dbo].[WN_ChallanValidityLog]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_ChallanValidityLog'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_ChallanValidityLog_Audit] created/updated on [dbo].[WN_ChallanValidityLog].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_ChallanValidityLog] does not exist.';
END
GO

-- [16/77] Trigger: TR_WN_ChargeTypeRate_Audit (Table: dbo.WN_ChargeTypeRate)
IF OBJECT_ID(N'[dbo].[WN_ChargeTypeRate]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ChargeTypeRate_Audit]
ON [dbo].[WN_ChargeTypeRate]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_ChargeTypeRate'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_ChargeTypeRate_Audit] created/updated on [dbo].[WN_ChargeTypeRate].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_ChargeTypeRate] does not exist.';
END
GO

-- [17/77] Trigger: TR_WN_ChargeTypes_Audit (Table: dbo.WN_ChargeTypes)
IF OBJECT_ID(N'[dbo].[WN_ChargeTypes]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ChargeTypes_Audit]
ON [dbo].[WN_ChargeTypes]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_ChargeTypes'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_ChargeTypes_Audit] created/updated on [dbo].[WN_ChargeTypes].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_ChargeTypes] does not exist.';
END
GO

-- [18/77] Trigger: TR_WN_Cities_Audit (Table: dbo.WN_Cities)
IF OBJECT_ID(N'[dbo].[WN_Cities]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Cities_Audit]
ON [dbo].[WN_Cities]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Cities'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Cities_Audit] created/updated on [dbo].[WN_Cities].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Cities] does not exist.';
END
GO

-- [19/77] Trigger: TR_WN_CitiesTemp_Audit (Table: dbo.WN_CitiesTemp)
IF OBJECT_ID(N'[dbo].[WN_CitiesTemp]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_CitiesTemp_Audit]
ON [dbo].[WN_CitiesTemp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_CitiesTemp'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                N''NO_PK'',
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                N''NO_PK'',
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON 1=1;
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                N''NO_PK'',
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_CitiesTemp_Audit] created/updated on [dbo].[WN_CitiesTemp].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_CitiesTemp] does not exist.';
END
GO

-- [20/77] Trigger: TR_WN_Contacts_Audit (Table: dbo.WN_Contacts)
IF OBJECT_ID(N'[dbo].[WN_Contacts]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Contacts_Audit]
ON [dbo].[WN_Contacts]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Contacts'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Contacts_Audit] created/updated on [dbo].[WN_Contacts].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Contacts] does not exist.';
END
GO

-- [21/77] Trigger: TR_WN_Countries_Audit (Table: dbo.WN_Countries)
IF OBJECT_ID(N'[dbo].[WN_Countries]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Countries_Audit]
ON [dbo].[WN_Countries]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Countries'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Countries_Audit] created/updated on [dbo].[WN_Countries].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Countries] does not exist.';
END
GO

-- [22/77] Trigger: TR_WN_CustomerAttendants_Audit (Table: dbo.WN_CustomerAttendants)
IF OBJECT_ID(N'[dbo].[WN_CustomerAttendants]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_CustomerAttendants_Audit]
ON [dbo].[WN_CustomerAttendants]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_CustomerAttendants'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_CustomerAttendants_Audit] created/updated on [dbo].[WN_CustomerAttendants].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_CustomerAttendants] does not exist.';
END
GO

-- [23/77] Trigger: TR_WN_Customers_Audit (Table: dbo.WN_Customers)
IF OBJECT_ID(N'[dbo].[WN_Customers]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Customers_Audit]
ON dbo.WN_Customers
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Customers'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Customers_Audit] created/updated on [dbo].[WN_Customers].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Customers] does not exist.';
END
GO

-- [24/77] Trigger: TR_WN_CustomerSTInvoice_Audit (Table: dbo.WN_CustomerSTInvoice)
IF OBJECT_ID(N'[dbo].[WN_CustomerSTInvoice]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_CustomerSTInvoice_Audit]
ON [dbo].[WN_CustomerSTInvoice]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_CustomerSTInvoice'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_CustomerSTInvoice_Audit] created/updated on [dbo].[WN_CustomerSTInvoice].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_CustomerSTInvoice] does not exist.';
END
GO

-- [25/77] Trigger: TR_WN_EmailOtps_Temp_Audit (Table: dbo.WN_EmailOtps_Temp)
IF OBJECT_ID(N'[dbo].[WN_EmailOtps_Temp]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_EmailOtps_Temp_Audit]
ON [dbo].[WN_EmailOtps_Temp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_EmailOtps_Temp'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_EmailOtps_Temp_Audit] created/updated on [dbo].[WN_EmailOtps_Temp].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_EmailOtps_Temp] does not exist.';
END
GO

-- [26/77] Trigger: TR_WN_Floors_Audit (Table: dbo.WN_Floors)
IF OBJECT_ID(N'[dbo].[WN_Floors]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Floors_Audit]
ON [dbo].[WN_Floors]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Floors'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Floors_Audit] created/updated on [dbo].[WN_Floors].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Floors] does not exist.';
END
GO

-- [27/77] Trigger: TR_WN_GalleryImages_Audit (Table: dbo.WN_GalleryImages)
IF OBJECT_ID(N'[dbo].[WN_GalleryImages]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_GalleryImages_Audit]
ON [dbo].[WN_GalleryImages]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_GalleryImages'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_GalleryImages_Audit] created/updated on [dbo].[WN_GalleryImages].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_GalleryImages] does not exist.';
END
GO

-- [28/77] Trigger: TR_WN_HIK_AccessGrants_Audit (Table: dbo.WN_HIK_AccessGrants)
IF OBJECT_ID(N'[dbo].[WN_HIK_AccessGrants]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_AccessGrants_Audit]
ON [dbo].[WN_HIK_AccessGrants]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_AccessGrants'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[id] = d.[id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_AccessGrants_Audit] created/updated on [dbo].[WN_HIK_AccessGrants].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_AccessGrants] does not exist.';
END
GO

-- [29/77] Trigger: TR_WN_HIK_DashboardUsers_Audit (Table: dbo.WN_HIK_DashboardUsers)
IF OBJECT_ID(N'[dbo].[WN_HIK_DashboardUsers]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_DashboardUsers_Audit]
ON [dbo].[WN_HIK_DashboardUsers]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_DashboardUsers'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[id] = d.[id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_DashboardUsers_Audit] created/updated on [dbo].[WN_HIK_DashboardUsers].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_DashboardUsers] does not exist.';
END
GO

-- [30/77] Trigger: TR_WN_HIK_DevCache_Audit (Table: dbo.WN_HIK_DevCache)
IF OBJECT_ID(N'[dbo].[WN_HIK_DevCache]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_DevCache_Audit]
ON [dbo].[WN_HIK_DevCache]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_DevCache'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[device_id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[device_id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[device_id] = d.[device_id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[device_id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_DevCache_Audit] created/updated on [dbo].[WN_HIK_DevCache].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_DevCache] does not exist.';
END
GO

-- [31/77] Trigger: TR_WN_HIK_Devices_Audit (Table: dbo.WN_HIK_Devices)
IF OBJECT_ID(N'[dbo].[WN_HIK_Devices]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_Devices_Audit]
ON [dbo].[WN_HIK_Devices]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_Devices'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[id] = d.[id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_Devices_Audit] created/updated on [dbo].[WN_HIK_Devices].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_Devices] does not exist.';
END
GO

-- [32/77] Trigger: TR_WN_HIK_Events_Audit (Table: dbo.WN_HIK_Events)
IF OBJECT_ID(N'[dbo].[WN_HIK_Events]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_Events_Audit]
ON [dbo].[WN_HIK_Events]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_Events'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[id] = d.[id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_Events_Audit] created/updated on [dbo].[WN_HIK_Events].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_Events] does not exist.';
END
GO

-- [33/77] Trigger: TR_WN_HIK_FaceVault_Audit (Table: dbo.WN_HIK_FaceVault)
IF OBJECT_ID(N'[dbo].[WN_HIK_FaceVault]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_FaceVault_Audit]
ON [dbo].[WN_HIK_FaceVault]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_FaceVault'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[id] = d.[id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_FaceVault_Audit] created/updated on [dbo].[WN_HIK_FaceVault].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_FaceVault] does not exist.';
END
GO

-- [34/77] Trigger: TR_WN_HIK_FpVault_Audit (Table: dbo.WN_HIK_FpVault)
IF OBJECT_ID(N'[dbo].[WN_HIK_FpVault]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_FpVault_Audit]
ON [dbo].[WN_HIK_FpVault]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_FpVault'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[id] = d.[id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_FpVault_Audit] created/updated on [dbo].[WN_HIK_FpVault].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_FpVault] does not exist.';
END
GO

-- [35/77] Trigger: TR_WN_HIK_PendingOps_Audit (Table: dbo.WN_HIK_PendingOps)
IF OBJECT_ID(N'[dbo].[WN_HIK_PendingOps]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_PendingOps_Audit]
ON [dbo].[WN_HIK_PendingOps]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_PendingOps'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[id] = d.[id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_PendingOps_Audit] created/updated on [dbo].[WN_HIK_PendingOps].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_PendingOps] does not exist.';
END
GO

-- [36/77] Trigger: TR_WN_HIK_Settings_Audit (Table: dbo.WN_HIK_Settings)
IF OBJECT_ID(N'[dbo].[WN_HIK_Settings]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_Settings_Audit]
ON [dbo].[WN_HIK_Settings]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_Settings'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[key] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[key] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[key] = d.[key];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[key] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_Settings_Audit] created/updated on [dbo].[WN_HIK_Settings].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_Settings] does not exist.';
END
GO

-- [37/77] Trigger: TR_WN_HIK_Users_Audit (Table: dbo.WN_HIK_Users)
IF OBJECT_ID(N'[dbo].[WN_HIK_Users]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_Users_Audit]
ON [dbo].[WN_HIK_Users]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_HIK_Users'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[id] = d.[id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_HIK_Users_Audit] created/updated on [dbo].[WN_HIK_Users].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_HIK_Users] does not exist.';
END
GO

-- [38/77] Trigger: TR_WN_InvoiceDeliveryQueue_Audit (Table: dbo.WN_InvoiceDeliveryQueue)
IF OBJECT_ID(N'[dbo].[WN_InvoiceDeliveryQueue]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_InvoiceDeliveryQueue_Audit]
ON [dbo].[WN_InvoiceDeliveryQueue]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_InvoiceDeliveryQueue'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_InvoiceDeliveryQueue_Audit] created/updated on [dbo].[WN_InvoiceDeliveryQueue].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_InvoiceDeliveryQueue] does not exist.';
END
GO

-- [39/77] Trigger: TR_WN_InvoiceLines_Audit (Table: dbo.WN_InvoiceLines)
IF OBJECT_ID(N'[dbo].[WN_InvoiceLines]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_InvoiceLines_Audit]
ON [dbo].[WN_InvoiceLines]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_InvoiceLines'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_InvoiceLines_Audit] created/updated on [dbo].[WN_InvoiceLines].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_InvoiceLines] does not exist.';
END
GO

-- [40/77] Trigger: TR_WN_InvoicePrepaidPeriods_Audit (Table: dbo.WN_InvoicePrepaidPeriods)
IF OBJECT_ID(N'[dbo].[WN_InvoicePrepaidPeriods]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_InvoicePrepaidPeriods_Audit]
ON [dbo].[WN_InvoicePrepaidPeriods]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_InvoicePrepaidPeriods'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_InvoicePrepaidPeriods_Audit] created/updated on [dbo].[WN_InvoicePrepaidPeriods].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_InvoicePrepaidPeriods] does not exist.';
END
GO

-- [41/77] Trigger: TR_WN_Invoices_Audit (Table: dbo.WN_Invoices)
IF OBJECT_ID(N'[dbo].[WN_Invoices]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Invoices_Audit]
ON [dbo].[WN_Invoices]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Invoices'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Invoices_Audit] created/updated on [dbo].[WN_Invoices].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Invoices] does not exist.';
END
GO

-- [42/77] Trigger: TR_WN_Locations_Audit (Table: dbo.WN_Locations)
IF OBJECT_ID(N'[dbo].[WN_Locations]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Locations_Audit]
ON [dbo].[WN_Locations]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Locations'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Locations_Audit] created/updated on [dbo].[WN_Locations].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Locations] does not exist.';
END
GO

-- [43/77] Trigger: TR_WN_MeetingRoomUsageLogs_Audit (Table: dbo.WN_MeetingRoomUsageLogs)
IF OBJECT_ID(N'[dbo].[WN_MeetingRoomUsageLogs]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_MeetingRoomUsageLogs_Audit]
ON [dbo].[WN_MeetingRoomUsageLogs]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_MeetingRoomUsageLogs'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_MeetingRoomUsageLogs_Audit] created/updated on [dbo].[WN_MeetingRoomUsageLogs].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_MeetingRoomUsageLogs] does not exist.';
END
GO

-- [44/77] Trigger: TR_WN_MembershipPlanFeatures_Audit (Table: dbo.WN_MembershipPlanFeatures)
IF OBJECT_ID(N'[dbo].[WN_MembershipPlanFeatures]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_MembershipPlanFeatures_Audit]
ON [dbo].[WN_MembershipPlanFeatures]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_MembershipPlanFeatures'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_MembershipPlanFeatures_Audit] created/updated on [dbo].[WN_MembershipPlanFeatures].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_MembershipPlanFeatures] does not exist.';
END
GO

-- [45/77] Trigger: TR_WN_MembershipPlans_Audit (Table: dbo.WN_MembershipPlans)
IF OBJECT_ID(N'[dbo].[WN_MembershipPlans]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_MembershipPlans_Audit]
ON [dbo].[WN_MembershipPlans]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_MembershipPlans'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_MembershipPlans_Audit] created/updated on [dbo].[WN_MembershipPlans].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_MembershipPlans] does not exist.';
END
GO

-- [46/77] Trigger: TR_WN_MemberShips_Audit (Table: dbo.WN_MemberShips)
IF OBJECT_ID(N'[dbo].[WN_MemberShips]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_MemberShips_Audit]
ON [dbo].[WN_MemberShips]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_MemberShips'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_MemberShips_Audit] created/updated on [dbo].[WN_MemberShips].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_MemberShips] does not exist.';
END
GO

-- [47/77] Trigger: TR_WN_PaymentLines_Audit (Table: dbo.WN_PaymentLines)
IF OBJECT_ID(N'[dbo].[WN_PaymentLines]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PaymentLines_Audit]
ON [dbo].[WN_PaymentLines]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_PaymentLines'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_PaymentLines_Audit] created/updated on [dbo].[WN_PaymentLines].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_PaymentLines] does not exist.';
END
GO

-- [48/77] Trigger: TR_WN_PaymentMethods_Audit (Table: dbo.WN_PaymentMethods)
IF OBJECT_ID(N'[dbo].[WN_PaymentMethods]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PaymentMethods_Audit]
ON [dbo].[WN_PaymentMethods]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_PaymentMethods'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_PaymentMethods_Audit] created/updated on [dbo].[WN_PaymentMethods].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_PaymentMethods] does not exist.';
END
GO

-- [49/77] Trigger: TR_WN_Payments_Audit (Table: dbo.WN_Payments)
IF OBJECT_ID(N'[dbo].[WN_Payments]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Payments_Audit]
ON [dbo].[WN_Payments]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Payments'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Payments_Audit] created/updated on [dbo].[WN_Payments].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Payments] does not exist.';
END
GO

-- [50/77] Trigger: TR_WN_Persons_Audit (Table: dbo.WN_Persons)
IF OBJECT_ID(N'[dbo].[WN_Persons]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Persons_Audit]
ON [dbo].[WN_Persons]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Persons'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[PersonId] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[PersonId] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[PersonId] = d.[PersonId];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[PersonId] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Persons_Audit] created/updated on [dbo].[WN_Persons].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Persons] does not exist.';
END
GO

-- [51/77] Trigger: TR_WN_PlanFeatures_Audit (Table: dbo.WN_PlanFeatures)
IF OBJECT_ID(N'[dbo].[WN_PlanFeatures]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PlanFeatures_Audit]
ON [dbo].[WN_PlanFeatures]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_PlanFeatures'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_PlanFeatures_Audit] created/updated on [dbo].[WN_PlanFeatures].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_PlanFeatures] does not exist.';
END
GO

-- [52/77] Trigger: TR_WN_PricingPlans_Audit (Table: dbo.WN_PricingPlans)
IF OBJECT_ID(N'[dbo].[WN_PricingPlans]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PricingPlans_Audit]
ON [dbo].[WN_PricingPlans]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_PricingPlans'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_PricingPlans_Audit] created/updated on [dbo].[WN_PricingPlans].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_PricingPlans] does not exist.';
END
GO

-- [53/77] Trigger: TR_WN_PricingRules_Audit (Table: dbo.WN_PricingRules)
IF OBJECT_ID(N'[dbo].[WN_PricingRules]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PricingRules_Audit]
ON [dbo].[WN_PricingRules]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_PricingRules'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_PricingRules_Audit] created/updated on [dbo].[WN_PricingRules].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_PricingRules] does not exist.';
END
GO

-- [54/77] Trigger: TR_WN_PricingTierTypes_Audit (Table: dbo.WN_PricingTierTypes)
IF OBJECT_ID(N'[dbo].[WN_PricingTierTypes]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PricingTierTypes_Audit]
ON [dbo].[WN_PricingTierTypes]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_PricingTierTypes'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_PricingTierTypes_Audit] created/updated on [dbo].[WN_PricingTierTypes].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_PricingTierTypes] does not exist.';
END
GO

-- [55/77] Trigger: TR_WN_ProvinceChargeTypeTax_Audit (Table: dbo.WN_ProvinceChargeTypeTax)
IF OBJECT_ID(N'[dbo].[WN_ProvinceChargeTypeTax]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ProvinceChargeTypeTax_Audit]
ON [dbo].[WN_ProvinceChargeTypeTax]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_ProvinceChargeTypeTax'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_ProvinceChargeTypeTax_Audit] created/updated on [dbo].[WN_ProvinceChargeTypeTax].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_ProvinceChargeTypeTax] does not exist.';
END
GO

-- [56/77] Trigger: TR_WN_Provinces_Audit (Table: dbo.WN_Provinces)
IF OBJECT_ID(N'[dbo].[WN_Provinces]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Provinces_Audit]
ON [dbo].[WN_Provinces]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Provinces'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Provinces_Audit] created/updated on [dbo].[WN_Provinces].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Provinces] does not exist.';
END
GO

-- [57/77] Trigger: TR_WN_ProvincesTemp_Audit (Table: dbo.WN_ProvincesTemp)
IF OBJECT_ID(N'[dbo].[WN_ProvincesTemp]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ProvincesTemp_Audit]
ON [dbo].[WN_ProvincesTemp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_ProvincesTemp'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_ProvincesTemp_Audit] created/updated on [dbo].[WN_ProvincesTemp].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_ProvincesTemp] does not exist.';
END
GO

-- [58/77] Trigger: TR_WN_QuotationActivities_Audit (Table: dbo.WN_QuotationActivities)
IF OBJECT_ID(N'[dbo].[WN_QuotationActivities]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_QuotationActivities_Audit]
ON [dbo].[WN_QuotationActivities]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_QuotationActivities'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_QuotationActivities_Audit] created/updated on [dbo].[WN_QuotationActivities].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_QuotationActivities] does not exist.';
END
GO

-- [59/77] Trigger: TR_WN_QuotationDetails_Audit (Table: dbo.WN_QuotationDetails)
IF OBJECT_ID(N'[dbo].[WN_QuotationDetails]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_QuotationDetails_Audit]
ON [dbo].[WN_QuotationDetails]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_QuotationDetails'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_QuotationDetails_Audit] created/updated on [dbo].[WN_QuotationDetails].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_QuotationDetails] does not exist.';
END
GO

-- [60/77] Trigger: TR_WN_QuotationResponses_Audit (Table: dbo.WN_QuotationResponses)
IF OBJECT_ID(N'[dbo].[WN_QuotationResponses]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_QuotationResponses_Audit]
ON [dbo].[WN_QuotationResponses]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_QuotationResponses'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_QuotationResponses_Audit] created/updated on [dbo].[WN_QuotationResponses].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_QuotationResponses] does not exist.';
END
GO

-- [61/77] Trigger: TR_WN_Quotations_Audit (Table: dbo.WN_Quotations)
IF OBJECT_ID(N'[dbo].[WN_Quotations]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Quotations_Audit]
ON dbo.WN_Quotations
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Quotations'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Quotations_Audit] created/updated on [dbo].[WN_Quotations].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Quotations] does not exist.';
END
GO

-- [62/77] Trigger: TR_WN_RefreshTokens_Temp_Audit (Table: dbo.WN_RefreshTokens_Temp)
IF OBJECT_ID(N'[dbo].[WN_RefreshTokens_Temp]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_RefreshTokens_Temp_Audit]
ON [dbo].[WN_RefreshTokens_Temp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_RefreshTokens_Temp'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_RefreshTokens_Temp_Audit] created/updated on [dbo].[WN_RefreshTokens_Temp].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_RefreshTokens_Temp] does not exist.';
END
GO

-- [63/77] Trigger: TR_WN_Refunds_Audit (Table: dbo.WN_Refunds)
IF OBJECT_ID(N'[dbo].[WN_Refunds]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Refunds_Audit]
ON [dbo].[WN_Refunds]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Refunds'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Refunds_Audit] created/updated on [dbo].[WN_Refunds].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Refunds] does not exist.';
END
GO

-- [64/77] Trigger: TR_WN_Roles_Audit (Table: dbo.WN_Roles)
IF OBJECT_ID(N'[dbo].[WN_Roles]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Roles_Audit]
ON [dbo].[WN_Roles]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Roles'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Roles_Audit] created/updated on [dbo].[WN_Roles].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Roles] does not exist.';
END
GO

-- [65/77] Trigger: TR_WN_SecurityDeposits_Audit (Table: dbo.WN_SecurityDeposits)
IF OBJECT_ID(N'[dbo].[WN_SecurityDeposits]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SecurityDeposits_Audit]
ON [dbo].[WN_SecurityDeposits]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_SecurityDeposits'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_SecurityDeposits_Audit] created/updated on [dbo].[WN_SecurityDeposits].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_SecurityDeposits] does not exist.';
END
GO

-- [66/77] Trigger: TR_WN_SpaceAmenities_Audit (Table: dbo.WN_SpaceAmenities)
IF OBJECT_ID(N'[dbo].[WN_SpaceAmenities]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceAmenities_Audit]
ON [dbo].[WN_SpaceAmenities]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_SpaceAmenities'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                ''ceId='' + ISNULL(CAST(i.[SpaceId] AS NVARCHAR(64)), ''NULL'') + '';AmenityId='' + ISNULL(CAST(i.[AmenityId] AS NVARCHAR(64)), ''NULL''),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                ''ceId='' + ISNULL(CAST(i.[SpaceId] AS NVARCHAR(64)), ''NULL'') + '';AmenityId='' + ISNULL(CAST(i.[AmenityId] AS NVARCHAR(64)), ''NULL''),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[SpaceId] = d.[SpaceId] AND i.[AmenityId] = d.[AmenityId];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                ''ceId='' + ISNULL(CAST(d.[SpaceId] AS NVARCHAR(64)), ''NULL'') + '';AmenityId='' + ISNULL(CAST(d.[AmenityId] AS NVARCHAR(64)), ''NULL''),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_SpaceAmenities_Audit] created/updated on [dbo].[WN_SpaceAmenities].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_SpaceAmenities] does not exist.';
END
GO

-- [67/77] Trigger: TR_WN_SpaceCategories_Audit (Table: dbo.WN_SpaceCategories)
IF OBJECT_ID(N'[dbo].[WN_SpaceCategories]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceCategories_Audit]
ON [dbo].[WN_SpaceCategories]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_SpaceCategories'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_SpaceCategories_Audit] created/updated on [dbo].[WN_SpaceCategories].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_SpaceCategories] does not exist.';
END
GO

-- [68/77] Trigger: TR_WN_SpaceConfig_Audit (Table: dbo.WN_SpaceConfig)
IF OBJECT_ID(N'[dbo].[WN_SpaceConfig]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceConfig_Audit]
ON [dbo].[WN_SpaceConfig]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_SpaceConfig'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_SpaceConfig_Audit] created/updated on [dbo].[WN_SpaceConfig].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_SpaceConfig] does not exist.';
END
GO

-- [69/77] Trigger: TR_WN_SpaceInventoryConfig_Audit (Table: dbo.WN_SpaceInventoryConfig)
IF OBJECT_ID(N'[dbo].[WN_SpaceInventoryConfig]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceInventoryConfig_Audit]
ON [dbo].[WN_SpaceInventoryConfig]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_SpaceInventoryConfig'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_SpaceInventoryConfig_Audit] created/updated on [dbo].[WN_SpaceInventoryConfig].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_SpaceInventoryConfig] does not exist.';
END
GO

-- [70/77] Trigger: TR_WN_SpacePricing_Audit (Table: dbo.WN_SpacePricing)
IF OBJECT_ID(N'[dbo].[WN_SpacePricing]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpacePricing_Audit]
ON [dbo].[WN_SpacePricing]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_SpacePricing'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_SpacePricing_Audit] created/updated on [dbo].[WN_SpacePricing].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_SpacePricing] does not exist.';
END
GO

-- [71/77] Trigger: TR_WN_Spaces_Audit (Table: dbo.WN_Spaces)
IF OBJECT_ID(N'[dbo].[WN_Spaces]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Spaces_Audit]
ON [dbo].[WN_Spaces]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Spaces'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Spaces_Audit] created/updated on [dbo].[WN_Spaces].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Spaces] does not exist.';
END
GO

-- [72/77] Trigger: TR_WN_SpaceTypes_Audit (Table: dbo.WN_SpaceTypes)
IF OBJECT_ID(N'[dbo].[WN_SpaceTypes]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceTypes_Audit]
ON [dbo].[WN_SpaceTypes]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_SpaceTypes'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_SpaceTypes_Audit] created/updated on [dbo].[WN_SpaceTypes].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_SpaceTypes] does not exist.';
END
GO

-- [73/77] Trigger: TR_WN_StatusLookUp_Audit (Table: dbo.WN_StatusLookUp)
IF OBJECT_ID(N'[dbo].[WN_StatusLookUp]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_StatusLookUp_Audit]
ON [dbo].[WN_StatusLookUp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_StatusLookUp'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_StatusLookUp_Audit] created/updated on [dbo].[WN_StatusLookUp].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_StatusLookUp] does not exist.';
END
GO

-- [74/77] Trigger: TR_WN_StatusLookUpTemp_Audit (Table: dbo.WN_StatusLookUpTemp)
IF OBJECT_ID(N'[dbo].[WN_StatusLookUpTemp]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_StatusLookUpTemp_Audit]
ON [dbo].[WN_StatusLookUpTemp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_StatusLookUpTemp'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_StatusLookUpTemp_Audit] created/updated on [dbo].[WN_StatusLookUpTemp].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_StatusLookUpTemp] does not exist.';
END
GO

-- [75/77] Trigger: TR_WN_Tax_Audit (Table: dbo.WN_Tax)
IF OBJECT_ID(N'[dbo].[WN_Tax]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Tax_Audit]
ON [dbo].[WN_Tax]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Tax'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Tax_Audit] created/updated on [dbo].[WN_Tax].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Tax] does not exist.';
END
GO

-- [76/77] Trigger: TR_WN_Users_Audit (Table: dbo.WN_Users)
IF OBJECT_ID(N'[dbo].[WN_Users]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Users_Audit]
ON [dbo].[WN_Users]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_Users'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[Id] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[Id] = d.[Id];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[Id] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_Users_Audit] created/updated on [dbo].[WN_Users].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_Users] does not exist.';
END
GO

-- [77/77] Trigger: TR_WN_LoginHistory_Audit (Table: dbo.WN_LoginHistory)
IF OBJECT_ID(N'[dbo].[WN_LoginHistory]', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TR_WN_LoginHistory_Audit]
ON [dbo].[WN_LoginHistory]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N''AppUserId'') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N''AppUserEmail'') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N''SourceApp'') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N''WN_LoginHistory'';
    DECLARE @SysUtc DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        -- INSERT Operation
        IF EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[LoginId] AS NVARCHAR(64)),
                ''I'',
                NULL,
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i;
        END

        -- UPDATE Operation
        ELSE IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(i.[LoginId] AS NVARCHAR(64)),
                ''U'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM inserted i
            INNER JOIN deleted d ON i.[LoginId] = d.[LoginId];
        END

        -- DELETE Operation
        ELSE IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        BEGIN
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace, ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            SELECT 
                @EntityName,
                CAST(d.[LoginId] AS NVARCHAR(64)),
                ''D'',
                (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                NULL,
                NULL, NULL, NULL,
                @UserId, @UserEmail, @SysUtc, @SourceApp
            FROM deleted d;
        END
    END TRY
    BEGIN CATCH
        BEGIN TRY
            INSERT INTO dbo.WN_AuditLog (
                EntityName, RecordId, Operation, OldValues, NewValues,
                ErrorMessage, ErrorSeverity, StackTrace,
                ChangedByUserId, ChangedByUserEmail, ChangedAt, SourceApp
            )
            VALUES (
                @EntityName,
                ''TRIGGER_ERROR'',
                ''E'',
                NULL, NULL,
                ERROR_MESSAGE(),
                ''Error'',
                CONCAT(''Line: '', ERROR_LINE(), '' State: '', ERROR_STATE(), '' Procedure: '', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
');
    PRINT 'SUCCESS: [TR_WN_LoginHistory_Audit] created/updated on [dbo].[WN_LoginHistory].';
END
ELSE
BEGIN
    PRINT 'SKIPPED: Table [dbo].[WN_LoginHistory] does not exist.';
END
GO

