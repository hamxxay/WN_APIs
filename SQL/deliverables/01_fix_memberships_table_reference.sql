USE [SAC400]
GO

-- ============================================================
-- Stage 1: Fix WN_Memberships table reference bug
-- Both procs referenced dbo.WN_Memberships which does not exist.
-- The real table is dbo.WN_MemberShips_Temp.
-- WN_Memberships_GetList also referenced m.IdGUID and m.UserGuid
-- which do not exist on WN_MemberShips_Temp — corrected to use
-- the actual columns (Id, UserId) with appropriate joins.
-- Script is idempotent (CREATE OR ALTER).
-- ============================================================

-- ── 1. WN_Memberships_GetByPlanId ────────────────────────────────────────────
-- Change: dbo.WN_Memberships → dbo.WN_MemberShips_Temp
-- All parameters, result-set columns, joins, and logic preserved exactly.
CREATE OR ALTER PROCEDURE [dbo].[WN_Memberships_GetByPlanId]
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
        FROM dbo.WN_MemberShips_Temp m WITH (NOLOCK)
        LEFT JOIN dbo.WN_Users        u WITH (NOLOCK) ON u.Id = m.UserId
        LEFT JOIN dbo.WN_PricingPlans p WITH (NOLOCK) ON p.Id = m.PlanId
        WHERE m.PlanId = @PlanId;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

-- ── 2. WN_Memberships_GetList ─────────────────────────────────────────────────
-- Change: dbo.WN_Memberships → dbo.WN_MemberShips_Temp
-- Secondary fix: m.IdGUID → m.Id (WN_MemberShips_Temp has no IdGUID column)
--                m.UserGuid → u.IdGUID (join via m.UserId → WN_Users.Id)
-- Output column aliases preserved exactly as the original intended:
--   IdGuid, NumericId, UserEmail, PlanName, PlanPrice, PlanCycle,
--   StartDate, EndDate, Status
CREATE OR ALTER PROCEDURE [dbo].[WN_Memberships_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        CAST(m.Id AS NVARCHAR(36)) AS IdGuid,   -- WN_MemberShips_Temp has no IdGUID; expose Id cast to preserve column name contract
        m.Id                       AS NumericId,
        u.Email                    AS UserEmail,
        pp.Name                    AS PlanName,
        pp.Price                   AS PlanPrice,
        pp.BillingCycle            AS PlanCycle,
        m.StartDate                AS StartDate,
        m.EndDate                  AS EndDate,
        m.Status                   AS Status
    FROM dbo.WN_MemberShips_Temp  m  WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users        u  WITH (NOLOCK) ON u.Id  = m.UserId
    LEFT JOIN dbo.WN_PricingPlans pp WITH (NOLOCK) ON pp.Id = m.PlanId
    WHERE m.Status != 0
    ORDER BY m.StartDate DESC;
END
GO

PRINT 'Stage 1 complete: WN_Memberships_GetByPlanId and WN_Memberships_GetList fixed.';
GO
