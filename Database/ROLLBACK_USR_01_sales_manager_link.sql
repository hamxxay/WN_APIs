SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WN_Users') AND name = N'IX_WN_Users_ManagerId')
        DROP INDEX IX_WN_Users_ManagerId ON dbo.WN_Users;

    IF EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE parent_object_id = OBJECT_ID(N'dbo.WN_Users')
                 AND name = N'FK_WN_Users_ManagerId_WN_Users')
        ALTER TABLE dbo.WN_Users DROP CONSTRAINT FK_WN_Users_ManagerId_WN_Users;

    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.WN_Users')
                 AND name = N'CK_WN_Users_ManagerId_NotSelf')
        ALTER TABLE dbo.WN_Users DROP CONSTRAINT CK_WN_Users_ManagerId_NotSelf;

    IF COL_LENGTH(N'dbo.WN_Users', N'ManagerId') IS NOT NULL
        ALTER TABLE dbo.WN_Users DROP COLUMN ManagerId;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
