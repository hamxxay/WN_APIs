USE [SAC400]
GO
/****** Object:  Table [dbo].[WN_BillingPeriods]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BillingPeriods](
	[Id] [tinyint] IDENTITY(1,1) NOT NULL,
	[Code] [nvarchar](20) NOT NULL,
	[Label] [nvarchar](50) NOT NULL,
	[DurationDays] [int] NULL,
	[SortOrder] [tinyint] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_WN_BillingPeriods] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_BillingPeriods_Code] UNIQUE NONCLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_PricingTierTypes]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_PricingTierTypes](
	[Id] [tinyint] IDENTITY(1,1) NOT NULL,
	[Code] [nvarchar](30) NOT NULL,
	[Label] [nvarchar](100) NOT NULL,
	[Description] [nvarchar](500) NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_WN_PricingTierTypes] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_PricingTierTypes_Code] UNIQUE NONCLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Spaces]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Spaces](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Code] [nvarchar](50) NULL,
	[LocationId] [uniqueidentifier] NULL,
	[SpaceTypeId] [uniqueidentifier] NULL,
	[Name] [nvarchar](max) NULL,
	[Description] [nvarchar](max) NULL,
	[PricePerHour] [decimal](18, 2) NULL,
	[PricePerDay] [decimal](18, 2) NULL,
	[PricePerMonth] [decimal](18, 2) NULL,
	[ImageUrl] [nvarchar](max) NULL,
	[Amenities] [nvarchar](max) NULL,
	[Status] [int] NULL,
	[CreatedOn] [datetime] NULL,
	[CreatedBy] [uniqueidentifier] NULL,
	[UpdatedOn] [datetime] NULL,
	[UpdatedBy] [uniqueidentifier] NULL,
	[FloorId] [int] NULL,
	[RentAccountId] [int] NULL,
	[SecurityDepositAccountId] [int] NULL,
	[IsActive] [bit] NOT NULL,
	[Capacity] [smallint] NOT NULL,
	[UpdatedById] [int] NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[LocationIdInt] [int] NULL,
	[SpaceTypeIdInt] [int] NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_Spaces] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SpacePricing]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_SpacePricing](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[SpaceId] [int] NOT NULL,
	[BillingPeriodId] [tinyint] NOT NULL,
	[TierTypeId] [tinyint] NOT NULL,
	[SeatPrice] [decimal](18, 4) NOT NULL,
	[SecurityDeposit] [decimal](18, 4) NOT NULL,
	[RentAccountId] [int] NULL,
	[DepositAccountId] [int] NULL,
	[CurrencyCode] [nvarchar](10) NOT NULL,
	[EffectiveFrom] [date] NOT NULL,
	[EffectiveTo] [date] NULL,
	[IsActive] [bit] NOT NULL,
	[Notes] [nvarchar](500) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_SpacePricing] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_SpacePricing_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  View [dbo].[VW_WN_SpacePricingActive]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   VIEW [dbo].[VW_WN_SpacePricingActive]
AS
    SELECT
        sp.Id                        AS PricingId,
        sp.SpaceId,
        s.Code                       AS SpaceCode,
        s.Name                       AS SpaceName,
        s.Capacity,
        sp.BillingPeriodId,
        bp.Code                      AS BillingPeriodCode,
        bp.Label                     AS BillingPeriodLabel,
        sp.TierTypeId,
        tt.Code                      AS TierCode,
        sp.SeatPrice,
        sp.SeatPrice * s.Capacity    AS RoomPrice,
        sp.SecurityDeposit,
        sp.RentAccountId,
        sp.DepositAccountId,
        sp.CurrencyCode,
        sp.EffectiveFrom,
        sp.EffectiveTo
    FROM  [dbo].[WN_SpacePricing]     sp
    JOIN  [dbo].[WN_Spaces]           s  ON s.Id  = sp.SpaceId
    JOIN  [dbo].[WN_BillingPeriods]   bp ON bp.Id = sp.BillingPeriodId
    JOIN  [dbo].[WN_PricingTierTypes] tt ON tt.Id = sp.TierTypeId
    WHERE sp.IsActive = 1
      AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
      AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE));
GO
/****** Object:  Table [dbo].[WN_Locations]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Locations](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Name] [nvarchar](max) NULL,
	[Address] [nvarchar](max) NULL,
	[City] [nvarchar](100) NULL,
	[OpeningTime] [nvarchar](10) NULL,
	[ClosingTime] [nvarchar](10) NULL,
	[Status] [int] NULL,
	[CreatedOn] [datetime] NULL,
	[CreatedBy] [uniqueidentifier] NULL,
	[UpdatedOn] [datetime] NULL,
	[UpdatedBy] [uniqueidentifier] NULL,
	[BranchId] [int] NULL,
	[CityId] [int] NULL,
	[CompanyId] [int] NULL,
	[IsActive] [bit] NOT NULL,
	[UpdatedById] [int] NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[Latitude] [decimal](10, 7) NULL,
	[Longitude] [decimal](10, 7) NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_Locations] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Bookings]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Bookings](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NOT NULL,
	[BookingDate] [datetime] NULL,
	[UserGuid] [uniqueidentifier] NULL,
	[CustomerCode] [nvarchar](20) NULL,
	[SpaceGuid] [uniqueidentifier] NULL,
	[StartDateTime] [datetime] NULL,
	[EndDateTime] [datetime] NULL,
	[TotalAmount] [decimal](18, 2) NULL,
	[BookingStatus] [int] NULL,
	[BankAccountId] [int] NULL,
	[SecurityDepositAccountId] [int] NULL,
	[Status] [int] NULL,
	[Notes] [nvarchar](max) NULL,
	[RejectReason] [nvarchar](max) NULL,
	[LogApiId] [uniqueidentifier] NULL,
	[HubConsumerNo] [nvarchar](max) NULL,
	[HubActionSatus] [int] NULL,
	[HubActionDate] [datetime] NULL,
	[HubActionPaidAmount] [decimal](18, 4) NULL,
	[HubActionById] [int] NULL,
	[BatchSyncStatus] [int] NULL,
	[BatchSyncDate] [datetime] NULL,
	[CreatedOn] [datetime] NULL,
	[UpdatedOn] [datetime] NULL,
	[CreatedBy] [uniqueidentifier] NULL,
	[UpdatedBy] [uniqueidentifier] NULL,
	[TransactionDate] [datetime] NULL,
	[ChallanNumber] [nvarchar](50) NULL,
	[ValidityDate] [datetime] NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[UserId] [int] NULL,
	[SpaceId] [int] NULL,
	[PricingId] [int] NULL,
	[StartOn] [datetime2](7) NULL,
	[EndOn] [datetime2](7) NULL,
	[BookingStatusId] [tinyint] NOT NULL,
	[CancelReason] [nvarchar](500) NULL,
	[IsDeleted] [bit] NOT NULL,
	[CreatedById] [int] NULL,
	[UpdatedById] [int] NULL,
 CONSTRAINT [PK_WN_Bookings_1] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Challans]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Challans](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[BookingId] [int] NOT NULL,
	[ChallanNumber] [nvarchar](50) NOT NULL,
	[IssuedOn] [datetime2](7) NOT NULL,
	[ValidUntil] [date] NOT NULL,
	[StatusId] [tinyint] NOT NULL,
	[Notes] [nvarchar](500) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_Challans] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Challans_ChallanNumber] UNIQUE NONCLUSTERED 
(
	[ChallanNumber] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Challans_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Companies]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Companies](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[Name] [nvarchar](200) NOT NULL,
	[LegalName] [nvarchar](200) NULL,
	[TaxNumber] [nvarchar](50) NULL,
	[Email] [nvarchar](256) NULL,
	[Phone] [nvarchar](50) NULL,
	[Address] [nvarchar](500) NULL,
	[CityId] [int] NULL,
	[LogoUrl] [nvarchar](500) NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[UpdatedOn] [datetime2](7) NULL,
 CONSTRAINT [PK_WN_Companies] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Companies_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Branches]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Branches](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[CompanyId] [int] NOT NULL,
	[Name] [nvarchar](200) NOT NULL,
	[Code] [nvarchar](20) NULL,
	[CityId] [int] NULL,
	[Address] [nvarchar](500) NULL,
	[Phone] [nvarchar](50) NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[UpdatedOn] [datetime2](7) NULL,
 CONSTRAINT [PK_WN_Branches] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Branches_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookingStatuses]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BookingStatuses](
	[Id] [tinyint] IDENTITY(1,1) NOT NULL,
	[Code] [nvarchar](20) NOT NULL,
	[Label] [nvarchar](50) NOT NULL,
 CONSTRAINT [PK_WN_BookingStatuses] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_BookingStatuses_Code] UNIQUE NONCLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SpaceTypes]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_SpaceTypes](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Description] [nvarchar](max) NULL,
	[Capacity] [int] NULL,
	[HourlyAllowed] [bit] NULL,
	[Status] [int] NULL,
	[CreatedOn] [datetime] NULL,
	[CreatedBy] [int] NULL,
	[UpdatedOn] [datetime] NULL,
	[UpdatedBy] [int] NULL,
	[RentAccountId] [int] NULL,
	[SecurityDepositAccountId] [int] NULL,
	[CategoryId] [tinyint] NULL,
	[Name] [nvarchar](100) NULL,
	[IsActive] [bit] NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
 CONSTRAINT [PK_WN_SpaceTypes] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Users]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Users](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Name] [nvarchar](250) NULL,
	[UserName] [nvarchar](250) NULL,
	[RoleId] [int] NULL,
	[Email] [nvarchar](256) NULL,
	[PhoneNumber] [nvarchar](max) NULL,
	[LockoutEnd] [datetimeoffset](7) NULL,
	[LockoutEnabled] [bit] NULL,
	[CreatedOn] [datetime] NULL,
	[CreatedBy] [uniqueidentifier] NULL,
	[UpdatedBy] [uniqueidentifier] NULL,
	[UpdatedOn] [datetime] NULL,
	[CompanyId] [int] NULL,
	[Status] [int] NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[PasswordHash] [nvarchar](500) NULL,
	[IsActive] [bit] NOT NULL,
	[Address] [nvarchar](500) NULL,
	[CnicOrPassport] [nvarchar](50) NULL,
	[AvatarUrl] [nvarchar](500) NULL,
	[Notes] [nvarchar](1000) NULL,
	[CreatedById] [int] NULL,
	[CityId] [int] NULL,
 CONSTRAINT [PK_WN_Users] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  View [dbo].[VW_WN_BookingSummary]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   VIEW [dbo].[VW_WN_BookingSummary]
AS
    SELECT
        b.Id                      AS BookingId,
        b.PublicId                AS BookingPublicId,
        b.StartOn,
        b.EndOn,
        b.BookingStatusId,
        bs.Code                   AS BookingStatusCode,
        bs.Label                  AS BookingStatusLabel,
        b.Notes,
        b.CancelReason,
        b.CreatedOn               AS BookedOn,
        u.Id                      AS UserId,
        u.PublicId                AS UserPublicId,
        u.Name                    AS UserName,
        u.Email                   AS UserEmail,
        s.Id                      AS SpaceId,
        s.PublicId                AS SpacePublicId,
        s.Code                    AS SpaceCode,
        s.Name                    AS SpaceName,
        s.Capacity                AS SpaceCapacity,
        st.Id                     AS SpaceTypeId,
        st.Name                   AS SpaceTypeName,
        l.Id                      AS LocationId,
        l.Name                    AS LocationName,
        br.Id                     AS BranchId,
        br.Name                   AS BranchName,
        co.Id                     AS CompanyId,
        co.Name                   AS CompanyName,
        sp.SeatPrice,
        sp.SeatPrice * s.Capacity AS RoomPrice,
        sp.SecurityDeposit,
        bp.Code                   AS BillingPeriodCode,
        bp.Label                  AS BillingPeriodLabel,
        ch.ChallanNumber,
        ch.ValidUntil             AS ChallanValidUntil,
        ch.StatusId               AS ChallanStatusId
    FROM       [dbo].[WN_Bookings]        b
    JOIN       [dbo].[WN_Users]           u  ON u.Id  = b.UserId
    JOIN       [dbo].[WN_Spaces]          s  ON s.Id  = b.SpaceId
    JOIN       [dbo].[WN_SpaceTypes]      st ON st.Id = s.SpaceTypeIdInt
    JOIN       [dbo].[WN_Locations]       l  ON l.Id  = s.LocationIdInt
    JOIN       [dbo].[WN_Branches]        br ON br.Id = l.BranchId
    JOIN       [dbo].[WN_Companies]       co ON co.Id = br.CompanyId
    JOIN       [dbo].[WN_SpacePricing]    sp ON sp.Id = b.PricingId
    JOIN       [dbo].[WN_BillingPeriods]  bp ON bp.Id = sp.BillingPeriodId
    JOIN       [dbo].[WN_BookingStatuses] bs ON bs.Id = b.BookingStatusId
    OUTER APPLY (
        SELECT TOP 1 ChallanNumber, ValidUntil, StatusId
        FROM [dbo].[WN_Challans]
        WHERE BookingId = b.Id
        ORDER BY CreatedOn DESC
    ) ch
    WHERE b.IsDeleted = 0;
GO
/****** Object:  Table [dbo].[WN_Amenities]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Amenities](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[Status] [int] NOT NULL,
	[Icon] [nvarchar](100) NULL,
	[IsActive] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AmountFields]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AmountFields](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Entity] [nvarchar](100) NOT NULL,
	[Field] [nvarchar](100) NOT NULL,
	[Label] [nvarchar](200) NOT NULL,
	[Currency] [nvarchar](10) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_AmountFields] UNIQUE NONCLUSTERED 
(
	[Entity] ASC,
	[Field] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookingDetails]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BookingDetails](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NOT NULL,
	[BookingGuid] [uniqueidentifier] NOT NULL,
	[FeeType] [nvarchar](50) NOT NULL,
	[Amount] [decimal](18, 2) NOT NULL,
	[AccountId] [int] NULL,
	[CreatedOn] [datetime] NOT NULL,
	[CreatedBy] [nvarchar](100) NULL,
	[IsDeleted] [bit] NOT NULL,
	[CustomerCode] [nvarchar](50) NULL,
	[CustomerName] [nvarchar](255) NULL,
	[CustomerEmail] [nvarchar](255) NULL,
	[SpaceName] [nvarchar](255) NULL,
	[SpaceCode] [nvarchar](50) NULL,
	[SpaceCategory] [nvarchar](50) NULL,
	[StartDateTime] [datetime] NULL,
	[EndDateTime] [datetime] NULL,
	[RentAmount] [decimal](18, 2) NULL,
	[SecurityDeposit] [decimal](18, 2) NULL,
	[TotalAmount] [decimal](18, 2) NULL,
	[RentAccountId] [int] NULL,
	[DepositAccountId] [int] NULL,
	[PaymentMethod] [nvarchar](50) NULL,
	[Notes] [nvarchar](max) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookingLines]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BookingLines](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[BookingId] [int] NOT NULL,
	[ChargeTypeId] [tinyint] NOT NULL,
	[Description] [nvarchar](200) NULL,
	[Quantity] [decimal](10, 4) NOT NULL,
	[UnitPrice] [decimal](18, 4) NOT NULL,
	[DiscountAmount] [decimal](18, 4) NOT NULL,
	[TaxRate] [decimal](6, 4) NOT NULL,
	[TaxAmount]  AS (round(([Quantity]*[UnitPrice]-[DiscountAmount])*[TaxRate],(4))) PERSISTED,
	[LineTotal]  AS (round(([Quantity]*[UnitPrice]-[DiscountAmount])+round(([Quantity]*[UnitPrice]-[DiscountAmount])*[TaxRate],(4)),(4))) PERSISTED,
	[AccountId] [int] NULL,
	[IsDeleted] [bit] NOT NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_BookingLines] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookTour]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BookTour](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Name] [nvarchar](max) NULL,
	[Email] [nvarchar](100) NULL,
	[Message] [nvarchar](max) NULL,
	[PhoneNumber] [nvarchar](100) NULL,
	[CreatedOn] [datetime] NULL,
	[CreatedBy] [uniqueidentifier] NULL,
	[UpdatedOn] [datetime] NULL,
	[UpdatedBy] [uniqueidentifier] NULL,
	[Status] [int] NULL,
 CONSTRAINT [PK__WN_BookT__3214EC070BEA22C8] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ChallanAuditLog]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ChallanAuditLog](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ChallanId] [int] NOT NULL,
	[OldValidUntil] [date] NULL,
	[NewValidUntil] [date] NOT NULL,
	[UpdatedById] [int] NOT NULL,
	[UpdatedOn] [datetime2](7) NOT NULL,
	[Remarks] [nvarchar](500) NULL,
 CONSTRAINT [PK_WN_ChallanAuditLog] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ChallanCounter]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ChallanCounter](
	[CounterDate] [date] NOT NULL,
	[LastNumber] [int] NOT NULL,
 CONSTRAINT [PK_WN_ChallanCounter] PRIMARY KEY CLUSTERED 
(
	[CounterDate] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ChallanValidityLog]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ChallanValidityLog](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[BookingId] [int] NOT NULL,
	[OldExpiryDate] [date] NULL,
	[NewExpiryDate] [date] NOT NULL,
	[UpdatedBy] [nvarchar](200) NOT NULL,
	[UpdatedAt] [datetime] NOT NULL,
	[Remarks] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ChargeTypes]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ChargeTypes](
	[Id] [tinyint] IDENTITY(1,1) NOT NULL,
	[Code] [nvarchar](30) NOT NULL,
	[Label] [nvarchar](100) NOT NULL,
	[IsDebit] [bit] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_WN_ChargeTypes] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_ChargeTypes_Code] UNIQUE NONCLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Cities]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Cities](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
	[Status] [int] NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[CountryId] [int] NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Company]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Company](
	[Id] [nchar](10) NULL
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Contacts]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Contacts](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[ContactType] [nvarchar](20) NOT NULL,
	[UserId] [int] NULL,
	[Name] [nvarchar](200) NOT NULL,
	[Email] [nvarchar](256) NOT NULL,
	[PhoneNumber] [nvarchar](50) NULL,
	[Message] [nvarchar](max) NULL,
	[StatusId] [tinyint] NOT NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[UpdatedOn] [datetime2](7) NULL,
	[UpdatedById] [int] NULL,
 CONSTRAINT [PK_WN_Contacts] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Contacts_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Countries]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Countries](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[IsoCode2] [nchar](2) NOT NULL,
	[IsoCode3] [nchar](3) NOT NULL,
	[CurrencyCode] [nvarchar](10) NOT NULL,
	[PhoneCode] [nvarchar](10) NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_Countries] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Countries_IsoCode2] UNIQUE NONCLUSTERED 
(
	[IsoCode2] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Countries_IsoCode3] UNIQUE NONCLUSTERED 
(
	[IsoCode3] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Countries_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Customers]    Script Date: 10/08/2026 10:45:42 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Customers](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NOT NULL,
	[Code]  AS ('WN'+right('00000'+CONVERT([varchar](10),[Id]),(5))) PERSISTED,
	[FirstName] [nvarchar](100) NOT NULL,
	[LastName] [nvarchar](100) NULL,
	[Email] [nvarchar](255) NOT NULL,
	[PhoneNumber] [nvarchar](50) NULL,
	[CnicOrPassport] [nvarchar](50) NULL,
	[Address] [nvarchar](500) NULL,
	[CityId] [int] NULL,
	[IsActive] [bit] NOT NULL,
	[Notes] [nvarchar](1000) NULL,
	[CreatedAt] [datetime] NOT NULL,
	[UpdatedAt] [datetime] NULL,
	[CreatedBy] [nvarchar](255) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Customers_Code] UNIQUE NONCLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Customers_Email] UNIQUE NONCLUSTERED 
(
	[Email] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_EmailOtps_Temp]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_EmailOtps_Temp](
	[Id] [uniqueidentifier] NOT NULL,
	[Email] [nvarchar](256) NOT NULL,
	[Purpose] [nvarchar](60) NOT NULL,
	[CodeHash] [nvarchar](max) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[ExpiresAt] [datetime2](7) NOT NULL,
	[UsedAt] [datetime2](7) NULL,
	[IdentityToken] [nvarchar](max) NULL,
 CONSTRAINT [PK_WN_EmailOtps] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Floors]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Floors](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[LocationId] [int] NOT NULL,
	[FloorName] [nvarchar](100) NOT NULL,
	[Status] [tinyint] NOT NULL,
	[CreatedOn] [datetime] NULL,
	[IsActive] [bit] NOT NULL,
	[FloorNumber] [smallint] NOT NULL,
	[Name] [nvarchar](100) NULL,
	[CreatedById] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_GalleryImages]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_GalleryImages](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[LocationIdGuid] [uniqueidentifier] NULL,
	[Title] [nvarchar](150) NULL,
	[Description] [nvarchar](max) NULL,
	[ImageUrl] [nvarchar](max) NULL,
	[Status] [int] NULL,
	[CreatedOn] [datetime] NULL,
	[CreatedBy] [uniqueidentifier] NULL,
	[UpdatedOn] [datetime] NULL,
	[UpdatedBy] [uniqueidentifier] NULL,
	[IsActive] [bit] NULL,
	[SortOrder] [int] NULL,
	[LocationId] [int] NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[SpaceId] [int] NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_GalleryImages] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_IntegrationLog]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_IntegrationLog](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[EntityType] [nvarchar](50) NOT NULL,
	[EntityId] [int] NOT NULL,
	[System] [nvarchar](50) NOT NULL,
	[EventType] [nvarchar](50) NOT NULL,
	[StatusId] [tinyint] NOT NULL,
	[ExternalRef] [nvarchar](200) NULL,
	[RequestBody] [nvarchar](max) NULL,
	[ResponseBody] [nvarchar](max) NULL,
	[PaidAmount] [decimal](18, 4) NULL,
	[ProcessedById] [int] NULL,
	[ProcessedOn] [datetime2](7) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_IntegrationLog] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_InvoiceLines]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_InvoiceLines](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[InvoiceId] [int] NOT NULL,
	[ChargeTypeId] [tinyint] NOT NULL,
	[Description] [nvarchar](200) NULL,
	[Quantity] [decimal](10, 4) NOT NULL,
	[UnitPrice] [decimal](18, 4) NOT NULL,
	[DiscountAmount] [decimal](18, 4) NOT NULL,
	[TaxRate] [decimal](6, 4) NOT NULL,
	[TaxAmount]  AS (round(([Quantity]*[UnitPrice]-[DiscountAmount])*[TaxRate],(4))) PERSISTED,
	[LineTotal]  AS (round(([Quantity]*[UnitPrice]-[DiscountAmount])+round(([Quantity]*[UnitPrice]-[DiscountAmount])*[TaxRate],(4)),(4))) PERSISTED,
	[AccountId] [int] NULL,
	[SortOrder] [smallint] NOT NULL,
 CONSTRAINT [PK_WN_InvoiceLines] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Invoices]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Invoices](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[InvoiceNumber] [nvarchar](50) NOT NULL,
	[UserId] [int] NOT NULL,
	[BookingId] [int] NULL,
	[MembershipId] [int] NULL,
	[IssuedOn] [date] NOT NULL,
	[DueOn] [date] NOT NULL,
	[SubTotal] [decimal](18, 4) NOT NULL,
	[DiscountTotal] [decimal](18, 4) NOT NULL,
	[TaxTotal] [decimal](18, 4) NOT NULL,
	[GrandTotal] [decimal](18, 4) NOT NULL,
	[PaidTotal] [decimal](18, 4) NOT NULL,
	[BalanceDue]  AS ([GrandTotal]-[PaidTotal]) PERSISTED,
	[CurrencyCode] [nvarchar](10) NOT NULL,
	[StatusId] [tinyint] NOT NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[UpdatedOn] [datetime2](7) NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_Invoices] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Invoices_Number] UNIQUE NONCLUSTERED 
(
	[InvoiceNumber] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Invoices_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_MembershipPlanFeatures]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_MembershipPlanFeatures](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[PlanId] [int] NOT NULL,
	[FeatureName] [nvarchar](120) NOT NULL,
	[FeatureValue] [nvarchar](120) NULL,
	[SortOrder] [smallint] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_WN_MembershipPlanFeatures] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_MembershipPlanFeatures_Pub] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_MembershipPlans]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_MembershipPlans](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[Name] [nvarchar](120) NOT NULL,
	[Description] [nvarchar](max) NULL,
	[BillingPeriodId] [tinyint] NOT NULL,
	[Price] [decimal](18, 4) NOT NULL,
	[IncludesHours] [int] NULL,
	[CurrencyCode] [nvarchar](10) NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[UpdatedOn] [datetime2](7) NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_MembershipPlans] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_MembershipPlans_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_MemberShips]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_MemberShips](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[UserId] [int] NOT NULL,
	[PlanId] [int] NOT NULL,
	[StartDate] [datetime2](7) NOT NULL,
	[EndDate] [datetime2](7) NULL,
	[Status] [nvarchar](20) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_Memberships] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_PaymentLines]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_PaymentLines](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PaymentId] [int] NOT NULL,
	[ChargeTypeId] [tinyint] NOT NULL,
	[Amount] [decimal](18, 4) NOT NULL,
	[AccountId] [int] NULL,
	[Notes] [nvarchar](200) NULL,
 CONSTRAINT [PK_WN_PaymentLines] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_PaymentMethods]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_PaymentMethods](
	[Id] [tinyint] IDENTITY(1,1) NOT NULL,
	[Code] [nvarchar](30) NOT NULL,
	[Label] [nvarchar](100) NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_WN_PaymentMethods] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_PaymentMethods_Code] UNIQUE NONCLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Payments]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Payments](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[MembershipId] [int] NULL,
	[Amount] [decimal](18, 2) NULL,
	[Currency] [nvarchar](10) NULL,
	[PaymentMethod] [nvarchar](50) NULL,
	[PaymentStatus] [nvarchar](20) NULL,
	[TransactionRef] [nvarchar](120) NULL,
	[PaidAt] [datetime2](7) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UserId] [uniqueidentifier] NULL,
	[BookingId] [uniqueidentifier] NULL,
	[AccountId] [int] NULL,
	[ExpiryDate] [datetime2](7) NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[InvoiceId] [int] NULL,
	[PaymentMethodId] [tinyint] NOT NULL,
	[CurrencyCode] [nvarchar](10) NOT NULL,
	[GatewayRef] [nvarchar](200) NULL,
	[StatusId] [tinyint] NOT NULL,
	[ExpiresOn] [datetime2](7) NULL,
	[PaidOn] [datetime2](7) NULL,
	[CreatedById] [int] NULL,
	[CreatedOn] [datetime2](7) NULL,
	[UpdatedOn] [datetime2](7) NULL,
	[Notes] [nvarchar](500) NULL,
	[UserIdInt] [int] NULL,
	[BookingIdInt] [int] NULL,
 CONSTRAINT [PK_WN_Payments] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_PlanFeatures]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_PlanFeatures](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[PlanId] [int] NOT NULL,
	[FeatureName] [nvarchar](120) NOT NULL,
	[FeatureValue] [nvarchar](120) NULL,
	[Status] [int] NULL,
 CONSTRAINT [PK_WN_PlanFeatures] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_PricingPlans]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_PricingPlans](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Name] [nvarchar](120) NULL,
	[Description] [nvarchar](max) NULL,
	[BillingCycle] [nvarchar](20) NULL,
	[Price] [decimal](18, 2) NULL,
	[IncludesHours] [int] NULL,
	[IsActive] [bit] NULL,
	[CreatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_WN_PricingPlans] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_PricingRules]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_PricingRules](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[RuleType] [nvarchar](30) NOT NULL,
	[SpaceId] [int] NULL,
	[SpaceTypeId] [int] NULL,
	[LocationId] [int] NULL,
	[BillingPeriodId] [tinyint] NULL,
	[DayOfWeek] [tinyint] NULL,
	[StartDate] [date] NULL,
	[EndDate] [date] NULL,
	[StartTime] [time](0) NULL,
	[EndTime] [time](0) NULL,
	[PromoCode] [nvarchar](50) NULL,
	[CompanyId] [int] NULL,
	[AdjustmentType] [nvarchar](20) NOT NULL,
	[AdjustmentValue] [decimal](10, 4) NOT NULL,
	[Priority] [smallint] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[EffectiveFrom] [date] NOT NULL,
	[EffectiveTo] [date] NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_PricingRules] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_PricingRules_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_RefreshTokens_Temp]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_RefreshTokens_Temp](
	[Id] [uniqueidentifier] NOT NULL,
	[UserId] [int] NOT NULL,
	[TokenHash] [nvarchar](450) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[ExpiresAt] [datetime2](7) NOT NULL,
	[RevokedAt] [datetime2](7) NULL,
	[ReplacedByTokenId] [uniqueidentifier] NULL,
 CONSTRAINT [PK_WN_RefreshTokens] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Refunds]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Refunds](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[PaymentId] [int] NOT NULL,
	[Amount] [decimal](18, 4) NOT NULL,
	[Reason] [nvarchar](500) NULL,
	[StatusId] [tinyint] NOT NULL,
	[ProcessedOn] [datetime2](7) NULL,
	[ProcessedById] [int] NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_Refunds] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Refunds_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SecurityDeposits]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_SecurityDeposits](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[BookingId] [int] NOT NULL,
	[UserId] [int] NOT NULL,
	[Amount] [decimal](18, 4) NOT NULL,
	[AccountId] [int] NULL,
	[StatusId] [tinyint] NOT NULL,
	[HeldOn] [datetime2](7) NOT NULL,
	[ReleasedOn] [datetime2](7) NULL,
	[ForfeitedOn] [datetime2](7) NULL,
	[ForfeitReason] [nvarchar](500) NULL,
	[Notes] [nvarchar](500) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[UpdatedById] [int] NULL,
 CONSTRAINT [PK_WN_SecurityDeposits] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_SecurityDeposits_PublicId] UNIQUE NONCLUSTERED 
(
	[PublicId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SpaceAmenities]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_SpaceAmenities](
	[SpaceId] [int] NOT NULL,
	[AmenityId] [int] NOT NULL,
 CONSTRAINT [PK_WN_SpaceAmenities] PRIMARY KEY CLUSTERED 
(
	[SpaceId] ASC,
	[AmenityId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SpaceCategories]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_SpaceCategories](
	[Id] [tinyint] IDENTITY(1,1) NOT NULL,
	[Code] [nvarchar](30) NOT NULL,
	[Label] [nvarchar](100) NOT NULL,
	[Description] [nvarchar](500) NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_WN_SpaceCategories] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_SpaceCategories_Code] UNIQUE NONCLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SpaceConfig]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_SpaceConfig](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[SpaceCategory] [nvarchar](20) NOT NULL,
	[TotalSpaces] [int] NOT NULL,
	[CodePrefix] [nvarchar](5) NOT NULL,
	[MinCode] [int] NOT NULL,
	[DefaultCapacities] [nvarchar](50) NULL,
	[OpeningTime] [nvarchar](5) NOT NULL,
	[ClosingTime] [nvarchar](5) NOT NULL,
	[UpdatedOn] [datetime] NULL,
	[UpdatedBy] [nvarchar](255) NULL,
	[SpaceTypeId] [int] NULL,
	[SecurityDeposit] [decimal](10, 2) NOT NULL,
	[SecurityAccountId] [int] NULL,
	[LocationId] [int] NULL,
	[RentAccountId] [int] NULL,
	[DepositAccountId] [int] NULL,
	[PricePerHour] [decimal](18, 2) NULL,
	[PricePerDay] [decimal](18, 2) NULL,
	[PricePerMonth] [decimal](18, 2) NULL,
	[Amenities] [nvarchar](max) NULL,
	[Status] [int] NOT NULL,
	[FloorId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SpaceInventoryConfig]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_SpaceInventoryConfig](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[LocationId] [int] NOT NULL,
	[SpaceTypeId] [int] NOT NULL,
	[CodePrefix] [nvarchar](10) NOT NULL,
	[MinCode] [int] NOT NULL,
	[TotalSpaces] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[UpdatedOn] [datetime2](7) NULL,
	[UpdatedById] [int] NULL,
 CONSTRAINT [PK_WN_SpaceInventoryConfig] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_SpaceInventoryConfig_Loc_Prefix] UNIQUE NONCLUSTERED 
(
	[LocationId] ASC,
	[CodePrefix] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_SpaceInventoryConfig_Loc_Type] UNIQUE NONCLUSTERED 
(
	[LocationId] ASC,
	[SpaceTypeId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[WN_Amenities] ADD  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Amenities] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_AmountFields] ADD  DEFAULT ('PKR') FOR [Currency]
GO
ALTER TABLE [dbo].[WN_BillingPeriods] ADD  DEFAULT ((0)) FOR [SortOrder]
GO
ALTER TABLE [dbo].[WN_BillingPeriods] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT (newid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT ((0)) FOR [Amount]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[WN_BookingLines] ADD  DEFAULT ((1)) FOR [Quantity]
GO
ALTER TABLE [dbo].[WN_BookingLines] ADD  DEFAULT ((0)) FOR [DiscountAmount]
GO
ALTER TABLE [dbo].[WN_BookingLines] ADD  DEFAULT ((0)) FOR [TaxRate]
GO
ALTER TABLE [dbo].[WN_BookingLines] ADD  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[WN_BookingLines] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_HubActionSatus]  DEFAULT ((2)) FOR [HubActionSatus]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_BatchSyncStatus]  DEFAULT ((0)) FOR [BatchSyncStatus]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  DEFAULT ((1)) FOR [BookingStatusId]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[WN_BookTour] ADD  CONSTRAINT [DF_WN_BookTour_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_BookTour] ADD  CONSTRAINT [DF_WN_BookTour_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Branches] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Branches] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Branches] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_ChallanAuditLog] ADD  DEFAULT (sysutcdatetime()) FOR [UpdatedOn]
GO
ALTER TABLE [dbo].[WN_Challans] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Challans] ADD  DEFAULT (sysutcdatetime()) FOR [IssuedOn]
GO
ALTER TABLE [dbo].[WN_Challans] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_Challans] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_ChallanValidityLog] ADD  DEFAULT (getutcdate()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[WN_ChargeTypes] ADD  DEFAULT ((1)) FOR [IsDebit]
GO
ALTER TABLE [dbo].[WN_ChargeTypes] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Cities] ADD  DEFAULT (newid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_Cities] ADD  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Cities] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Cities] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Cities] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Companies] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Companies] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Companies] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Contacts] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Contacts] ADD  DEFAULT ('Contact') FOR [ContactType]
GO
ALTER TABLE [dbo].[WN_Contacts] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_Contacts] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Countries] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Countries] ADD  DEFAULT (N'PKR') FOR [CurrencyCode]
GO
ALTER TABLE [dbo].[WN_Countries] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Countries] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Customers] ADD  DEFAULT (newid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_Customers] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Customers] ADD  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_Floors] ADD  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Floors] ADD  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Floors] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Floors] ADD  DEFAULT ((0)) FOR [FloorNumber]
GO
ALTER TABLE [dbo].[WN_GalleryImages] ADD  CONSTRAINT [DF_WN_GalleryImages_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_GalleryImages] ADD  CONSTRAINT [DF_WN_GalleryImages_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_GalleryImages] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_IntegrationLog] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_IntegrationLog] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_InvoiceLines] ADD  DEFAULT ((1)) FOR [Quantity]
GO
ALTER TABLE [dbo].[WN_InvoiceLines] ADD  DEFAULT ((0)) FOR [DiscountAmount]
GO
ALTER TABLE [dbo].[WN_InvoiceLines] ADD  DEFAULT ((0)) FOR [TaxRate]
GO
ALTER TABLE [dbo].[WN_InvoiceLines] ADD  DEFAULT ((0)) FOR [SortOrder]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT (CONVERT([date],sysutcdatetime())) FOR [IssuedOn]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT ((0)) FOR [SubTotal]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT ((0)) FOR [DiscountTotal]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT ((0)) FOR [TaxTotal]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT ((0)) FOR [GrandTotal]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT ((0)) FOR [PaidTotal]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT (N'PKR') FOR [CurrencyCode]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Locations] ADD  CONSTRAINT [DF_WN_Locations_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Locations] ADD  CONSTRAINT [DF_WN_Locations_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Locations] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Locations] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_MembershipPlanFeatures] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_MembershipPlanFeatures] ADD  DEFAULT ((0)) FOR [SortOrder]
GO
ALTER TABLE [dbo].[WN_MembershipPlanFeatures] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_MembershipPlans] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_MembershipPlans] ADD  DEFAULT ((4)) FOR [BillingPeriodId]
GO
ALTER TABLE [dbo].[WN_MembershipPlans] ADD  DEFAULT ((0)) FOR [Price]
GO
ALTER TABLE [dbo].[WN_MembershipPlans] ADD  DEFAULT (N'PKR') FOR [CurrencyCode]
GO
ALTER TABLE [dbo].[WN_MembershipPlans] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_MembershipPlans] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_PaymentMethods] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Payments] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Payments] ADD  DEFAULT ((1)) FOR [PaymentMethodId]
GO
ALTER TABLE [dbo].[WN_Payments] ADD  DEFAULT (N'PKR') FOR [CurrencyCode]
GO
ALTER TABLE [dbo].[WN_Payments] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_Payments] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_PlanFeatures] ADD  CONSTRAINT [DF_WN_PlanFeatures_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_PricingRules] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_PricingRules] ADD  DEFAULT ('Percentage') FOR [AdjustmentType]
GO
ALTER TABLE [dbo].[WN_PricingRules] ADD  DEFAULT ((0)) FOR [Priority]
GO
ALTER TABLE [dbo].[WN_PricingRules] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_PricingRules] ADD  DEFAULT (CONVERT([date],sysutcdatetime())) FOR [EffectiveFrom]
GO
ALTER TABLE [dbo].[WN_PricingRules] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_PricingTierTypes] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Refunds] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Refunds] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_Refunds] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] ADD  DEFAULT (sysutcdatetime()) FOR [HeldOn]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_SpaceCategories] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_SpaceConfig] ADD  DEFAULT ((0)) FOR [TotalSpaces]
GO
ALTER TABLE [dbo].[WN_SpaceConfig] ADD  DEFAULT ('08:00') FOR [OpeningTime]
GO
ALTER TABLE [dbo].[WN_SpaceConfig] ADD  DEFAULT ('20:00') FOR [ClosingTime]
GO
ALTER TABLE [dbo].[WN_SpaceConfig] ADD  DEFAULT ((0)) FOR [SecurityDeposit]
GO
ALTER TABLE [dbo].[WN_SpaceConfig] ADD  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] ADD  DEFAULT ((0)) FOR [TotalSpaces]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_SpacePricing] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_SpacePricing] ADD  DEFAULT ((1)) FOR [TierTypeId]
GO
ALTER TABLE [dbo].[WN_SpacePricing] ADD  DEFAULT ((0)) FOR [SecurityDeposit]
GO
ALTER TABLE [dbo].[WN_SpacePricing] ADD  DEFAULT (N'PKR') FOR [CurrencyCode]
GO
ALTER TABLE [dbo].[WN_SpacePricing] ADD  DEFAULT (CONVERT([date],sysutcdatetime())) FOR [EffectiveFrom]
GO
ALTER TABLE [dbo].[WN_SpacePricing] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_SpacePricing] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Spaces] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Spaces] ADD  DEFAULT ((1)) FOR [Capacity]
GO
ALTER TABLE [dbo].[WN_Spaces] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_SpaceTypes] ADD  CONSTRAINT [DF_WN_SpaceTypes_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_SpaceTypes] ADD  CONSTRAINT [DF_WN_SpaceTypes_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_SpaceTypes] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_SpaceTypes] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Users] ADD  DEFAULT ((484)) FOR [CompanyId]
GO
ALTER TABLE [dbo].[WN_Users] ADD  CONSTRAINT [DF_WN_Users_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Users] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Users] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_BookingLines]  WITH CHECK ADD  CONSTRAINT [FK_WN_BookingLines_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_BookingLines] CHECK CONSTRAINT [FK_WN_BookingLines_Booking]
GO
ALTER TABLE [dbo].[WN_BookingLines]  WITH CHECK ADD  CONSTRAINT [FK_WN_BookingLines_ChargeType] FOREIGN KEY([ChargeTypeId])
REFERENCES [dbo].[WN_ChargeTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_BookingLines] CHECK CONSTRAINT [FK_WN_BookingLines_ChargeType]
GO
ALTER TABLE [dbo].[WN_Branches]  WITH CHECK ADD  CONSTRAINT [FK_WN_Branches_City] FOREIGN KEY([CityId])
REFERENCES [dbo].[WN_Cities] ([Id])
GO
ALTER TABLE [dbo].[WN_Branches] CHECK CONSTRAINT [FK_WN_Branches_City]
GO
ALTER TABLE [dbo].[WN_Branches]  WITH CHECK ADD  CONSTRAINT [FK_WN_Branches_Company] FOREIGN KEY([CompanyId])
REFERENCES [dbo].[WN_Companies] ([Id])
GO
ALTER TABLE [dbo].[WN_Branches] CHECK CONSTRAINT [FK_WN_Branches_Company]
GO
ALTER TABLE [dbo].[WN_ChallanAuditLog]  WITH CHECK ADD  CONSTRAINT [FK_WN_ChallanAuditLog_Challan] FOREIGN KEY([ChallanId])
REFERENCES [dbo].[WN_Challans] ([Id])
GO
ALTER TABLE [dbo].[WN_ChallanAuditLog] CHECK CONSTRAINT [FK_WN_ChallanAuditLog_Challan]
GO
ALTER TABLE [dbo].[WN_ChallanAuditLog]  WITH CHECK ADD  CONSTRAINT [FK_WN_ChallanAuditLog_User] FOREIGN KEY([UpdatedById])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_ChallanAuditLog] CHECK CONSTRAINT [FK_WN_ChallanAuditLog_User]
GO
ALTER TABLE [dbo].[WN_Challans]  WITH CHECK ADD  CONSTRAINT [FK_WN_Challans_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_Challans] CHECK CONSTRAINT [FK_WN_Challans_Booking]
GO
ALTER TABLE [dbo].[WN_Companies]  WITH CHECK ADD  CONSTRAINT [FK_WN_Companies_City] FOREIGN KEY([CityId])
REFERENCES [dbo].[WN_Cities] ([Id])
GO
ALTER TABLE [dbo].[WN_Companies] CHECK CONSTRAINT [FK_WN_Companies_City]
GO
ALTER TABLE [dbo].[WN_Contacts]  WITH CHECK ADD  CONSTRAINT [FK_WN_Contacts_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_Contacts] CHECK CONSTRAINT [FK_WN_Contacts_User]
GO
ALTER TABLE [dbo].[WN_Floors]  WITH CHECK ADD  CONSTRAINT [FK_WN_Floors_Location] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_Floors] CHECK CONSTRAINT [FK_WN_Floors_Location]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH CHECK ADD  CONSTRAINT [FK_WN_InvoiceLines_ChargeType] FOREIGN KEY([ChargeTypeId])
REFERENCES [dbo].[WN_ChargeTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_InvoiceLines] CHECK CONSTRAINT [FK_WN_InvoiceLines_ChargeType]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH CHECK ADD  CONSTRAINT [FK_WN_InvoiceLines_Invoice] FOREIGN KEY([InvoiceId])
REFERENCES [dbo].[WN_Invoices] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_InvoiceLines] CHECK CONSTRAINT [FK_WN_InvoiceLines_Invoice]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH CHECK ADD  CONSTRAINT [FK_WN_Invoices_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_Invoices] CHECK CONSTRAINT [FK_WN_Invoices_Booking]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH CHECK ADD  CONSTRAINT [FK_WN_Invoices_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_Invoices] CHECK CONSTRAINT [FK_WN_Invoices_User]
GO
ALTER TABLE [dbo].[WN_MembershipPlanFeatures]  WITH CHECK ADD  CONSTRAINT [FK_WN_MembershipPlanFeatures_Plan] FOREIGN KEY([PlanId])
REFERENCES [dbo].[WN_MembershipPlans] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_MembershipPlanFeatures] CHECK CONSTRAINT [FK_WN_MembershipPlanFeatures_Plan]
GO
ALTER TABLE [dbo].[WN_MemberShips]  WITH CHECK ADD  CONSTRAINT [FK_WN_Memberships_PricingPlans_PlanId] FOREIGN KEY([PlanId])
REFERENCES [dbo].[WN_PricingPlans] ([Id])
GO
ALTER TABLE [dbo].[WN_MemberShips] CHECK CONSTRAINT [FK_WN_Memberships_PricingPlans_PlanId]
GO
ALTER TABLE [dbo].[WN_MemberShips]  WITH CHECK ADD  CONSTRAINT [FK_WN_Memberships_Users_UserId] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_MemberShips] CHECK CONSTRAINT [FK_WN_Memberships_Users_UserId]
GO
ALTER TABLE [dbo].[WN_PaymentLines]  WITH CHECK ADD  CONSTRAINT [FK_WN_PaymentLines_ChargeType] FOREIGN KEY([ChargeTypeId])
REFERENCES [dbo].[WN_ChargeTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_PaymentLines] CHECK CONSTRAINT [FK_WN_PaymentLines_ChargeType]
GO
ALTER TABLE [dbo].[WN_PaymentLines]  WITH CHECK ADD  CONSTRAINT [FK_WN_PaymentLines_Payment] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[WN_Payments] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_PaymentLines] CHECK CONSTRAINT [FK_WN_PaymentLines_Payment]
GO
ALTER TABLE [dbo].[WN_Payments]  WITH CHECK ADD  CONSTRAINT [FK_WN_Payments_Memberships_MembershipId] FOREIGN KEY([MembershipId])
REFERENCES [dbo].[WN_MemberShips] ([Id])
GO
ALTER TABLE [dbo].[WN_Payments] CHECK CONSTRAINT [FK_WN_Payments_Memberships_MembershipId]
GO
ALTER TABLE [dbo].[WN_PlanFeatures]  WITH CHECK ADD  CONSTRAINT [FK_WN_PlanFeatures_PricingPlans_PlanId] FOREIGN KEY([PlanId])
REFERENCES [dbo].[WN_PricingPlans] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_PlanFeatures] CHECK CONSTRAINT [FK_WN_PlanFeatures_PricingPlans_PlanId]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH CHECK ADD  CONSTRAINT [FK_WN_PricingRules_BillingPeriod] FOREIGN KEY([BillingPeriodId])
REFERENCES [dbo].[WN_BillingPeriods] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] CHECK CONSTRAINT [FK_WN_PricingRules_BillingPeriod]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH CHECK ADD  CONSTRAINT [FK_WN_PricingRules_Company] FOREIGN KEY([CompanyId])
REFERENCES [dbo].[WN_Companies] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] CHECK CONSTRAINT [FK_WN_PricingRules_Company]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH CHECK ADD  CONSTRAINT [FK_WN_PricingRules_Location] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] CHECK CONSTRAINT [FK_WN_PricingRules_Location]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH CHECK ADD  CONSTRAINT [FK_WN_PricingRules_Space] FOREIGN KEY([SpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] CHECK CONSTRAINT [FK_WN_PricingRules_Space]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH CHECK ADD  CONSTRAINT [FK_WN_PricingRules_SpaceType] FOREIGN KEY([SpaceTypeId])
REFERENCES [dbo].[WN_SpaceTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] CHECK CONSTRAINT [FK_WN_PricingRules_SpaceType]
GO
ALTER TABLE [dbo].[WN_RefreshTokens_Temp]  WITH CHECK ADD  CONSTRAINT [FK_WN_RefreshTokens_Users_UserId] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_RefreshTokens_Temp] CHECK CONSTRAINT [FK_WN_RefreshTokens_Users_UserId]
GO
ALTER TABLE [dbo].[WN_Refunds]  WITH CHECK ADD  CONSTRAINT [FK_WN_Refunds_Payment] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[WN_Payments] ([Id])
GO
ALTER TABLE [dbo].[WN_Refunds] CHECK CONSTRAINT [FK_WN_Refunds_Payment]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits]  WITH CHECK ADD  CONSTRAINT [FK_WN_SecurityDeposits_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] CHECK CONSTRAINT [FK_WN_SecurityDeposits_Booking]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits]  WITH CHECK ADD  CONSTRAINT [FK_WN_SecurityDeposits_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] CHECK CONSTRAINT [FK_WN_SecurityDeposits_User]
GO
ALTER TABLE [dbo].[WN_SpaceAmenities]  WITH CHECK ADD  CONSTRAINT [FK_WN_SpaceAmenities_Amenity] FOREIGN KEY([AmenityId])
REFERENCES [dbo].[WN_Amenities] ([Id])
GO
ALTER TABLE [dbo].[WN_SpaceAmenities] CHECK CONSTRAINT [FK_WN_SpaceAmenities_Amenity]
GO
ALTER TABLE [dbo].[WN_SpaceAmenities]  WITH CHECK ADD  CONSTRAINT [FK_WN_SpaceAmenities_Space] FOREIGN KEY([SpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_SpaceAmenities] CHECK CONSTRAINT [FK_WN_SpaceAmenities_Space]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig]  WITH CHECK ADD  CONSTRAINT [FK_WN_SpaceInventoryConfig_Location] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] CHECK CONSTRAINT [FK_WN_SpaceInventoryConfig_Location]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig]  WITH CHECK ADD  CONSTRAINT [FK_WN_SpaceInventoryConfig_SpaceType] FOREIGN KEY([SpaceTypeId])
REFERENCES [dbo].[WN_SpaceTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] CHECK CONSTRAINT [FK_WN_SpaceInventoryConfig_SpaceType]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH CHECK ADD  CONSTRAINT [FK_WN_SpacePricing_BillingPeriod] FOREIGN KEY([BillingPeriodId])
REFERENCES [dbo].[WN_BillingPeriods] ([Id])
GO
ALTER TABLE [dbo].[WN_SpacePricing] CHECK CONSTRAINT [FK_WN_SpacePricing_BillingPeriod]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH CHECK ADD  CONSTRAINT [FK_WN_SpacePricing_Space] FOREIGN KEY([SpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
GO
ALTER TABLE [dbo].[WN_SpacePricing] CHECK CONSTRAINT [FK_WN_SpacePricing_Space]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH CHECK ADD  CONSTRAINT [FK_WN_SpacePricing_TierType] FOREIGN KEY([TierTypeId])
REFERENCES [dbo].[WN_PricingTierTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_SpacePricing] CHECK CONSTRAINT [FK_WN_SpacePricing_TierType]
GO
ALTER TABLE [dbo].[WN_BookingLines]  WITH CHECK ADD  CONSTRAINT [CK_WN_BookingLines_Discount] CHECK  (([DiscountAmount]>=(0)))
GO
ALTER TABLE [dbo].[WN_BookingLines] CHECK CONSTRAINT [CK_WN_BookingLines_Discount]
GO
ALTER TABLE [dbo].[WN_BookingLines]  WITH CHECK ADD  CONSTRAINT [CK_WN_BookingLines_Quantity] CHECK  (([Quantity]>(0)))
GO
ALTER TABLE [dbo].[WN_BookingLines] CHECK CONSTRAINT [CK_WN_BookingLines_Quantity]
GO
ALTER TABLE [dbo].[WN_BookingLines]  WITH CHECK ADD  CONSTRAINT [CK_WN_BookingLines_TaxRate] CHECK  (([TaxRate]>=(0) AND [TaxRate]<=(1)))
GO
ALTER TABLE [dbo].[WN_BookingLines] CHECK CONSTRAINT [CK_WN_BookingLines_TaxRate]
GO
ALTER TABLE [dbo].[WN_BookingLines]  WITH CHECK ADD  CONSTRAINT [CK_WN_BookingLines_UnitPrice] CHECK  (([UnitPrice]>=(0)))
GO
ALTER TABLE [dbo].[WN_BookingLines] CHECK CONSTRAINT [CK_WN_BookingLines_UnitPrice]
GO
ALTER TABLE [dbo].[WN_Challans]  WITH CHECK ADD  CONSTRAINT [CK_WN_Challans_ValidUntil] CHECK  (([ValidUntil]>=CONVERT([date],sysutcdatetime())))
GO
ALTER TABLE [dbo].[WN_Challans] CHECK CONSTRAINT [CK_WN_Challans_ValidUntil]
GO
ALTER TABLE [dbo].[WN_Contacts]  WITH CHECK ADD  CONSTRAINT [CK_WN_Contacts_Type] CHECK  (([ContactType]='BookTour' OR [ContactType]='Contact'))
GO
ALTER TABLE [dbo].[WN_Contacts] CHECK CONSTRAINT [CK_WN_Contacts_Type]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH CHECK ADD  CONSTRAINT [CK_WN_InvoiceLines_Discount] CHECK  (([DiscountAmount]>=(0)))
GO
ALTER TABLE [dbo].[WN_InvoiceLines] CHECK CONSTRAINT [CK_WN_InvoiceLines_Discount]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH CHECK ADD  CONSTRAINT [CK_WN_InvoiceLines_Quantity] CHECK  (([Quantity]>(0)))
GO
ALTER TABLE [dbo].[WN_InvoiceLines] CHECK CONSTRAINT [CK_WN_InvoiceLines_Quantity]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH CHECK ADD  CONSTRAINT [CK_WN_InvoiceLines_UnitPrice] CHECK  (([UnitPrice]>=(0)))
GO
ALTER TABLE [dbo].[WN_InvoiceLines] CHECK CONSTRAINT [CK_WN_InvoiceLines_UnitPrice]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH CHECK ADD  CONSTRAINT [CK_WN_Invoices_DueDate] CHECK  (([DueOn]>=[IssuedOn]))
GO
ALTER TABLE [dbo].[WN_Invoices] CHECK CONSTRAINT [CK_WN_Invoices_DueDate]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH CHECK ADD  CONSTRAINT [CK_WN_Invoices_Grand] CHECK  (([GrandTotal]>=(0)))
GO
ALTER TABLE [dbo].[WN_Invoices] CHECK CONSTRAINT [CK_WN_Invoices_Grand]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH CHECK ADD  CONSTRAINT [CK_WN_Invoices_Paid] CHECK  (([PaidTotal]>=(0)))
GO
ALTER TABLE [dbo].[WN_Invoices] CHECK CONSTRAINT [CK_WN_Invoices_Paid]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH CHECK ADD  CONSTRAINT [CK_WN_Invoices_SubTotal] CHECK  (([SubTotal]>=(0)))
GO
ALTER TABLE [dbo].[WN_Invoices] CHECK CONSTRAINT [CK_WN_Invoices_SubTotal]
GO
ALTER TABLE [dbo].[WN_PaymentLines]  WITH CHECK ADD  CONSTRAINT [CK_WN_PaymentLines_Amount] CHECK  (([Amount]<>(0)))
GO
ALTER TABLE [dbo].[WN_PaymentLines] CHECK CONSTRAINT [CK_WN_PaymentLines_Amount]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH CHECK ADD  CONSTRAINT [CK_WN_PricingRules_AdjType] CHECK  (([AdjustmentType]='FixedAmount' OR [AdjustmentType]='Percentage'))
GO
ALTER TABLE [dbo].[WN_PricingRules] CHECK CONSTRAINT [CK_WN_PricingRules_AdjType]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH CHECK ADD  CONSTRAINT [CK_WN_PricingRules_DayOfWeek] CHECK  (([DayOfWeek] IS NULL OR [DayOfWeek]>=(1) AND [DayOfWeek]<=(7)))
GO
ALTER TABLE [dbo].[WN_PricingRules] CHECK CONSTRAINT [CK_WN_PricingRules_DayOfWeek]
GO
ALTER TABLE [dbo].[WN_Refunds]  WITH CHECK ADD  CONSTRAINT [CK_WN_Refunds_Amount] CHECK  (([Amount]>(0)))
GO
ALTER TABLE [dbo].[WN_Refunds] CHECK CONSTRAINT [CK_WN_Refunds_Amount]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits]  WITH CHECK ADD  CONSTRAINT [CK_WN_SecurityDeposits_Amount] CHECK  (([Amount]>(0)))
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] CHECK CONSTRAINT [CK_WN_SecurityDeposits_Amount]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig]  WITH CHECK ADD  CONSTRAINT [CK_WN_SpaceInventoryConfig_MinCode] CHECK  (([MinCode]>=(0)))
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] CHECK CONSTRAINT [CK_WN_SpaceInventoryConfig_MinCode]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig]  WITH CHECK ADD  CONSTRAINT [CK_WN_SpaceInventoryConfig_TotalSpaces] CHECK  (([TotalSpaces]>=(0)))
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] CHECK CONSTRAINT [CK_WN_SpaceInventoryConfig_TotalSpaces]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH CHECK ADD  CONSTRAINT [CK_WN_SpacePricing_Dates] CHECK  (([EffectiveTo] IS NULL OR [EffectiveTo]>[EffectiveFrom]))
GO
ALTER TABLE [dbo].[WN_SpacePricing] CHECK CONSTRAINT [CK_WN_SpacePricing_Dates]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH CHECK ADD  CONSTRAINT [CK_WN_SpacePricing_Deposit] CHECK  (([SecurityDeposit]>=(0)))
GO
ALTER TABLE [dbo].[WN_SpacePricing] CHECK CONSTRAINT [CK_WN_SpacePricing_Deposit]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH CHECK ADD  CONSTRAINT [CK_WN_SpacePricing_SeatPrice] CHECK  (([SeatPrice]>=(0)))
GO
ALTER TABLE [dbo].[WN_SpacePricing] CHECK CONSTRAINT [CK_WN_SpacePricing_SeatPrice]
GO
/****** Object:  StoredProcedure [dbo].[WN_AccountsCOA_GetById]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
