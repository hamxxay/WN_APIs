SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @IdentityInsertOn BIT = 0;

    IF OBJECT_ID(N'dbo.WN_Roles', N'U') IS NULL
        THROW 51000, 'dbo.WN_Roles must exist before applying USR_01_sales_manager_link.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.WN_Roles WHERE Id = 18 AND RoleName = N'sales_manager')
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.WN_Roles WHERE Id = 18 OR RoleName = N'sales_manager')
            THROW 51001, 'RoleId 18 or the sales_manager role name is already assigned to a different role.', 1;

        IF COLUMNPROPERTY(OBJECT_ID(N'dbo.WN_Roles'), N'Id', 'IsIdentity') = 1
        BEGIN
            SET IDENTITY_INSERT dbo.WN_Roles ON;
            SET @IdentityInsertOn = 1;
            INSERT INTO dbo.WN_Roles (Id, RoleName, DisplayName, IsActive, CreatedAt)
            VALUES (18, N'sales_manager', N'Sales Manager', 1, SYSUTCDATETIME());
            SET IDENTITY_INSERT dbo.WN_Roles OFF;
            SET @IdentityInsertOn = 0;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.WN_Roles (Id, RoleName, DisplayName, IsActive, CreatedAt)
            VALUES (18, N'sales_manager', N'Sales Manager', 1, SYSUTCDATETIME());
        END
    END;

    IF COL_LENGTH(N'dbo.WN_Users', N'ManagerId') IS NULL
        ALTER TABLE dbo.WN_Users ADD ManagerId INT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
                   WHERE parent_object_id = OBJECT_ID(N'dbo.WN_Users')
                     AND name = N'FK_WN_Users_ManagerId_WN_Users')
        ALTER TABLE dbo.WN_Users
            ADD CONSTRAINT FK_WN_Users_ManagerId_WN_Users
                FOREIGN KEY (ManagerId) REFERENCES dbo.WN_Users (Id);

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE parent_object_id = OBJECT_ID(N'dbo.WN_Users')
                     AND name = N'CK_WN_Users_ManagerId_NotSelf')
        ALTER TABLE dbo.WN_Users
            ADD CONSTRAINT CK_WN_Users_ManagerId_NotSelf
                CHECK (ManagerId <> Id);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WN_Users') AND name = N'IX_WN_Users_ManagerId')
        CREATE INDEX IX_WN_Users_ManagerId ON dbo.WN_Users (ManagerId);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @IdentityInsertOn = 1
        SET IDENTITY_INSERT dbo.WN_Roles OFF;
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
