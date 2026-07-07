-- ============================================================
-- WorkNest Missing Stored Procedures
-- Schema verified from sys.columns output.
-- ============================================================

-- ── WN_Users_GetByGuid ────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.WN_Users_GetByGuid', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Users_GetByGuid;
GO

CREATE PROCEDURE dbo.WN_Users_GetByGuid
    @IdGUID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            u.Id,
            u.IdGUID,
            u.Email,
            u.Name,
            u.PhoneNumber,
            u.CreatedOn,
            u.RoleId,
            u.CompanyId,
            u.Status
        FROM dbo.WN_Users u WITH (NOLOCK)
        WHERE u.IdGUID = @IdGUID;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

-- ── WN_Users_GetById ──────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.WN_Users_GetById', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Users_GetById;
GO

CREATE PROCEDURE dbo.WN_Users_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            u.Id,
            u.IdGUID,
            u.Email,
            u.Name,
            u.PhoneNumber,
            u.CreatedOn,
            u.RoleId,
            u.CompanyId,
            u.Status
        FROM dbo.WN_Users u WITH (NOLOCK)
        WHERE u.Id = @Id;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

-- ── WN_Users_Delete (soft delete: Status = 0) ─────────────────────────────────
IF OBJECT_ID('dbo.WN_Users_Delete', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Users_Delete;
GO

CREATE PROCEDURE dbo.WN_Users_Delete
    @IdGUID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
            UPDATE dbo.WN_Users
            SET    Status    = 0,
                   UpdatedOn = GETDATE()
            WHERE  IdGUID = @IdGUID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- ── WN_Users_SetStatus ────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.WN_Users_SetStatus', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Users_SetStatus;
GO

CREATE PROCEDURE dbo.WN_Users_SetStatus
    @IdGUID   UNIQUEIDENTIFIER,
    @IsActive INT                  -- 1 = active, 0 = inactive
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
            UPDATE dbo.WN_Users
            SET    Status    = @IsActive,
                   UpdatedOn = GETDATE()
            WHERE  IdGUID = @IdGUID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- ── WN_Users_SetRole ─────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.WN_Users_SetRole', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Users_SetRole;
GO

CREATE PROCEDURE dbo.WN_Users_SetRole
    @IdGUID UNIQUEIDENTIFIER,
    @RoleId INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
            UPDATE dbo.WN_Users
            SET    RoleId    = @RoleId,
                   UpdatedOn = GETDATE()
            WHERE  IdGUID = @IdGUID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- ── WN_Spaces_GetByGuid ───────────────────────────────────────────────────────
-- WN_Spaces.LocationId  = UNIQUEIDENTIFIER (FK to WN_Locations.IdGUID)
-- WN_Spaces.SpaceTypeId = UNIQUEIDENTIFIER (FK to WN_SpaceTypes.IdGUID)
-- WN_SpaceTypes has no Name column — uses Description
IF OBJECT_ID('dbo.WN_Spaces_GetByGuid', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Spaces_GetByGuid;
GO

CREATE PROCEDURE dbo.WN_Spaces_GetByGuid
    @IdGUID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            s.Id,
            s.IdGUID,
            s.Name,
            s.Code,
            s.Description,
            s.LocationId,
            s.SpaceTypeId,
            s.FloorId,
            s.PricePerDay,
            s.PricePerHour,
            s.ImageUrl,
            s.Amenities,
            s.Status,
            l.Name            AS LocationName,
            st.Description    AS SpaceTypeName
        FROM dbo.WN_Spaces s WITH (NOLOCK)
        LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.IdGUID  = s.LocationId
        LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.IdGUID = s.SpaceTypeId
        WHERE s.IdGUID = @IdGUID;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

-- ── WN_Bookings_GetBySpaceGuid ────────────────────────────────────────────────
-- WN_Bookings.SpaceGuid = UNIQUEIDENTIFIER (direct FK to WN_Spaces.IdGUID)
-- WN_Bookings.UserGuid  = UNIQUEIDENTIFIER (direct FK to WN_Users.IdGUID)
IF OBJECT_ID('dbo.WN_Bookings_GetBySpaceGuid', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Bookings_GetBySpaceGuid;
GO

CREATE PROCEDURE dbo.WN_Bookings_GetBySpaceGuid
    @SpaceGuid UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            b.Id,
            b.IdGUID,
            b.UserGuid,
            b.SpaceGuid,
            b.StartDateTime,
            b.EndDateTime,
            b.TotalAmount,
            b.Notes,
            b.BookingStatus,
            b.Status,
            b.CreatedOn,
            s.Name                                              AS SpaceName,
            DATEDIFF(DAY, b.StartDateTime, b.EndDateTime)       AS ReservedDays
        FROM dbo.WN_Bookings b WITH (NOLOCK)
        INNER JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.IdGUID = b.SpaceGuid
        WHERE b.SpaceGuid = @SpaceGuid;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

-- ── WN_Bookings_GetByGuid ─────────────────────────────────────────────────────
IF OBJECT_ID('dbo.WN_Bookings_GetByGuid', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Bookings_GetByGuid;
GO

CREATE PROCEDURE dbo.WN_Bookings_GetByGuid
    @IdGUID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            b.Id,
            b.IdGUID,
            b.UserGuid,
            b.SpaceGuid,
            b.StartDateTime,
            b.EndDateTime,
            b.TotalAmount,
            b.Notes,
            b.BookingStatus,
            b.Status,
            b.CreatedOn
        FROM dbo.WN_Bookings b WITH (NOLOCK)
        WHERE b.IdGUID = @IdGUID;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

-- ── WN_Memberships_GetByPlanId ────────────────────────────────────────────────
IF OBJECT_ID('dbo.WN_Memberships_GetByPlanId', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Memberships_GetByPlanId;
GO

CREATE PROCEDURE dbo.WN_Memberships_GetByPlanId
    @PlanId INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            m.*,
            u.Email AS UserEmail,
            p.Name  AS PlanName,
            p.Price AS PlanPrice
        FROM dbo.WN_Memberships m WITH (NOLOCK)
        LEFT JOIN dbo.WN_Users        u WITH (NOLOCK) ON u.Id = m.UserId
        LEFT JOIN dbo.WN_PricingPlans p WITH (NOLOCK) ON p.Id = m.PlanId
        WHERE m.PlanId = @PlanId;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

-- ── WN_Payments_GetByMembershipId ─────────────────────────────────────────────
-- WN_Payments.UserId    = UNIQUEIDENTIFIER
-- WN_Payments.BookingId = UNIQUEIDENTIFIER
-- WN_Payments date cols = PaidAt (datetime2), CreatedAt (datetime2)
IF OBJECT_ID('dbo.WN_Payments_GetByMembershipId', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Payments_GetByMembershipId;
GO

CREATE PROCEDURE dbo.WN_Payments_GetByMembershipId
    @MembershipId INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            p.Id,
            p.IdGUID,
            p.UserId,
            p.BookingId,
            p.MembershipId,
            p.Amount,
            p.Currency,
            p.PaymentMethod,
            p.PaymentStatus,
            p.TransactionRef,
            p.PaidAt,
            p.CreatedAt
        FROM dbo.WN_Payments p WITH (NOLOCK)
        WHERE p.MembershipId = @MembershipId;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

-- ── WN_Payments_UpdateStatusByGuid ───────────────────────────────────────────
IF OBJECT_ID('dbo.WN_Payments_UpdateStatusByGuid', 'P') IS NOT NULL
    DROP PROCEDURE dbo.WN_Payments_UpdateStatusByGuid;
GO

CREATE PROCEDURE dbo.WN_Payments_UpdateStatusByGuid
    @IdGUID UNIQUEIDENTIFIER,
    @Status NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
            UPDATE dbo.WN_Payments
            SET    PaymentStatus = @Status
            WHERE  IdGUID = @IdGUID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
