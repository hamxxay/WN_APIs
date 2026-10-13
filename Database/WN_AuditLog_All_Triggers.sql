-- ==============================================================================
-- WorkNest Audit Log - All 76 Table Audit Triggers
-- Target: Live Database
--
-- Notes:
-- - Ensure dbo.WN_AuditLog exists first (run WN_AuditLog_Live_Setup.sql).
-- - Triggers use 'CREATE OR ALTER TRIGGER' for safe, repeatable execution.
-- ==============================================================================

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- [1/76] Trigger: TR_WN_AccessCards_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_AccessCards_Audit]
ON [dbo].[WN_AccessCards]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_AccessCards';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [2/76] Trigger: TR_WN_AccessStatus_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_AccessStatus_Audit]
ON [dbo].[WN_AccessStatus]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_AccessStatus';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [3/76] Trigger: TR_WN_Amenities_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Amenities_Audit]
ON [dbo].[WN_Amenities]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Amenities';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [4/76] Trigger: TR_WN_AmountFields_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_AmountFields_Audit]
ON [dbo].[WN_AmountFields]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_AmountFields';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [5/76] Trigger: TR_WN_BillingPeriods_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BillingPeriods_Audit]
ON [dbo].[WN_BillingPeriods]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_BillingPeriods';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [6/76] Trigger: TR_WN_BookingAttendants_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingAttendants_Audit]
ON [dbo].[WN_BookingAttendants]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_BookingAttendants';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [7/76] Trigger: TR_WN_BookingDetails_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingDetails_Audit]
ON [dbo].[WN_BookingDetails]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_BookingDetails';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [8/76] Trigger: TR_WN_BookingLines_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingLines_Audit]
ON [dbo].[WN_BookingLines]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_BookingLines';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [9/76] Trigger: TR_WN_BookingMeetingRoomEntitlements_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingMeetingRoomEntitlements_Audit]
ON [dbo].[WN_BookingMeetingRoomEntitlements]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_BookingMeetingRoomEntitlements';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [10/76] Trigger: TR_WN_Bookings_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Bookings_Audit]
ON dbo.WN_Bookings
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Bookings';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [11/76] Trigger: TR_WN_BookingStatuses_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookingStatuses_Audit]
ON [dbo].[WN_BookingStatuses]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_BookingStatuses';
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
                N'NO_PK',
                'I',
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
                N'NO_PK',
                'U',
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
                N'NO_PK',
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [12/76] Trigger: TR_WN_BookTour_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_BookTour_Audit]
ON [dbo].[WN_BookTour]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_BookTour';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [13/76] Trigger: TR_WN_ChallanCounter_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ChallanCounter_Audit]
ON [dbo].[WN_ChallanCounter]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_ChallanCounter';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [14/76] Trigger: TR_WN_Challans_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Challans_Audit]
ON [dbo].[WN_Challans]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Challans';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [15/76] Trigger: TR_WN_ChallanValidityLog_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ChallanValidityLog_Audit]
ON [dbo].[WN_ChallanValidityLog]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_ChallanValidityLog';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [16/76] Trigger: TR_WN_ChargeTypeRate_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ChargeTypeRate_Audit]
ON [dbo].[WN_ChargeTypeRate]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_ChargeTypeRate';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [17/76] Trigger: TR_WN_ChargeTypes_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ChargeTypes_Audit]
ON [dbo].[WN_ChargeTypes]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_ChargeTypes';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [18/76] Trigger: TR_WN_Cities_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Cities_Audit]
ON [dbo].[WN_Cities]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Cities';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [19/76] Trigger: TR_WN_CitiesTemp_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_CitiesTemp_Audit]
ON [dbo].[WN_CitiesTemp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_CitiesTemp';
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
                N'NO_PK',
                'I',
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
                N'NO_PK',
                'U',
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
                N'NO_PK',
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [20/76] Trigger: TR_WN_Contacts_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Contacts_Audit]
ON [dbo].[WN_Contacts]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Contacts';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [21/76] Trigger: TR_WN_Countries_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Countries_Audit]
ON [dbo].[WN_Countries]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Countries';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [22/76] Trigger: TR_WN_CustomerAttendants_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_CustomerAttendants_Audit]
ON [dbo].[WN_CustomerAttendants]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_CustomerAttendants';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [23/76] Trigger: TR_WN_Customers_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Customers_Audit]
ON dbo.WN_Customers
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Customers';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [24/76] Trigger: TR_WN_CustomerSTInvoice_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_CustomerSTInvoice_Audit]
ON [dbo].[WN_CustomerSTInvoice]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_CustomerSTInvoice';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [25/76] Trigger: TR_WN_EmailOtps_Temp_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_EmailOtps_Temp_Audit]
ON [dbo].[WN_EmailOtps_Temp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_EmailOtps_Temp';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [26/76] Trigger: TR_WN_Floors_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Floors_Audit]
ON [dbo].[WN_Floors]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Floors';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [27/76] Trigger: TR_WN_GalleryImages_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_GalleryImages_Audit]
ON [dbo].[WN_GalleryImages]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_GalleryImages';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [28/76] Trigger: TR_WN_HIK_AccessGrants_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_AccessGrants_Audit]
ON [dbo].[WN_HIK_AccessGrants]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_AccessGrants';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [29/76] Trigger: TR_WN_HIK_DashboardUsers_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_DashboardUsers_Audit]
ON [dbo].[WN_HIK_DashboardUsers]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_DashboardUsers';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [30/76] Trigger: TR_WN_HIK_DevCache_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_DevCache_Audit]
ON [dbo].[WN_HIK_DevCache]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_DevCache';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [31/76] Trigger: TR_WN_HIK_Devices_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_Devices_Audit]
ON [dbo].[WN_HIK_Devices]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_Devices';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [32/76] Trigger: TR_WN_HIK_Events_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_Events_Audit]
ON [dbo].[WN_HIK_Events]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_Events';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [33/76] Trigger: TR_WN_HIK_FaceVault_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_FaceVault_Audit]
ON [dbo].[WN_HIK_FaceVault]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_FaceVault';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [34/76] Trigger: TR_WN_HIK_FpVault_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_FpVault_Audit]
ON [dbo].[WN_HIK_FpVault]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_FpVault';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [35/76] Trigger: TR_WN_HIK_PendingOps_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_PendingOps_Audit]
ON [dbo].[WN_HIK_PendingOps]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_PendingOps';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [36/76] Trigger: TR_WN_HIK_Settings_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_Settings_Audit]
ON [dbo].[WN_HIK_Settings]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_Settings';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [37/76] Trigger: TR_WN_HIK_Users_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_HIK_Users_Audit]
ON [dbo].[WN_HIK_Users]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_HIK_Users';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [38/76] Trigger: TR_WN_InvoiceDeliveryQueue_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_InvoiceDeliveryQueue_Audit]
ON [dbo].[WN_InvoiceDeliveryQueue]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_InvoiceDeliveryQueue';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [39/76] Trigger: TR_WN_InvoiceLines_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_InvoiceLines_Audit]
ON [dbo].[WN_InvoiceLines]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_InvoiceLines';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [40/76] Trigger: TR_WN_InvoicePrepaidPeriods_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_InvoicePrepaidPeriods_Audit]
ON [dbo].[WN_InvoicePrepaidPeriods]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_InvoicePrepaidPeriods';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [41/76] Trigger: TR_WN_Invoices_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Invoices_Audit]
ON [dbo].[WN_Invoices]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Invoices';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [42/76] Trigger: TR_WN_Locations_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Locations_Audit]
ON [dbo].[WN_Locations]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Locations';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [43/76] Trigger: TR_WN_MeetingRoomUsageLogs_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_MeetingRoomUsageLogs_Audit]
ON [dbo].[WN_MeetingRoomUsageLogs]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_MeetingRoomUsageLogs';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [44/76] Trigger: TR_WN_MembershipPlanFeatures_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_MembershipPlanFeatures_Audit]
ON [dbo].[WN_MembershipPlanFeatures]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_MembershipPlanFeatures';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [45/76] Trigger: TR_WN_MembershipPlans_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_MembershipPlans_Audit]
ON [dbo].[WN_MembershipPlans]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_MembershipPlans';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [46/76] Trigger: TR_WN_MemberShips_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_MemberShips_Audit]
ON [dbo].[WN_MemberShips]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_MemberShips';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [47/76] Trigger: TR_WN_PaymentLines_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PaymentLines_Audit]
ON [dbo].[WN_PaymentLines]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_PaymentLines';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [48/76] Trigger: TR_WN_PaymentMethods_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PaymentMethods_Audit]
ON [dbo].[WN_PaymentMethods]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_PaymentMethods';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [49/76] Trigger: TR_WN_Payments_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Payments_Audit]
ON [dbo].[WN_Payments]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Payments';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [50/76] Trigger: TR_WN_Persons_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Persons_Audit]
ON [dbo].[WN_Persons]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Persons';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [51/76] Trigger: TR_WN_PlanFeatures_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PlanFeatures_Audit]
ON [dbo].[WN_PlanFeatures]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_PlanFeatures';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [52/76] Trigger: TR_WN_PricingPlans_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PricingPlans_Audit]
ON [dbo].[WN_PricingPlans]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_PricingPlans';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [53/76] Trigger: TR_WN_PricingRules_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PricingRules_Audit]
ON [dbo].[WN_PricingRules]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_PricingRules';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [54/76] Trigger: TR_WN_PricingTierTypes_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_PricingTierTypes_Audit]
ON [dbo].[WN_PricingTierTypes]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_PricingTierTypes';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [55/76] Trigger: TR_WN_ProvinceChargeTypeTax_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ProvinceChargeTypeTax_Audit]
ON [dbo].[WN_ProvinceChargeTypeTax]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_ProvinceChargeTypeTax';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [56/76] Trigger: TR_WN_Provinces_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Provinces_Audit]
ON [dbo].[WN_Provinces]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Provinces';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [57/76] Trigger: TR_WN_ProvincesTemp_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_ProvincesTemp_Audit]
ON [dbo].[WN_ProvincesTemp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_ProvincesTemp';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [58/76] Trigger: TR_WN_QuotationActivities_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_QuotationActivities_Audit]
ON [dbo].[WN_QuotationActivities]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_QuotationActivities';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [59/76] Trigger: TR_WN_QuotationDetails_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_QuotationDetails_Audit]
ON [dbo].[WN_QuotationDetails]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_QuotationDetails';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [60/76] Trigger: TR_WN_QuotationResponses_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_QuotationResponses_Audit]
ON [dbo].[WN_QuotationResponses]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_QuotationResponses';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [61/76] Trigger: TR_WN_Quotations_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Quotations_Audit]
ON dbo.WN_Quotations
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Quotations';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [62/76] Trigger: TR_WN_RefreshTokens_Temp_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_RefreshTokens_Temp_Audit]
ON [dbo].[WN_RefreshTokens_Temp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_RefreshTokens_Temp';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [63/76] Trigger: TR_WN_Refunds_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Refunds_Audit]
ON [dbo].[WN_Refunds]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Refunds';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [64/76] Trigger: TR_WN_Roles_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Roles_Audit]
ON [dbo].[WN_Roles]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Roles';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [65/76] Trigger: TR_WN_SecurityDeposits_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SecurityDeposits_Audit]
ON [dbo].[WN_SecurityDeposits]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_SecurityDeposits';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [66/76] Trigger: TR_WN_SpaceAmenities_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceAmenities_Audit]
ON [dbo].[WN_SpaceAmenities]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_SpaceAmenities';
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
                'ceId=' + ISNULL(CAST(i.[SpaceId] AS NVARCHAR(64)), 'NULL') + ';AmenityId=' + ISNULL(CAST(i.[AmenityId] AS NVARCHAR(64)), 'NULL'),
                'I',
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
                'ceId=' + ISNULL(CAST(i.[SpaceId] AS NVARCHAR(64)), 'NULL') + ';AmenityId=' + ISNULL(CAST(i.[AmenityId] AS NVARCHAR(64)), 'NULL'),
                'U',
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
                'ceId=' + ISNULL(CAST(d.[SpaceId] AS NVARCHAR(64)), 'NULL') + ';AmenityId=' + ISNULL(CAST(d.[AmenityId] AS NVARCHAR(64)), 'NULL'),
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [67/76] Trigger: TR_WN_SpaceCategories_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceCategories_Audit]
ON [dbo].[WN_SpaceCategories]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_SpaceCategories';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [68/76] Trigger: TR_WN_SpaceConfig_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceConfig_Audit]
ON [dbo].[WN_SpaceConfig]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_SpaceConfig';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [69/76] Trigger: TR_WN_SpaceInventoryConfig_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceInventoryConfig_Audit]
ON [dbo].[WN_SpaceInventoryConfig]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_SpaceInventoryConfig';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [70/76] Trigger: TR_WN_SpacePricing_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpacePricing_Audit]
ON [dbo].[WN_SpacePricing]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_SpacePricing';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [71/76] Trigger: TR_WN_Spaces_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Spaces_Audit]
ON [dbo].[WN_Spaces]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Spaces';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [72/76] Trigger: TR_WN_SpaceTypes_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_SpaceTypes_Audit]
ON [dbo].[WN_SpaceTypes]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_SpaceTypes';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [73/76] Trigger: TR_WN_StatusLookUp_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_StatusLookUp_Audit]
ON [dbo].[WN_StatusLookUp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_StatusLookUp';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [74/76] Trigger: TR_WN_StatusLookUpTemp_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_StatusLookUpTemp_Audit]
ON [dbo].[WN_StatusLookUpTemp]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_StatusLookUpTemp';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [75/76] Trigger: TR_WN_Tax_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Tax_Audit]
ON [dbo].[WN_Tax]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Tax';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

-- [76/76] Trigger: TR_WN_Users_Audit
CREATE OR ALTER TRIGGER [dbo].[TR_WN_Users_Audit]
ON [dbo].[WN_Users]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    DECLARE @UserId INT = CAST(SESSION_CONTEXT(N'AppUserId') AS INT);
    IF @UserId IS NULL SET @UserId = -1;

    DECLARE @UserEmail NVARCHAR(256) = CAST(SESSION_CONTEXT(N'AppUserEmail') AS NVARCHAR(256));
    DECLARE @SourceApp NVARCHAR(50) = CAST(SESSION_CONTEXT(N'SourceApp') AS NVARCHAR(50));
    DECLARE @EntityName NVARCHAR(128) = N'WN_Users';
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
                'I',
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
                'U',
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
                'D',
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
                'TRIGGER_ERROR',
                'E',
                NULL, NULL,
                ERROR_MESSAGE(),
                'Error',
                CONCAT('Line: ', ERROR_LINE(), ' State: ', ERROR_STATE(), ' Procedure: ', ERROR_PROCEDURE()),
                @UserId, @UserEmail, SYSUTCDATETIME(), @SourceApp
            );
        END TRY
        BEGIN CATCH
            -- Silent swallow to guarantee original DML never fails
        END CATCH
    END CATCH
END;
GO

