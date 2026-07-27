-- ============================================================
-- Fix: WN_BookingDetails_InsertLine — remove ModifiedOn / ModifiedBy
-- The UPDATE branch references these columns which don't exist in WN_Bookings.
-- Also re-applies WN_Bookings_Insert to ensure it is up to date.
-- Run once in SSMS against SAC400
-- ============================================================



-- ── Fix 1: WN_BookingDetails_InsertLine ──────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_BookingDetails_InsertLine
    @BookingGuid UNIQUEIDENTIFIER,
    @FeeType     NVARCHAR(50),
    @Amount      DECIMAL(18,2),
    @AccountId   INT           = NULL,
    @CreatedBy   NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 FROM dbo.WN_BookingDetails
        WHERE BookingGuid = @BookingGuid AND FeeType = @FeeType AND IsDeleted = 0
    )
    BEGIN
        UPDATE dbo.WN_BookingDetails SET
            Amount    = @Amount,
            AccountId = ISNULL(@AccountId, AccountId)
        WHERE BookingGuid = @BookingGuid AND FeeType = @FeeType AND IsDeleted = 0;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_BookingDetails (IdGUID, BookingGuid, FeeType, Amount, AccountId, CreatedOn, CreatedBy)
        VALUES (NEWID(), @BookingGuid, @FeeType, @Amount, @AccountId, GETDATE(), @CreatedBy);
    END
END
GO

-- ── Fix 2: WN_Bookings_Insert (re-apply to be safe) ──────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Bookings_Insert
    @UserId        INT,
    @SpaceId       INT,
    @StartDateTime DATETIME,
    @EndDateTime   DATETIME,
    @TotalAmount   DECIMAL(18,2),
    @Notes         NVARCHAR(MAX),
    @CustomerCode  NVARCHAR(50)  = NULL,
    @AccountId     INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserGUID                 UNIQUEIDENTIFIER;
    DECLARE @SpaceGUID                UNIQUEIDENTIFIER;
    DECLARE @RentAccountId            INT;
    DECLARE @SecurityDepositAccountId INT;
    DECLARE @NewBookingGUID           UNIQUEIDENTIFIER = NEWID();
    DECLARE @SpaceTypeName            NVARCHAR(100);
    DECLARE @UserEmail                NVARCHAR(255);
    DECLARE @SecurityDeposit          DECIMAL(18,2) = 0;
    DECLARE @ChallanNumber            NVARCHAR(50);
    DECLARE @ValidityDate             DATETIME;
    DECLARE @NewId                    INT;
    DECLARE @DatePart                 NVARCHAR(8);
    DECLARE @SeqNum                   INT;

    SELECT @UserGUID = IdGUID, @UserEmail = Email
    FROM dbo.WN_Users WHERE Id = @UserId;

    SELECT @SpaceGUID = IdGUID,
           @RentAccountId = RentAccountId,
           @SecurityDepositAccountId = SecurityDepositAccountId
    FROM dbo.WN_Spaces WHERE Id = @SpaceId;

    SELECT TOP 1 @SpaceTypeName = sc.SpaceCategory
    FROM dbo.WN_SpaceConfig sc
    JOIN dbo.WN_SpaceTypes st ON st.Id = sc.SpaceTypeId
    JOIN dbo.WN_Spaces s ON s.SpaceTypeId = st.IdGUID
    WHERE s.Id = @SpaceId;

    IF @SecurityDepositAccountId IS NOT NULL
        SELECT TOP 1 @SecurityDeposit = ISNULL(SecurityDeposit, 0)
        FROM dbo.WN_SpaceConfig
        WHERE SpaceCategory = @SpaceTypeName;

    SET @DatePart = CONVERT(NVARCHAR(8), GETDATE(), 112);
    SELECT @SeqNum = COUNT(*) + 1
    FROM dbo.WN_Bookings
    WHERE CONVERT(NVARCHAR(8), ISNULL(BookingDate, CreatedOn), 112) = @DatePart;
    SET @ChallanNumber = 'WN-' + @DatePart + '-' + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);
    SET @ValidityDate  = DATEADD(DAY, 5, GETDATE());

    INSERT INTO dbo.WN_Bookings
        (IdGUID, BookingDate, UserGuid, SpaceGuid,
         StartDateTime, EndDateTime, TotalAmount,
         BookingStatus, Status, Notes, CustomerCode,
         BankAccountId, SecurityDepositAccountId,
         ChallanNumber, ValidityDate,
         CreatedOn, CreatedBy)
    VALUES
        (@NewBookingGUID, GETDATE(), @UserGUID, @SpaceGUID,
         @StartDateTime, @EndDateTime, @TotalAmount,
         1, 1, @Notes, @CustomerCode,
         @RentAccountId, @SecurityDepositAccountId,
         @ChallanNumber, @ValidityDate,
         GETDATE(), @UserGUID);

    SET @NewId = SCOPE_IDENTITY();

    EXEC dbo.WN_BookingDetails_InsertLine
        @BookingGuid = @NewBookingGUID,
        @FeeType     = 'RoomRent',
        @Amount      = @TotalAmount,
        @AccountId   = @RentAccountId,
        @CreatedBy   = @UserEmail;

    IF @SecurityDeposit > 0 AND @SecurityDepositAccountId IS NOT NULL
        EXEC dbo.WN_BookingDetails_InsertLine
            @BookingGuid = @NewBookingGUID,
            @FeeType     = 'SecurityDeposit',
            @Amount      = @SecurityDeposit,
            @AccountId   = @SecurityDepositAccountId,
            @CreatedBy   = @UserEmail;

    SELECT @NewId                    AS NewId,
           @NewBookingGUID           AS IdGUID,
           @RentAccountId            AS RentAccountId,
           @SecurityDepositAccountId AS SecurityDepositAccountId,
           @ChallanNumber            AS ChallanNumber,
           @ValidityDate             AS Validity,
           @SecurityDeposit          AS SecurityDeposit;
END
GO

PRINT 'Both SPs fixed successfully.';
GO
