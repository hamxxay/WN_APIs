USE [SAC400]
GO
/****** Object:  Table [dbo].[WN_AccessCards]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AccessCards](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[LocationId] [int] NULL,
	[CustomerId] [int] NULL,
	[BookingId] [int] NULL,
	[SpaceId] [int] NULL,
	[CardNumber] [nvarchar](100) NULL,
	[StartDate] [datetime] NULL,
	[EndDate] [datetime] NULL,
	[Status] [int] NULL,
	[CreatedOn] [datetime] NULL,
	[UpdatedOn] [datetime] NULL,
	[CreatedById] [int] NULL,
	[UpdatedById] [int] NULL,
 CONSTRAINT [PK_WN_AccessCards] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_AccessCards_IdGUID] UNIQUE NONCLUSTERED 
(
	[IdGUID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AccessStatus]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AccessStatus](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[BookingDetailId] [int] NOT NULL,
	[PersonId] [int] NOT NULL,
	[CustomerId] [int] NOT NULL,
	[IsEnabled] [bit] NOT NULL,
	[GrantedAt] [datetime2](7) NULL,
	[RevokedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AgreementESignatures]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AgreementESignatures](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[AgreementId] [int] NOT NULL,
	[SignerName] [nvarchar](100) NULL,
	[SignerEmail] [nvarchar](256) NULL,
	[SignedAt] [datetime2](0) NULL,
	[IpAddress] [nvarchar](64) NULL,
	[UserAgent] [nvarchar](400) NULL,
	[DocumentSha256] [char](64) NULL,
	[SignatureImage] [varbinary](max) NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_AgreementESignatures] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Agreements]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Agreements](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Guid] [uniqueidentifier] NOT NULL,
	[QuotationId] [int] NOT NULL,
	[BookingId] [int] NULL,
	[CustomerId] [int] NOT NULL,
	[EntityType] [nvarchar](50) NOT NULL,
	[Status] [nvarchar](50) NOT NULL,
	[SentDate] [datetime2](7) NOT NULL,
	[SignedDate] [datetime2](7) NULL,
	[RefundDays] [int] NOT NULL,
	[FeeAmount] [decimal](18, 2) NOT NULL,
	[SecurityDeposit] [decimal](18, 2) NOT NULL,
	[OperatingHours] [nvarchar](100) NULL,
	[DocumentUrl] [nvarchar](500) NULL,
	[CustomerName] [nvarchar](200) NULL,
	[CustomerCnic] [nvarchar](50) NULL,
	[CustomerPhone] [nvarchar](50) NULL,
	[CustomerAddress] [nvarchar](500) NULL,
	[CompanyName] [nvarchar](200) NULL,
	[Ntn] [nvarchar](50) NULL,
	[SecpRegistrationNo] [nvarchar](50) NULL,
	[CreatedById] [int] NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[UpdatedOn] [datetime2](7) NULL,
	[TemplateVersionId] [int] NULL,
	[SignedPdfPath] [nvarchar](500) NULL,
	[SignedPdfUploadedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Amenities]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_AmountFields]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_Announcement]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Announcement](
	[Id] [uniqueidentifier] NOT NULL,
	[Title] [nvarchar](200) NOT NULL,
	[Body] [nvarchar](max) NOT NULL,
	[Type] [nvarchar](20) NOT NULL,
	[TargetScope] [nvarchar](20) NOT NULL,
	[LocationId] [int] NULL,
	[SpaceId] [int] NULL,
	[ScheduledAt] [datetime2](7) NULL,
	[SentAt] [datetime2](7) NULL,
	[Status] [nvarchar](20) NOT NULL,
	[CreatedBy] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AnnouncementRecipient]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AnnouncementRecipient](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[AnnouncementId] [uniqueidentifier] NOT NULL,
	[UserId] [int] NOT NULL,
	[Channel] [nvarchar](10) NOT NULL,
	[Status] [nvarchar](20) NOT NULL,
	[RetryCount] [int] NOT NULL,
	[SentAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AttendanceHolidays]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AttendanceHolidays](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CustomerId] [int] NOT NULL,
	[HolidayDate] [date] NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
	[CreatedBy] [nvarchar](256) NULL,
 CONSTRAINT [PK_WN_AttendanceHolidays] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_AttendanceHolidays_Customer_Date] UNIQUE NONCLUSTERED 
(
	[CustomerId] ASC,
	[HolidayDate] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AttendanceShiftAssignments]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AttendanceShiftAssignments](
	[CustomerId] [int] NOT NULL,
	[PersonId] [int] NOT NULL,
	[ShiftId] [int] NOT NULL,
	[UpdatedOn] [datetime2](0) NOT NULL,
	[UpdatedBy] [nvarchar](256) NULL,
 CONSTRAINT [PK_WN_AttendanceShiftAssignments] PRIMARY KEY CLUSTERED 
(
	[CustomerId] ASC,
	[PersonId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AttendanceShifts]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AttendanceShifts](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CustomerId] [int] NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[StartTime] [time](0) NOT NULL,
	[EndTime] [time](0) NOT NULL,
	[GraceMinutes] [int] NOT NULL,
	[HalfDayMinutes] [int] NULL,
	[WorkDays] [nvarchar](40) NOT NULL,
	[IsDefault] [bit] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
	[CreatedBy] [nvarchar](256) NULL,
	[UpdatedOn] [datetime2](0) NULL,
	[UpdatedBy] [nvarchar](256) NULL,
 CONSTRAINT [PK_WN_AttendanceShifts] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AttendantCapacityRequests]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AttendantCapacityRequests](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[BookingDetailId] [int] NOT NULL,
	[PersonId] [int] NOT NULL,
	[CustomerId] [int] NOT NULL,
	[RequestedByEmail] [nvarchar](256) NOT NULL,
	[RequestedAt] [datetime2](0) NOT NULL,
	[Status] [nvarchar](20) NOT NULL,
	[DecidedByEmail] [nvarchar](256) NULL,
	[DecidedAt] [datetime2](0) NULL,
	[DecisionNote] [nvarchar](500) NULL,
 CONSTRAINT [PK_WN_AttendantCapacityRequests] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AuditLog]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AuditLog](
	[AuditId] [bigint] IDENTITY(1,1) NOT NULL,
	[EntityName] [nvarchar](128) NULL,
	[RecordId] [nvarchar](64) NULL,
	[Operation] [char](1) NOT NULL,
	[OldValues] [nvarchar](max) NULL,
	[NewValues] [nvarchar](max) NULL,
	[ErrorMessage] [nvarchar](max) NULL,
	[ErrorSeverity] [nvarchar](20) NULL,
	[StackTrace] [nvarchar](max) NULL,
	[ChangedByUserId] [int] NULL,
	[ChangedByUserEmail] [nvarchar](256) NULL,
	[ChangedAt] [datetime2](3) NOT NULL,
	[SourceApp] [nvarchar](50) NULL,
PRIMARY KEY CLUSTERED 
(
	[AuditId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_AuthHandoff]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_AuthHandoff](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[CodeHash] [binary](32) NOT NULL,
	[UserId] [int] NOT NULL,
	[Target] [varchar](10) NOT NULL,
	[CreatedAt] [datetime2](3) NOT NULL,
	[ExpiresAt] [datetime2](3) NOT NULL,
	[UsedAt] [datetime2](3) NULL,
 CONSTRAINT [PK_WN_AuthHandoff] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_AuthHandoff_CodeHash] UNIQUE NONCLUSTERED 
(
	[CodeHash] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BankReceipts]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BankReceipts](
	[Id] [int] NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Code] [varchar](50) NULL,
	[UploadedDate] [smalldatetime] NULL,
	[BankReceiptStatus] [tinyint] NULL,
	[CompanyId] [int] NULL,
	[BranchId] [int] NULL,
	[Status] [tinyint] NULL,
	[CreatedById] [int] NULL,
	[CreatedTime] [smalldatetime] NULL,
	[UpdatedById] [int] NULL,
	[UpdatedTime] [smalldatetime] NULL,
	[ApprovedById] [int] NULL,
	[ApprovedDate] [smalldatetime] NULL,
	[VoucherId] [int] NULL,
	[BankAccountId] [int] NULL,
	[RejectReason] [varchar](350) NULL,
	[ReceiptsType] [varchar](50) NULL
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BankReceiptsDetails]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BankReceiptsDetails](
	[Id] [int] NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[BankReceiptId] [int] NULL,
	[TransDate] [smalldatetime] NULL,
	[InvoiceId] [varchar](50) NULL,
	[CustomerId] [varchar](50) NULL,
	[Amount] [decimal](18, 4) NULL,
	[DepositSlipNo] [varchar](50) NULL,
	[CollectionDate] [varchar](50) NULL,
	[ModeOfPayment] [varchar](50) NULL,
	[CreditDate] [varchar](50) NULL,
	[DocStatus] [tinyint] NULL,
	[UpdatedById] [int] NULL,
	[UpdatedTime] [smalldatetime] NULL,
	[FeeCharges] [decimal](18, 4) NULL,
	[AmtAfterFeeCharges] [decimal](18, 4) NULL
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BillingPeriods]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_BillingSettings]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BillingSettings](
	[SettingKey] [nvarchar](50) NOT NULL,
	[SettingValue] [nvarchar](250) NOT NULL,
	[Description] [nvarchar](250) NULL,
	[UpdatedOn] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[SettingKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookingAttendants]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BookingAttendants](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[BookingDetailId] [int] NOT NULL,
	[PersonId] [int] NOT NULL,
	[CustomerId] [int] NOT NULL,
	[AssignedFrom] [date] NOT NULL,
	[AssignedTo] [date] NULL,
	[IsOverCapacity] [bit] NOT NULL,
	[ExcessSeatCount] [int] NOT NULL,
	[SurchargeApplied] [decimal](10, 2) NULL,
	[CustomInvoiceId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookingDetails]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[SupportChargesId] [tinyint] NULL,
	[AppliedChargePercentage] [decimal](5, 2) NOT NULL,
	[AppliedTaxPercentage] [decimal](5, 2) NOT NULL,
	[SupportChargeAmount] [decimal](18, 2) NOT NULL,
	[TaxAmount] [decimal](18, 2) NOT NULL,
	[OfferingType] [nvarchar](50) NULL,
	[PerSeatSupportRate] [decimal](18, 2) NULL,
	[AccountReceivableId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookingEmployeeRequests]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BookingEmployeeRequests](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[BookingId] [int] NOT NULL,
	[BookingDetailId] [int] NULL,
	[CustomerId] [int] NULL,
	[PersonId] [int] NOT NULL,
	[Status] [nvarchar](20) NOT NULL,
	[AddedAt] [datetime2](7) NOT NULL,
	[AddedByUserId] [int] NULL,
	[VerifiedAt] [datetime2](7) NULL,
	[VerifiedById] [int] NULL,
	[RemovedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_WN_BookingEmployeeRequests] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookingLines]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
 CONSTRAINT [PK_WN_BookingLines] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookingMeetingRoomEntitlements]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BookingMeetingRoomEntitlements](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Guid] [uniqueidentifier] NOT NULL,
	[BookingId] [int] NOT NULL,
	[UserId] [int] NOT NULL,
	[MonthStartDate] [date] NOT NULL,
	[MonthEndDate] [date] NOT NULL,
	[AllocatedMinutes] [int] NOT NULL,
	[Status] [nvarchar](50) NOT NULL,
	[CreatedDate] [datetime2](7) NOT NULL,
	[CreatedById] [int] NULL,
	[UpdatedDate] [datetime2](7) NULL,
	[UpdatedById] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_Booking_Month] UNIQUE NONCLUSTERED 
(
	[BookingId] ASC,
	[MonthStartDate] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Bookings]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Bookings](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NOT NULL,
	[BookingDate] [datetime] NULL,
	[CustomerCode] [nvarchar](20) NULL,
	[TotalAmount] [decimal](18, 2) NULL,
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
	[TransactionDate] [datetime] NULL,
	[ChallanNumber] [nvarchar](50) NULL,
	[ValidityDate] [datetime] NULL,
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
	[DiscountPercentage] [decimal](5, 2) NOT NULL,
	[SubtotalAmount] [decimal](18, 2) NOT NULL,
	[IsFree] [bit] NOT NULL,
	[EntitlementId] [int] NULL,
	[DiscountType] [nvarchar](20) NOT NULL,
	[DiscountAmount] [decimal](18, 2) NOT NULL,
	[SecurityDepositOverride] [decimal](18, 2) NULL,
	[FloorId] [int] NULL,
	[AdvanceRentMonths] [int] NOT NULL,
	[SecurityDepositMonths] [int] NOT NULL,
	[SecurityDepositRequired] [decimal](18, 2) NOT NULL,
	[SecurityDepositPaid] [decimal](18, 2) NOT NULL,
	[BillingPeriodMonths] [int] NOT NULL,
	[SecurityDepositCharged] [bit] NOT NULL,
	[BillingPeriodStart] [datetime] NULL,
	[BillingPeriodEnd] [datetime] NULL,
	[NextBillingDate] [datetime] NULL,
	[MonthlyRent] [decimal](18, 2) NULL,
	[PerSeatBasePrice] [decimal](18, 2) NULL,
	[Capacity] [int] NULL,
	[MonthlyBasePrice] [decimal](18, 2) NULL,
	[MaxDiscountPercent] [decimal](5, 2) NULL,
	[DiscountValue] [decimal](18, 2) NULL,
	[OfferingType] [nvarchar](50) NULL,
	[ShiftType] [nvarchar](20) NULL,
	[AccountReceivableId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[RentAccountId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
	[ShiftTypeId] [int] NULL,
	[SecurityRecivedId] [int] NULL,
	[SendWHTInvoice] [bit] NOT NULL,
	[WHTaxId] [int] NULL,
	[TransferredToBookingId] [int] NULL,
	[TransferredFromBookingId] [int] NULL,
	[TransferCreditBalance] [decimal](18, 2) NOT NULL,
 CONSTRAINT [PK_WN_Bookings_1] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookingStatuses]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_BookingTransfers]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_BookingTransfers](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[OldBookingId] [int] NOT NULL,
	[NewBookingId] [int] NOT NULL,
	[OldSpaceId] [int] NOT NULL,
	[NewSpaceId] [int] NOT NULL,
	[LocationId] [int] NOT NULL,
	[EffectiveDate] [date] NOT NULL,
	[PeriodStartDate] [date] NULL,
	[PeriodEndDate] [date] NULL,
	[OldMonthlyRate] [decimal](18, 2) NOT NULL,
	[NewMonthlyRate] [decimal](18, 2) NOT NULL,
	[DaysUsed] [int] NOT NULL,
	[RemainingDays] [int] NOT NULL,
	[CreditAtOldRate] [decimal](18, 2) NOT NULL,
	[RepricedAtNewRate] [decimal](18, 2) NOT NULL,
	[Adjustment] [decimal](18, 2) NOT NULL,
	[SettlementOutcome] [nvarchar](50) NOT NULL,
	[SettlementInvoiceId] [int] NULL,
	[TransferFee] [decimal](18, 2) NOT NULL,
	[Notes] [nvarchar](max) NOT NULL,
	[InitiatedById] [int] NULL,
	[InitiatedByEmail] [nvarchar](256) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_BookTour]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_ChallanAuditLog]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_ChallanCounter]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_Challans]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_ChallanValidityLog]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_ChargeTypeAccountMapping]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ChargeTypeAccountMapping](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ChargeTypeId] [int] NULL,
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
	[EffectiveFrom] [date] NULL,
	[EffectiveTo] [date] NULL,
	[LocationId] [int] NOT NULL,
	[WHTReceivableId] [int] NULL,
	[WhtAccountId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ChargeTypeRate]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ChargeTypeRate](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ChargeTypeId] [tinyint] NOT NULL,
	[ChargePercentage] [decimal](5, 2) NOT NULL,
	[StartDate] [datetime] NOT NULL,
	[EndDate] [datetime] NULL,
	[CreatedOn] [datetime] NOT NULL,
	[ChargeMode] [nvarchar](20) NOT NULL,
	[FixedAmount] [decimal](18, 2) NULL,
 CONSTRAINT [PK_WN_ChargeTypeRate] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ChargeTypes]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_Complaints]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Complaints](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ComplaintNo] [nvarchar](20) NOT NULL,
	[Source] [nvarchar](20) NOT NULL,
	[CustomerName] [nvarchar](200) NULL,
	[PhoneNumber] [nvarchar](30) NULL,
	[Email] [nvarchar](256) NULL,
	[Branch] [nvarchar](50) NULL,
	[Category] [nvarchar](50) NOT NULL,
	[Description] [nvarchar](2000) NOT NULL,
	[Status] [nvarchar](20) NOT NULL,
	[IsFollowUp] [bit] NOT NULL,
	[PreviousComplaintId] [int] NULL,
	[ResolvedNotificationSent] [bit] NOT NULL,
	[ResolvedNotificationSentAt] [datetime2](0) NULL,
	[ResolutionResponse] [nvarchar](20) NULL,
	[ResolutionResponseAt] [datetime2](0) NULL,
	[StaffNotes] [nvarchar](1000) NULL,
	[CreatedById] [int] NULL,
	[UpdatedById] [int] NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
	[UpdatedOn] [datetime2](0) NULL,
 CONSTRAINT [PK_WN_Complaints] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Complaints_ComplaintNo] UNIQUE NONCLUSTERED 
(
	[ComplaintNo] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ContactFeedback]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ContactFeedback](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ContactId] [int] NOT NULL,
	[Outcome] [nvarchar](20) NOT NULL,
	[Reason] [nvarchar](1000) NULL,
	[FollowUpOn] [date] NULL,
	[QuotationId] [int] NULL,
	[CreatedById] [int] NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_ContactFeedback] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ContactFeedbackMoved]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ContactFeedbackMoved](
	[FeedbackId] [int] NOT NULL,
	[FromContactId] [int] NOT NULL,
	[ToContactId] [int] NOT NULL,
	[MovedOn] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_ContactFeedbackMoved] PRIMARY KEY CLUSTERED 
(
	[FeedbackId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Contacts]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[InquiryType] [nvarchar](20) NULL,
	[ReferenceName] [nvarchar](200) NULL,
	[LocationId] [int] NULL,
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
/****** Object:  Table [dbo].[WN_ContactsFromBookTour]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ContactsFromBookTour](
	[BookTourId] [int] NOT NULL,
	[ContactId] [int] NOT NULL,
	[CopiedOn] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_ContactsFromBookTour] PRIMARY KEY CLUSTERED 
(
	[BookTourId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Countries]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_CustomerAttendants]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_CustomerAttendants](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PersonId] [int] NOT NULL,
	[CustomerId] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NULL,
	[DeactivatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_CustomerKYCDocuments]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_CustomerKYCDocuments](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CustomerId] [int] NOT NULL,
	[FolderName] [nvarchar](100) NOT NULL,
	[DocumentTypeId] [int] NOT NULL,
	[SlotNo] [tinyint] NOT NULL,
	[HolderName] [nvarchar](150) NULL,
	[StoredPath] [nvarchar](500) NOT NULL,
	[OriginalFileName] [nvarchar](260) NOT NULL,
	[FileHash] [char](64) NOT NULL,
	[ExpiryDate] [date] NULL,
	[VersionNo] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[Status] [tinyint] NOT NULL,
	[Remarks] [nvarchar](500) NULL,
	[VerifiedBy] [int] NULL,
	[VerifiedOn] [datetime2](7) NULL,
	[UploadedBy] [int] NOT NULL,
	[UploadedOn] [datetime2](7) NOT NULL,
	[ReplacedBy] [int] NULL,
	[ReplacedOn] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Customers]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Customers](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NOT NULL,
	[Code] [nvarchar](50) NOT NULL,
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
	[UserId] [int] NULL,
	[Company] [nvarchar](255) NULL,
	[NTN] [nvarchar](50) NULL,
	[SecpRegistrationNo] [nvarchar](50) NULL,
	[CustomerTypeId] [int] NULL,
	[CountryId] [int] NULL,
	[IdentificationDocumentTypeId] [int] NULL,
	[UpdatedById] [int] NULL,
	[AttendanceEnabled] [bit] NOT NULL,
	[AttendanceEnabledOn] [datetime2](0) NULL,
	[AttendanceEnabledBy] [nvarchar](256) NULL,
 CONSTRAINT [PK__WN_Custo__3214EC072DB97A58] PRIMARY KEY CLUSTERED 
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
/****** Object:  Table [dbo].[WN_CustomerSTInvoice]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_CustomerSTInvoice](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PublicId] [uniqueidentifier] NOT NULL,
	[CustomerInvoiceId] [int] NOT NULL,
	[STInvoiceNumber] [nvarchar](50) NOT NULL,
	[TariffHeading] [nvarchar](50) NOT NULL,
	[TariffLabel] [nvarchar](200) NOT NULL,
	[RoomRentDescription] [nvarchar](255) NOT NULL,
	[RoomRentAmount] [decimal](18, 2) NOT NULL,
	[RoomRentTaxRate] [decimal](5, 2) NOT NULL,
	[RoomRentTaxAmount] [decimal](18, 2) NOT NULL,
	[ServiceChargeDescription] [nvarchar](255) NOT NULL,
	[ServiceChargeAmount] [decimal](18, 2) NOT NULL,
	[ServiceChargeTaxRate] [decimal](5, 2) NOT NULL,
	[ServiceChargeTaxAmount] [decimal](18, 2) NOT NULL,
	[SecurityDepositDescription] [nvarchar](255) NULL,
	[SecurityDepositAmount] [decimal](18, 2) NULL,
	[SecurityDepositTaxRate] [decimal](5, 2) NOT NULL,
	[SecurityDepositTaxAmount] [decimal](18, 2) NOT NULL,
	[SubTotal] [decimal](18, 2) NOT NULL,
	[TaxTotal] [decimal](18, 2) NOT NULL,
	[GrandTotal] [decimal](18, 2) NOT NULL,
	[DocumentUrl] [nvarchar](1000) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[UpdatedOn] [datetime2](7) NULL,
	[CreatedById] [int] NOT NULL,
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
 CONSTRAINT [PK_WN_CustomerSTInvoice] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_CustomInvoices]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_CustomInvoices](
	[InvoiceId] [int] NOT NULL,
	[LocationId] [int] NULL,
	[ReplacedInvoiceId] [int] NULL,
	[ReversalVoucherId] [int] NULL,
	[VoucherDescription] [nvarchar](500) NULL,
	[TaxPercent] [decimal](9, 4) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[SpaceId] [int] NULL,
 CONSTRAINT [PK_WN_CustomInvoices] PRIMARY KEY CLUSTERED 
(
	[InvoiceId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_DeviceToken]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_DeviceToken](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[UserId] [int] NOT NULL,
	[Token] [nvarchar](500) NOT NULL,
	[Platform] [nvarchar](10) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[IsActive] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_EmailOtps_Temp]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_EmailOutbox]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_EmailOutbox](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Kind] [nvarchar](100) NOT NULL,
	[ToEmail] [nvarchar](1000) NOT NULL,
	[Subject] [nvarchar](500) NOT NULL,
	[MessageJson] [nvarchar](max) NOT NULL,
	[Status] [nvarchar](20) NOT NULL,
	[Attempts] [int] NOT NULL,
	[LastError] [nvarchar](1000) NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
	[ClaimedOn] [datetime2](0) NULL,
	[SentOn] [datetime2](0) NULL,
	[DiscardedOn] [datetime2](0) NULL,
	[DiscardedBy] [nvarchar](256) NULL,
 CONSTRAINT [PK_WN_EmailOutbox] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_FeeReceipts]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_FeeReceipts](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Code] [varchar](50) NULL,
	[CustomerIdGUID] [uniqueidentifier] NULL,
	[InvoiceIdGUID] [uniqueidentifier] NULL,
	[InvoiceTypeId] [int] NULL,
	[TotalAmount] [decimal](18, 4) NULL,
	[RoomRentExclTax] [decimal](18, 4) NULL,
	[ServiceCharges] [decimal](18, 4) NULL,
	[TaxOnServiceCharges] [decimal](18, 4) NULL,
	[WHTAmount] [decimal](18, 4) NULL,
	[SecurityDepositAmount] [decimal](18, 4) NULL,
	[CashInHandAccountId] [int] NULL,
	[PaymentType] [varchar](50) NULL,
	[BranchId] [int] NULL,
	[CompanyId] [int] NULL,
	[FeeStatus] [tinyint] NULL,
	[Status] [tinyint] NULL,
	[ReceiptDate] [date] NOT NULL,
	[CreatedOn] [datetime2](7) NULL,
	[UpdatedOn] [datetime2](7) NULL,
	[CreatedById] [int] NULL,
	[Remarks] [nvarchar](500) NULL,
	[UpdatedById] [int] NULL,
	[VoidReason] [nvarchar](500) NULL,
	[DepositorName] [nvarchar](150) NULL,
	[DepositorCNIC] [varchar](15) NULL,
	[VoucherId] [int] NULL,
	[BillingMonth] [date] NULL,
	[MonthNo] [int] NULL,
	[MonthCount] [int] NULL,
	[InvoiceTotal] [decimal](18, 2) NULL,
	[PreviousPaid] [decimal](18, 2) NULL,
	[BalanceAfter] [decimal](18, 2) NULL,
 CONSTRAINT [PK_WN_FeeReceipts] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Floors]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Floors](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[LocationId] [int] NOT NULL,
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
/****** Object:  Table [dbo].[WN_GalleryImages]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_GalleryImages](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Title] [nvarchar](150) NULL,
	[Description] [nvarchar](max) NULL,
	[ImageUrl] [nvarchar](max) NULL,
	[Status] [int] NULL,
	[CreatedOn] [datetime] NULL,
	[UpdatedOn] [datetime] NULL,
	[UpdatedBy] [uniqueidentifier] NULL,
	[IsActive] [bit] NULL,
	[SortOrder] [int] NULL,
	[LocationId] [int] NULL,
	[SpaceId] [int] NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_GalleryImages] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_GWN_ActionLog]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_GWN_ActionLog](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[NetworkId] [bigint] NULL,
	[Action] [nvarchar](30) NOT NULL,
	[Target] [nvarchar](64) NULL,
	[ByEmail] [nvarchar](256) NULL,
	[ByUserId] [int] NULL,
	[Ok] [bit] NOT NULL,
	[Message] [nvarchar](500) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_GWN_ActionLog] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_GWN_ClientAliases]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_GWN_ClientAliases](
	[Mac] [char](17) NOT NULL,
	[Alias] [nvarchar](100) NULL,
	[UpdatedBy] [nvarchar](256) NULL,
	[UpdatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_GWN_ClientAliases] PRIMARY KEY CLUSTERED 
(
	[Mac] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_GWN_NetworkLocations]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_GWN_NetworkLocations](
	[NetworkId] [bigint] NOT NULL,
	[LocationId] [int] NOT NULL,
	[NetworkName] [nvarchar](200) NULL,
	[UpdatedBy] [nvarchar](256) NULL,
	[UpdatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_GWN_NetworkLocations] PRIMARY KEY CLUSTERED 
(
	[NetworkId] ASC,
	[LocationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_AccessGrants]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_AccessGrants](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[employee_id] [int] NOT NULL,
	[device_id] [int] NOT NULL,
	[sync_state] [nvarchar](16) NOT NULL,
	[last_error] [nvarchar](max) NULL,
	[synced_at] [datetime2](0) NULL,
 CONSTRAINT [PK_WN_HIK_AccessGrants] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_HIK_grants_emp_dev] UNIQUE NONCLUSTERED 
(
	[employee_id] ASC,
	[device_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_BookingAccessOverrides]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_BookingAccessOverrides](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[SuspensionId] [int] NOT NULL,
	[BookingId] [int] NOT NULL,
	[OverrideUntil] [date] NOT NULL,
	[Reason] [nvarchar](500) NOT NULL,
	[CreatedById] [int] NULL,
	[CreatedByEmail] [nvarchar](200) NULL,
	[CreatedAt] [datetime2](0) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_BookingAccessSuspensions]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_BookingAccessSuspensions](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[BookingId] [int] NOT NULL,
	[InvoiceId] [int] NULL,
	[SuspendedAt] [datetime2](0) NOT NULL,
	[Reason] [nvarchar](200) NULL,
	[OverrideUntil] [date] NULL,
	[MachinesBlocked] [bit] NOT NULL,
	[ResolvedAt] [datetime2](0) NULL,
	[ResolvedReason] [nvarchar](200) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_Cards]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_Cards](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Employee_no] [nvarchar](max) NULL,
	[Employee_name] [nvarchar](128) NULL,
	[Name] [nvarchar](max) NOT NULL,
	[Card_no] [nvarchar](32) NULL,
	[Valid_begin] [datetime2](7) NULL,
	[Valid_end] [datetime2](7) NULL,
	[Auto_delete] [bit] NOT NULL,
	[Notes] [nvarchar](max) NULL,
	[Booking_ref] [nvarchar](max) NULL,
	[Created_at] [datetime2](7) NOT NULL,
	[Status] [int] NOT NULL,
	[Created_by] [int] NULL,
	[Updated_by] [int] NULL,
	[Updated_on] [datetime2](0) NULL,
 CONSTRAINT [PK_WN_HIK_Cards] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_DashboardUsers]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_DashboardUsers](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Username] [nvarchar](64) NOT NULL,
	[Password_hash] [nvarchar](256) NOT NULL,
	[Created_at] [datetime2](0) NOT NULL,
	[Updated_at] [datetime2](0) NULL,
	[Role] [nvarchar](16) NOT NULL,
	[Name] [nvarchar](128) NULL,
	[Display_password] [nvarchar](256) NULL,
	[Status] [int] NOT NULL,
	[Created_by] [int] NULL,
	[Updated_by] [int] NULL,
 CONSTRAINT [PK_WN_HIK_DashboardUsers] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_HIK_DashboardUsers_username] UNIQUE NONCLUSTERED 
(
	[Username] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_DevCache]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_DevCache](
	[Device_id] [int] NOT NULL,
	[Users_snapshot] [nvarchar](max) NULL,
	[Users_UpdatedOn] [datetime2](0) NULL,
	[Cards_snapshot] [nvarchar](max) NULL,
	[Cards_UpdatedOn] [datetime2](0) NULL,
	[Status] [int] NOT NULL,
 CONSTRAINT [PK_WN_HIK_DevCache] PRIMARY KEY CLUSTERED 
(
	[Device_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_Devices]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_Devices](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Device_Name] [nvarchar](100) NOT NULL,
	[Host] [nvarchar](64) NOT NULL,
	[Port] [int] NOT NULL,
	[Use_https] [bit] NOT NULL,
	[Username] [nvarchar](64) NOT NULL,
	[Password] [nvarchar](128) NOT NULL,
	[Location] [nvarchar](128) NULL,
	[Model] [nvarchar](64) NULL,
	[Serial] [nvarchar](64) NULL,
	[Last_seen] [datetime2](0) NULL,
	[Online] [bit] NOT NULL,
	[Created_at] [datetime2](0) NOT NULL,
	[Code] [nvarchar](32) NULL,
	[Host2] [nvarchar](64) NULL,
	[Group_id] [int] NULL,
	[LocationId] [int] NULL,
 CONSTRAINT [PK_WN_HIK_Devices] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_HIK_Devices_host_port] UNIQUE NONCLUSTERED 
(
	[Host] ASC,
	[Port] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_EventCategories]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_EventCategories](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Code] [int] NOT NULL,
	[Label] [nvarchar](64) NOT NULL,
	[Is_denied] [bit] NOT NULL,
	[Status] [int] NOT NULL,
 CONSTRAINT [PK_WN_HIK_EventCategories] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_HIK_EventCategories_Code] UNIQUE NONCLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_Events]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_Events](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[device_id] [int] NOT NULL,
	[device_name] [nvarchar](100) NULL,
	[employee_no] [nvarchar](32) NULL,
	[name] [nvarchar](128) NULL,
	[card_no] [nvarchar](32) NULL,
	[access_event] [int] NULL,
	[machine_event_no] [bigint] NULL,
	[event_time] [datetime2](0) NOT NULL,
	[created_at] [datetime2](0) NOT NULL,
	[access_event_details] [nvarchar](64) NULL,
 CONSTRAINT [PK_WN_HIK_Events] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_FaceVault]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_FaceVault](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[employee_no] [nvarchar](32) NOT NULL,
	[name] [nvarchar](128) NOT NULL,
	[model_data] [nvarchar](max) NOT NULL,
	[updated_at] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_HIK_FaceVault] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_HIK_FaceVault] UNIQUE NONCLUSTERED 
(
	[employee_no] ASC,
	[name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_FpVault]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_FpVault](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[employee_no] [nvarchar](32) NOT NULL,
	[name] [nvarchar](128) NOT NULL,
	[finger_no] [int] NOT NULL,
	[template] [nvarchar](max) NOT NULL,
	[updated_at] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_HIK_FpVault] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_HIK_FpVault] UNIQUE NONCLUSTERED 
(
	[employee_no] ASC,
	[name] ASC,
	[finger_no] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_Groups]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_Groups](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](64) NOT NULL,
	[Created_at] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_HIK_Groups] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_HIK_Groups_Name] UNIQUE NONCLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_PendingOps]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_PendingOps](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[device_id] [int] NOT NULL,
	[op] [nvarchar](32) NOT NULL,
	[employee_no] [nvarchar](32) NULL,
	[payload] [nvarchar](max) NULL,
	[attempts] [int] NOT NULL,
	[last_error] [nvarchar](max) NULL,
	[created_at] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_HIK_PendingOps] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_PersonMap]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_PersonMap](
	[PersonId] [int] NOT NULL,
	[MachineID] [nvarchar](32) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[PersonId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_Settings]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_Settings](
	[key] [nvarchar](64) NOT NULL,
	[value] [nvarchar](256) NULL,
 CONSTRAINT [PK_WN_HIK_Settings] PRIMARY KEY CLUSTERED 
(
	[key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_SyncLog]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_SyncLog](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Employee_id] [int] NULL,
	[Device_id] [int] NULL,
	[Action] [nvarchar](64) NULL,
	[Ok] [bit] NULL,
	[Detail] [nvarchar](max) NULL,
	[Timestamp] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_HIK_SyncLog] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_Tags]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_Tags](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](48) NOT NULL,
	[Status] [int] NOT NULL,
	[Created_at] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_HIK_Tags] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_HIK_Tags_Name] UNIQUE NONCLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_Users]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_Users](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[employee_no] [nvarchar](32) NOT NULL,
	[name] [nvarchar](128) NOT NULL,
	[room] [nvarchar](256) NULL,
	[role] [nvarchar](16) NOT NULL,
	[machines] [nvarchar](max) NULL,
	[machine_count] [int] NOT NULL,
	[updated_at] [datetime2](0) NOT NULL,
	[cnic] [nvarchar](20) NULL,
	[tag_id] [int] NULL,
 CONSTRAINT [PK_WN_HIK_Users] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_HIK_Users] UNIQUE NONCLUSTERED 
(
	[employee_no] ASC,
	[name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_HIK_Visitors]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_HIK_Visitors](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Employee_no] [nvarchar](32) NOT NULL,
	[Name] [nvarchar](128) NOT NULL,
	[Card_no] [nvarchar](32) NULL,
	[Face_path] [nvarchar](260) NULL,
	[Valid_begin] [datetime2](7) NULL,
	[Valid_end] [datetime2](7) NULL,
	[Auto_delete] [bit] NOT NULL,
	[Status] [nvarchar](16) NOT NULL,
	[Notes] [nvarchar](max) NULL,
	[Booking_ref] [nvarchar](64) NULL,
	[Created_at] [datetime2](7) NOT NULL,
	[Created_by] [int] NULL,
 CONSTRAINT [PK_WN_HIK_Visitors] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_IntegrationLog]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_InvoiceDeliveryQueue]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_InvoiceDeliveryQueue](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[InvoiceId] [int] NOT NULL,
	[BookingId] [int] NULL,
	[TargetEmail] [nvarchar](255) NULL,
	[Attempts] [int] NOT NULL,
	[LastAttemptAt] [datetime2](7) NULL,
	[NextRetryAt] [datetime2](7) NULL,
	[Status] [nvarchar](50) NOT NULL,
	[LastError] [nvarchar](max) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_InvoiceDeliveryQueue] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_InvoiceLines]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
 CONSTRAINT [PK_WN_InvoiceLines] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_InvoicePrepaidPeriods]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_InvoicePrepaidPeriods](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[InvoiceId] [int] NOT NULL,
	[BookingId] [int] NOT NULL,
	[PeriodStartDate] [datetime] NOT NULL,
	[PeriodEndDate] [datetime] NOT NULL,
	[MonthName] [varchar](50) NOT NULL,
	[MonthlyRentAmount] [decimal](18, 2) NOT NULL,
	[Status] [varchar](20) NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Invoices]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[InvoiceTypeId] [int] NOT NULL,
	[AdvanceRentMonths] [int] NULL,
	[SecurityDepositMonths] [int] NULL,
	[SecurityDepositAmount] [decimal](18, 2) NOT NULL,
	[AccountsCoaId] [int] NULL,
	[BillingPeriodMonths] [int] NULL,
	[BillingPeriodStart] [datetime] NULL,
	[BillingPeriodEnd] [datetime] NULL,
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
	[RoomRentExclTax] [decimal](18, 4) NOT NULL,
	[ServiceCharges] [decimal](18, 4) NOT NULL,
	[TaxOnServiceCharges] [decimal](18, 4) NOT NULL,
	[WHTRate] [decimal](9, 4) NOT NULL,
	[WHTAmount] [decimal](18, 4) NOT NULL,
	[VoucherId] [int] NULL,
	[IsCustom] [bit] NOT NULL,
	[CustomReason] [nvarchar](500) NULL,
	[OneBillRef] [varchar](20) NULL,
	[OneBillNumber] [varchar](30) NULL,
	[OneBillStatus] [tinyint] NULL,
	[OneBillAttempts] [int] NOT NULL,
	[OneBillError] [nvarchar](500) NULL,
	[OneBillClaimedOn] [datetime] NULL,
	[OneBillCreatedOn] [datetime] NULL,
	[EzPayInvoiceId] [bigint] NULL,
	[EzPayTranAuthId] [bigint] NULL,
	[ApprovalStatus] [nvarchar](50) NULL,
	[ViewedAt] [datetime2](7) NULL,
	[ViewedBy] [int] NULL,
	[ApprovedAt] [datetime2](7) NULL,
	[ApprovedBy] [int] NULL,
	[LocationId] [int] NULL,
	[CustomerCode] [nvarchar](50) NULL,
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
/****** Object:  Table [dbo].[WN_KYCDocumentTypes]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_KYCDocumentTypes](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Code] [varchar](50) NOT NULL,
	[Name] [nvarchar](150) NOT NULL,
	[IndividualRequirement] [tinyint] NOT NULL,
	[BusinessRequirement] [tinyint] NOT NULL,
	[GroupCode] [varchar](30) NULL,
	[AlternativeCode] [varchar](30) NULL,
	[AllowMultiple] [bit] NOT NULL,
	[RequiresHolderName] [bit] NOT NULL,
	[RequiresExpiry] [bit] NOT NULL,
	[SortOrder] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_LeaseTemplates]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_LeaseTemplates](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[ContentHtml] [text] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[CreatedBy] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Locations]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[UpdatedOn] [datetime] NULL,
	[BranchId] [int] NULL,
	[CityId] [int] NULL,
	[CompanyId] [int] NULL,
	[IsActive] [bit] NOT NULL,
	[UpdatedById] [int] NULL,
	[Latitude] [decimal](10, 7) NULL,
	[Longitude] [decimal](10, 7) NULL,
	[CreatedById] [int] NULL,
	[ProvinceId] [int] NULL,
	[OneBillBillerCode] [varchar](10) NULL,
	[OneBillPrefix] [varchar](20) NULL,
	[OneBillFrom] [datetime] NULL,
 CONSTRAINT [PK_WN_Locations] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_LoginHistory]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_LoginHistory](
	[LoginId] [bigint] IDENTITY(1,1) NOT NULL,
	[UserId] [int] NULL,
	[Email] [nvarchar](256) NULL,
	[EventType] [varchar](10) NOT NULL,
	[FailureReason] [nvarchar](100) NULL,
	[IpAddress] [varchar](45) NULL,
	[UserAgent] [nvarchar](400) NULL,
	[EventAt] [datetime2](3) NOT NULL,
 CONSTRAINT [PK_WN_LoginHistory] PRIMARY KEY CLUSTERED 
(
	[LoginId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_MeetingRoomUsageLogs]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_MeetingRoomUsageLogs](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Guid] [uniqueidentifier] NOT NULL,
	[UserId] [int] NOT NULL,
	[PrivateSpaceBookingId] [int] NOT NULL,
	[EntitlementId] [int] NOT NULL,
	[MeetingRoomBookingId] [int] NOT NULL,
	[MeetingRoomSpaceId] [int] NOT NULL,
	[UsageDate] [date] NOT NULL,
	[StartDateTime] [datetime2](7) NOT NULL,
	[EndDateTime] [datetime2](7) NOT NULL,
	[AllocatedMinutes] [int] NOT NULL,
	[UsedMinutes] [int] NOT NULL,
	[DurationHours] [int] NOT NULL,
	[Status] [nvarchar](50) NOT NULL,
	[CreatedDate] [datetime2](7) NOT NULL,
	[CreatedById] [int] NULL,
	[CancelledDate] [datetime2](7) NULL,
	[CancelledById] [int] NULL,
	[Remarks] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_MembershipPlanFeatures]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_MembershipPlans]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_MemberShips]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_MessageTemplates]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_MessageTemplates](
	[TemplateKey] [nvarchar](100) NOT NULL,
	[Template] [nvarchar](2000) NOT NULL,
	[UpdatedById] [int] NULL,
	[UpdatedOn] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[TemplateKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_MobileAccessVerifications]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_MobileAccessVerifications](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PersonId] [int] NOT NULL,
	[BookingDetailId] [int] NULL,
	[AccountEmail] [nvarchar](256) NOT NULL,
	[VerifiedAt] [datetime2](0) NOT NULL,
	[ExpiresAt] [datetime2](0) NOT NULL,
	[LastUnlockAt] [datetime2](0) NULL,
	[UnlockCount] [int] NOT NULL,
 CONSTRAINT [PK_WN_MobileAccessVerifications] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_MobileAccessVerifications_Person_Account] UNIQUE NONCLUSTERED 
(
	[PersonId] ASC,
	[AccountEmail] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_NAV_BadgeReads]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_NAV_BadgeReads](
	[UserEmail] [nvarchar](200) NOT NULL,
	[RouteKey] [nvarchar](100) NOT NULL,
	[ReadItems] [nvarchar](max) NULL,
	[ReadAt] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_NAV_BadgeReads] PRIMARY KEY CLUSTERED 
(
	[UserEmail] ASC,
	[RouteKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_NetworkAlertLocks]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_NetworkAlertLocks](
	[LocationId] [int] NOT NULL,
	[ClaimedAt] [datetime2](0) NULL,
	[LastRunAt] [datetime2](0) NULL,
 CONSTRAINT [PK_WN_NetworkAlertLocks] PRIMARY KEY CLUSTERED 
(
	[LocationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_NetworkAlertLog]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_NetworkAlertLog](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[LocationId] [int] NOT NULL,
	[Kind] [nvarchar](20) NOT NULL,
	[Subject] [nvarchar](300) NOT NULL,
	[Summary] [nvarchar](1000) NOT NULL,
	[Recipients] [nvarchar](1000) NOT NULL,
	[Status] [nvarchar](10) NOT NULL,
	[Error] [nvarchar](500) NULL,
	[SentAt] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_NetworkAlertLog] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_NetworkAlertRecipients]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_NetworkAlertRecipients](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[LocationId] [int] NOT NULL,
	[Email] [nvarchar](256) NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
	[CreatedBy] [nvarchar](256) NULL,
 CONSTRAINT [PK_WN_NetworkAlertRecipients] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_NetworkAlertState]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_NetworkAlertState](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Kind] [nvarchar](10) NOT NULL,
	[ItemKey] [nvarchar](200) NOT NULL,
	[LocationId] [int] NOT NULL,
	[Name] [nvarchar](200) NOT NULL,
	[Detail] [nvarchar](500) NULL,
	[DownSince] [datetime2](0) NOT NULL,
	[FirstAlertAt] [datetime2](0) NULL,
	[LastReminderAt] [datetime2](0) NULL,
	[RecoveredAt] [datetime2](0) NULL,
	[UpdatedAt] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_NetworkAlertState] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_OfferingType]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_OfferingType](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Description] [nvarchar](100) NULL,
	[DiscountCap] [decimal](5, 2) NULL,
	[Status] [bit] NULL,
	[LocationId] [int] NULL,
 CONSTRAINT [PK__WN_Offer__3214EC0777B4DAE8] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_OneBill_Payments]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_OneBill_Payments](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[BillNo] [varchar](30) NOT NULL,
	[RcvdAmount] [decimal](18, 2) NOT NULL,
	[RcvdDate] [date] NULL,
	[RcvdTime] [time](0) NULL,
	[RcvdVia] [nvarchar](50) NULL,
	[RawBody] [nvarchar](max) NULL,
	[RemoteIp] [varchar](64) NULL,
	[ReceivedOn] [datetime2](3) NOT NULL,
	[InvoiceId] [int] NULL,
	[Outcome] [varchar](20) NOT NULL,
	[Message] [nvarchar](500) NULL,
	[PaidTotalAfter] [decimal](18, 4) NULL,
	[InvoiceStatusAfter] [int] NULL,
 CONSTRAINT [PK_WN_OneBill_Payments] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_PaymentLines]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
 CONSTRAINT [PK_WN_PaymentLines] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_PaymentMethods]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_Payments]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Payments](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[MembershipId] [int] NULL,
	[Amount] [decimal](18, 2) NULL,
	[TransactionRef] [nvarchar](120) NULL,
	[AccountId] [int] NULL,
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
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
 CONSTRAINT [PK_WN_Payments] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Persons]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Persons](
	[PersonId] [int] IDENTITY(1,1) NOT NULL,
	[PersonGuid] [uniqueidentifier] NOT NULL,
	[Name] [nvarchar](150) NOT NULL,
	[Email] [nvarchar](150) NOT NULL,
	[Phone] [nvarchar](20) NOT NULL,
	[IdType] [varchar](10) NOT NULL,
	[IdNumber] [varchar](30) NOT NULL,
	[CreatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[PersonId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_PlanFeatures]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_PricingPlans]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_PricingRules]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_PricingTierTypes]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_ProvinceChargeTypeTax]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ProvinceChargeTypeTax](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ProvinceId] [int] NOT NULL,
	[ChargeTypeId] [tinyint] NOT NULL,
	[TaxApplicable] [bit] NOT NULL,
	[CreatedOn] [datetime] NOT NULL,
 CONSTRAINT [PK_WN_ProvinceChargeTypeTax] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_ProvinceChargeTypeTax_Province_ChargeType] UNIQUE NONCLUSTERED 
(
	[ProvinceId] ASC,
	[ChargeTypeId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ProvincesTemp]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ProvincesTemp](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[Code] [nvarchar](20) NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Lookup_ProvinceState] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_QuotationActivities]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_QuotationActivities](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NOT NULL,
	[QuotationId] [int] NOT NULL,
	[Version] [int] NOT NULL,
	[ActivityType] [nvarchar](50) NOT NULL,
	[Message] [nvarchar](1000) NOT NULL,
	[CustomerNote] [nvarchar](1000) NULL,
	[CreatedByUserId] [int] NULL,
	[CreatedDate] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_QuotationDetails]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_QuotationDetails](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Guid] [uniqueidentifier] NOT NULL,
	[QuotationId] [int] NOT NULL,
	[FeeType] [nvarchar](50) NOT NULL,
	[Description] [nvarchar](200) NOT NULL,
	[Quantity] [decimal](18, 2) NOT NULL,
	[UnitPrice] [decimal](18, 2) NOT NULL,
	[Amount] [decimal](18, 2) NOT NULL,
	[CreatedDate] [datetime2](7) NOT NULL,
	[CreatedById] [int] NULL,
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_QuotationResponses]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_QuotationResponses](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NOT NULL,
	[QuotationId] [int] NOT NULL,
	[Version] [int] NOT NULL,
	[ResponseType] [nvarchar](20) NOT NULL,
	[Note] [nvarchar](1000) NULL,
	[RespondedByUserId] [int] NULL,
	[RespondedByCustomerId] [int] NULL,
	[RespondedDate] [datetime2](7) NOT NULL,
	[CreatedDate] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Quotations]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Quotations](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Guid] [uniqueidentifier] NOT NULL,
	[BookingId] [int] NULL,
	[QuotationNumber] [nvarchar](50) NOT NULL,
	[QuotationDate] [date] NOT NULL,
	[ValidUntil] [date] NOT NULL,
	[CustomerId] [int] NOT NULL,
	[SpaceId] [int] NOT NULL,
	[StartDateTime] [datetime2](7) NOT NULL,
	[EndDateTime] [datetime2](7) NOT NULL,
	[SubtotalAmount] [decimal](18, 2) NOT NULL,
	[DiscountPercentage] [decimal](5, 2) NOT NULL,
	[DiscountAmount] [decimal](18, 2) NOT NULL,
	[TotalAmount] [decimal](18, 2) NOT NULL,
	[Remarks] [nvarchar](500) NULL,
	[Status] [nvarchar](50) NOT NULL,
	[Version] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedDate] [datetime2](7) NOT NULL,
	[CreatedById] [int] NULL,
	[UpdatedDate] [datetime2](7) NULL,
	[UpdatedById] [int] NULL,
	[DiscountType] [nvarchar](20) NOT NULL,
	[SecurityDeposit] [decimal](18, 2) NOT NULL,
	[FloorId] [int] NULL,
	[AdvanceRentMonths] [int] NOT NULL,
	[SecurityDepositMonths] [int] NOT NULL,
	[InitialAmountPayable] [decimal](18, 2) NOT NULL,
	[RemainingContractRent] [decimal](18, 2) NOT NULL,
	[BillingPeriodMonths] [int] NOT NULL,
	[SupportChargesId] [tinyint] NULL,
	[AppliedChargePercentage] [decimal](5, 2) NULL,
	[AppliedTaxPercentage] [decimal](5, 2) NULL,
	[SupportChargeAmount] [decimal](18, 2) NULL,
	[TaxAmount] [decimal](18, 2) NULL,
	[PerSeatBasePrice] [decimal](18, 2) NULL,
	[Capacity] [int] NULL,
	[MonthlyBasePrice] [decimal](18, 2) NULL,
	[MaxDiscountPercent] [decimal](5, 2) NULL,
	[OfferingType] [nvarchar](50) NOT NULL,
	[PerSeatSupportRate] [decimal](18, 2) NULL,
	[WHTaxId] [int] NULL,
	[SendWhtInvoice] [bit] NOT NULL,
 CONSTRAINT [PK__WN_Quota__3214EC0704E4355A] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_RefreshTokens_Temp]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_Refunds]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
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
/****** Object:  Table [dbo].[WN_Roles]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Roles](
	[Id] [int] NOT NULL,
	[RoleName] [nvarchar](50) NOT NULL,
	[DisplayName] [nvarchar](100) NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_Roles] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_Roles_RoleName] UNIQUE NONCLUSTERED 
(
	[RoleName] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_ScheduledReportRuns]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ScheduledReportRuns](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ReportKey] [nvarchar](50) NOT NULL,
	[PeriodKey] [nvarchar](20) NOT NULL,
	[SentAt] [datetime2](0) NOT NULL,
	[Recipients] [nvarchar](max) NULL,
	[Status] [nvarchar](20) NOT NULL,
	[Error] [nvarchar](1000) NULL,
 CONSTRAINT [PK_WN_ScheduledReportRuns] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_ScheduledReportRuns_Report_Period] UNIQUE NONCLUSTERED 
(
	[ReportKey] ASC,
	[PeriodKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SecurityDeposits]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[AccountReceivableId] [int] NULL,
	[RentAccountId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
	[RefNo] [varchar](50) NULL,
	[RefId] [int] NULL,
	[CustomerId] [varchar](50) NULL,
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
/****** Object:  Table [dbo].[WN_ServiceSwitches]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_ServiceSwitches](
	[ServiceName] [nvarchar](100) NOT NULL,
	[IsEnabled] [bit] NOT NULL,
	[UpdatedBy] [nvarchar](100) NULL,
	[UpdatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_ServiceSwitches] PRIMARY KEY CLUSTERED 
(
	[ServiceName] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SpaceAmenities]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_SpaceCategories]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_SpaceConfig]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[OpeningTime] [nvarchar](5) NOT NULL,
	[ClosingTime] [nvarchar](5) NOT NULL,
	[UpdatedOn] [datetime] NULL,
	[UpdatedBy] [nvarchar](255) NULL,
	[SpaceTypeId] [int] NULL,
	[SecurityDeposit] [decimal](10, 2) NOT NULL,
	[LocationId] [int] NULL,
	[RentAccountId] [int] NULL,
	[PricePerHour] [decimal](18, 2) NULL,
	[PricePerDay] [decimal](18, 2) NULL,
	[PricePerMonth] [decimal](18, 2) NULL,
	[Amenities] [nvarchar](max) NULL,
	[Status] [int] NOT NULL,
	[FloorId] [int] NULL,
	[PricePerSeat] [decimal](18, 4) NOT NULL,
	[SecurityReceivedId] [int] NULL,
	[AccountReceivableId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SpaceInventoryConfig]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_SpacePricing]    Script Date: 04/11/2026 10:16:52 am ******/
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
/****** Object:  Table [dbo].[WN_Spaces]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Spaces](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Code] [nvarchar](50) NULL,
	[Name] [nvarchar](max) NULL,
	[Capacity] [smallint] NOT NULL,
	[Description] [nvarchar](max) NULL,
	[ImageUrl] [nvarchar](max) NULL,
	[Amenities] [nvarchar](max) NULL,
	[Status] [int] NULL,
	[CreatedOn] [datetime] NULL,
	[UpdatedOn] [datetime] NULL,
	[FloorId] [int] NULL,
	[RentAccountId] [int] NULL,
	[SecurityRecivedId] [int] NULL,
	[IsActive] [bit] NOT NULL,
	[UpdatedById] [int] NULL,
	[LocationId] [int] NULL,
	[SpaceTypeId] [int] NULL,
	[CreatedById] [int] NULL,
	[Price] [decimal](18, 2) NULL,
	[BillingPeriodId] [tinyint] NULL,
	[OptimumCapacity] [smallint] NOT NULL,
	[MaxDiscountPercent] [decimal](5, 2) NOT NULL,
	[AccountReceivableId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
 CONSTRAINT [PK_WN_Spaces] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_SpaceTypes]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[CategoryId] [tinyint] NULL,
	[Name] [nvarchar](100) NULL,
	[IsActive] [bit] NOT NULL,
	[AccountReceivableId] [int] NULL,
	[ServicesIncomeId] [int] NULL,
	[SalesTaxId] [int] NULL,
	[SecurityReceivedId] [int] NULL,
	[LocationId] [int] NOT NULL,
 CONSTRAINT [PK_WN_SpaceTypes] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Tax]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_Tax](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ProvinceId] [int] NOT NULL,
	[TaxPercentage] [decimal](5, 2) NOT NULL,
	[StartDate] [datetime] NOT NULL,
	[EndDate] [datetime] NULL,
	[CreatedOn] [datetime] NOT NULL,
 CONSTRAINT [PK_WN_Tax] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_UNIFI_ClientAliases]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_UNIFI_ClientAliases](
	[Mac] [nvarchar](17) NOT NULL,
	[Alias] [nvarchar](100) NOT NULL,
	[UpdatedAt] [datetime2](0) NOT NULL,
	[UpdatedByEmail] [nvarchar](200) NULL,
PRIMARY KEY CLUSTERED 
(
	[Mac] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_UNIFI_History]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_UNIFI_History](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[SampledAt] [datetime2](0) NOT NULL,
	[Wifi] [int] NOT NULL,
	[Wired] [int] NOT NULL,
	[Guest] [int] NOT NULL,
	[Online] [int] NOT NULL,
	[Offline] [int] NOT NULL,
	[SitesJson] [nvarchar](max) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_UNIFI_MacFilterLog]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_UNIFI_MacFilterLog](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[LoggedAt] [datetime2](0) NOT NULL,
	[WlanId] [nvarchar](50) NOT NULL,
	[Ssid] [nvarchar](100) NOT NULL,
	[Mac] [nvarchar](17) NULL,
	[Action] [nvarchar](20) NOT NULL,
	[Policy] [nvarchar](10) NULL,
	[Reason] [nvarchar](500) NULL,
	[ByEmail] [nvarchar](200) NULL,
	[ByUserId] [int] NULL,
	[PreviousList] [nvarchar](max) NULL,
 CONSTRAINT [PK_WN_UNIFI_MacFilterLog] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_UNIFI_Settings]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_UNIFI_Settings](
	[SettingKey] [nvarchar](50) NOT NULL,
	[SettingValue] [nvarchar](1000) NULL,
	[UpdatedAt] [datetime2](0) NOT NULL,
	[UpdatedBy] [nvarchar](200) NULL,
 CONSTRAINT [PK_WN_UNIFI_Settings] PRIMARY KEY CLUSTERED 
(
	[SettingKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_UNIFI_SiteLocations]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_UNIFI_SiteLocations](
	[SiteId] [nvarchar](64) NOT NULL,
	[LocationId] [int] NOT NULL,
	[SiteName] [nvarchar](200) NULL,
	[UpdatedBy] [nvarchar](200) NULL,
	[UpdatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_UNIFI_SiteLocations] PRIMARY KEY CLUSTERED 
(
	[SiteId] ASC,
	[LocationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_UNIFI_SsidDevices]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_UNIFI_SsidDevices](
	[WlanId] [nvarchar](50) NOT NULL,
	[Mac] [nvarchar](17) NOT NULL,
	[Name] [nvarchar](100) NULL,
	[RoomNo] [nvarchar](50) NULL,
	[UpdatedAt] [datetime2](0) NOT NULL,
	[UpdatedBy] [nvarchar](200) NULL,
 CONSTRAINT [PK_WN_UNIFI_SsidDevices] PRIMARY KEY CLUSTERED 
(
	[WlanId] ASC,
	[Mac] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_UserLocations]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_UserLocations](
	[UserId] [int] NOT NULL,
	[LocationId] [int] NOT NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
	[CreatedById] [int] NULL,
 CONSTRAINT [PK_WN_UserLocations] PRIMARY KEY CLUSTERED 
(
	[UserId] ASC,
	[LocationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_UserPermissions]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_UserPermissions](
	[UserId] [int] NOT NULL,
	[Permission] [nvarchar](50) NOT NULL,
	[GrantedById] [int] NULL,
	[GrantedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_WN_UserPermissions] PRIMARY KEY CLUSTERED 
(
	[UserId] ASC,
	[Permission] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_Users]    Script Date: 04/11/2026 10:16:52 am ******/
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
	[UpdatedOn] [datetime] NULL,
	[CompanyId] [int] NULL,
	[Status] [int] NULL,
	[PasswordHash] [nvarchar](500) NULL,
	[IsActive] [bit] NOT NULL,
	[Address] [nvarchar](500) NULL,
	[CnicOrPassport] [nvarchar](50) NULL,
	[AvatarUrl] [nvarchar](500) NULL,
	[Notes] [nvarchar](1000) NULL,
	[CreatedById] [int] NULL,
	[CityId] [int] NULL,
	[LocationId] [int] NULL,
	[ManagerId] [int] NULL,
 CONSTRAINT [PK_WN_Users] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
ALTER TABLE [dbo].[WN_Users] WITH CHECK ADD CONSTRAINT [FK_WN_Users_ManagerId_WN_Users]
FOREIGN KEY ([ManagerId]) REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_Users] WITH CHECK ADD CONSTRAINT [CK_WN_Users_ManagerId_NotSelf]
CHECK ([ManagerId] <> [Id])
GO
CREATE NONCLUSTERED INDEX [IX_WN_Users_ManagerId] ON [dbo].[WN_Users]
(
	[ManagerId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_WhatsApp_BotState]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_WhatsApp_BotState](
	[PhoneNumber] [nvarchar](30) NOT NULL,
	[State] [nvarchar](40) NOT NULL,
	[Data] [nvarchar](max) NULL,
	[UpdatedOn] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_WhatsApp_BotState] PRIMARY KEY CLUSTERED 
(
	[PhoneNumber] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_WhatsApp_Contacts]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_WhatsApp_Contacts](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PhoneNumber] [nvarchar](30) NOT NULL,
	[Name] [nvarchar](200) NULL,
	[Tags] [nvarchar](500) NULL,
	[AdminNotes] [nvarchar](2000) NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
	[UpdatedOn] [datetime2](0) NULL,
 CONSTRAINT [PK_WN_WhatsApp_Contacts] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_WhatsApp_Contacts_Phone] UNIQUE NONCLUSTERED 
(
	[PhoneNumber] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_WhatsApp_Conversations]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_WhatsApp_Conversations](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ContactId] [int] NOT NULL,
	[Status] [nvarchar](10) NOT NULL,
	[LastIncomingAt] [datetime2](0) NULL,
	[LastMessageAt] [datetime2](0) NULL,
	[LastMessageText] [nvarchar](500) NULL,
	[UnreadCount] [int] NOT NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
	[UpdatedOn] [datetime2](0) NULL,
 CONSTRAINT [PK_WN_WhatsApp_Conversations] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WN_WhatsApp_Conversations_Contact] UNIQUE NONCLUSTERED 
(
	[ContactId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_WhatsApp_Messages]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_WhatsApp_Messages](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[ConversationId] [int] NOT NULL,
	[WhatsAppMessageId] [nvarchar](128) NULL,
	[Direction] [nvarchar](10) NOT NULL,
	[MessageType] [nvarchar](20) NOT NULL,
	[MessageText] [nvarchar](max) NULL,
	[SentByUserId] [int] NULL,
	[CreatedOn] [datetime2](0) NOT NULL,
 CONSTRAINT [PK_WN_WhatsApp_Messages] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[WN_WHTaxRate]    Script Date: 04/11/2026 10:16:52 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[WN_WHTaxRate](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdGUID] [uniqueidentifier] NULL,
	[Description] [nvarchar](max) NULL,
	[Code] [nvarchar](max) NULL,
	[WHRate] [decimal](18, 4) NULL,
	[ProvinceId] [int] NULL,
	[CompanyId] [int] NULL,
	[BranchId] [int] NULL,
	[Status] [tinyint] NULL,
	[CreatedById] [int] NULL,
	[CreatedOn] [smalldatetime] NULL,
	[UpdatedById] [int] NULL,
	[UpdatedOn] [smalldatetime] NULL,
	[WHAccountId] [int] NULL,
 CONSTRAINT [PK_WN_WHTaxRate] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
ALTER TABLE [dbo].[WN_AccessCards] ADD  CONSTRAINT [DF_WN_AccessCards_IdGUID]  DEFAULT (newid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_AccessCards] ADD  CONSTRAINT [DF_WN_AccessCards_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_AccessStatus] ADD  DEFAULT ((1)) FOR [IsEnabled]
GO
ALTER TABLE [dbo].[WN_AccessStatus] ADD  DEFAULT (sysutcdatetime()) FOR [GrantedAt]
GO
ALTER TABLE [dbo].[WN_AgreementESignatures] ADD  CONSTRAINT [DF_WN_AgreementESignatures_CreatedOn]  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Agreements] ADD  DEFAULT (newid()) FOR [Guid]
GO
ALTER TABLE [dbo].[WN_Agreements] ADD  DEFAULT ('AgreementSent') FOR [Status]
GO
ALTER TABLE [dbo].[WN_Agreements] ADD  DEFAULT (sysutcdatetime()) FOR [SentDate]
GO
ALTER TABLE [dbo].[WN_Agreements] ADD  DEFAULT ((30)) FOR [RefundDays]
GO
ALTER TABLE [dbo].[WN_Agreements] ADD  DEFAULT ((0.00)) FOR [FeeAmount]
GO
ALTER TABLE [dbo].[WN_Agreements] ADD  DEFAULT ((0.00)) FOR [SecurityDeposit]
GO
ALTER TABLE [dbo].[WN_Agreements] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Amenities] ADD  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Amenities] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_AmountFields] ADD  DEFAULT ('PKR') FOR [Currency]
GO
ALTER TABLE [dbo].[WN_Announcement] ADD  DEFAULT (newid()) FOR [Id]
GO
ALTER TABLE [dbo].[WN_Announcement] ADD  DEFAULT ('Draft') FOR [Status]
GO
ALTER TABLE [dbo].[WN_Announcement] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_AnnouncementRecipient] ADD  DEFAULT ('Pending') FOR [Status]
GO
ALTER TABLE [dbo].[WN_AnnouncementRecipient] ADD  DEFAULT ((0)) FOR [RetryCount]
GO
ALTER TABLE [dbo].[WN_AttendanceHolidays] ADD  CONSTRAINT [DF_WN_AttendanceHolidays_CreatedOn]  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_AttendanceShiftAssignments] ADD  CONSTRAINT [DF_WN_AttendanceShiftAssignments_UpdatedOn]  DEFAULT (sysutcdatetime()) FOR [UpdatedOn]
GO
ALTER TABLE [dbo].[WN_AttendanceShifts] ADD  CONSTRAINT [DF_WN_AttendanceShifts_GraceMinutes]  DEFAULT ((0)) FOR [GraceMinutes]
GO
ALTER TABLE [dbo].[WN_AttendanceShifts] ADD  CONSTRAINT [DF_WN_AttendanceShifts_WorkDays]  DEFAULT (N'Mon,Tue,Wed,Thu,Fri') FOR [WorkDays]
GO
ALTER TABLE [dbo].[WN_AttendanceShifts] ADD  CONSTRAINT [DF_WN_AttendanceShifts_IsDefault]  DEFAULT ((0)) FOR [IsDefault]
GO
ALTER TABLE [dbo].[WN_AttendanceShifts] ADD  CONSTRAINT [DF_WN_AttendanceShifts_IsActive]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_AttendanceShifts] ADD  CONSTRAINT [DF_WN_AttendanceShifts_CreatedOn]  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_AttendantCapacityRequests] ADD  CONSTRAINT [DF_WN_AttendantCapacityRequests_Status]  DEFAULT (N'Pending') FOR [Status]
GO
ALTER TABLE [dbo].[WN_AuditLog] ADD  DEFAULT (sysutcdatetime()) FOR [ChangedAt]
GO
ALTER TABLE [dbo].[WN_AuthHandoff] ADD  CONSTRAINT [DF_WN_AuthHandoff_CreatedAt]  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_BankReceipts] ADD  CONSTRAINT [DF_WN_BankReceipts_UploadedDate]  DEFAULT (getdate()) FOR [UploadedDate]
GO
ALTER TABLE [dbo].[WN_BankReceipts] ADD  CONSTRAINT [DF_WN_BankReceipts_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_BankReceipts] ADD  CONSTRAINT [DF_WN_BankReceipts_CreatedTime]  DEFAULT (getdate()) FOR [CreatedTime]
GO
ALTER TABLE [dbo].[WN_BankReceiptsDetails] ADD  CONSTRAINT [DF_WN_BankReceiptsDetails_GUIDs]  DEFAULT (newsequentialid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_BillingPeriods] ADD  DEFAULT ((0)) FOR [SortOrder]
GO
ALTER TABLE [dbo].[WN_BillingPeriods] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_BillingSettings] ADD  DEFAULT (sysutcdatetime()) FOR [UpdatedOn]
GO
ALTER TABLE [dbo].[WN_BookingAttendants] ADD  DEFAULT ((0)) FOR [IsOverCapacity]
GO
ALTER TABLE [dbo].[WN_BookingAttendants] ADD  DEFAULT ((0)) FOR [ExcessSeatCount]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT (newid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT ((0)) FOR [Amount]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT ((0)) FOR [AppliedChargePercentage]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT ((0)) FOR [AppliedTaxPercentage]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT ((0)) FOR [SupportChargeAmount]
GO
ALTER TABLE [dbo].[WN_BookingDetails] ADD  DEFAULT ((0)) FOR [TaxAmount]
GO
ALTER TABLE [dbo].[WN_BookingEmployeeRequests] ADD  CONSTRAINT [DF_WN_BookingEmployeeRequests_Status]  DEFAULT (N'Pending') FOR [Status]
GO
ALTER TABLE [dbo].[WN_BookingEmployeeRequests] ADD  CONSTRAINT [DF_WN_BookingEmployeeRequests_AddedAt]  DEFAULT (sysutcdatetime()) FOR [AddedAt]
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
ALTER TABLE [dbo].[WN_BookingMeetingRoomEntitlements] ADD  DEFAULT (newid()) FOR [Guid]
GO
ALTER TABLE [dbo].[WN_BookingMeetingRoomEntitlements] ADD  DEFAULT ((360)) FOR [AllocatedMinutes]
GO
ALTER TABLE [dbo].[WN_BookingMeetingRoomEntitlements] ADD  DEFAULT ('Active') FOR [Status]
GO
ALTER TABLE [dbo].[WN_BookingMeetingRoomEntitlements] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedDate]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_IdGUID]  DEFAULT (newid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_HubActionSatus]  DEFAULT ((2)) FOR [HubActionSatus]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_BatchSyncStatus]  DEFAULT ((0)) FOR [BatchSyncStatus]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Booki__74FEC837]  DEFAULT ((1)) FOR [BookingStatusId]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__IsDel__75F2EC70]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Disco__43F17C88]  DEFAULT ((0.00)) FOR [DiscountPercentage]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Subto__44E5A0C1]  DEFAULT ((0.00)) FOR [SubtotalAmount]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__IsFre__45D9C4FA]  DEFAULT ((0)) FOR [IsFree]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Disco__7771208E]  DEFAULT ('Percentage') FOR [DiscountType]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Disco__786544C7]  DEFAULT ((0)) FOR [DiscountAmount]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Advan__0E5485E6]  DEFAULT ((0)) FOR [AdvanceRentMonths]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Secur__0F48AA1F]  DEFAULT ((0)) FOR [SecurityDepositMonths]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Secur__103CCE58]  DEFAULT ((0.00)) FOR [SecurityDepositRequired]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Secur__1130F291]  DEFAULT ((0.00)) FOR [SecurityDepositPaid]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Billi__272033B0]  DEFAULT ((3)) FOR [BillingPeriodMonths]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF__WN_Bookin__Secur__281457E9]  DEFAULT ((0)) FOR [SecurityDepositCharged]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_ShiftType]  DEFAULT ('24/7') FOR [ShiftType]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_SendWHTInvoice]  DEFAULT ((0)) FOR [SendWHTInvoice]
GO
ALTER TABLE [dbo].[WN_Bookings] ADD  CONSTRAINT [DF_WN_Bookings_TransferCreditBalance]  DEFAULT ((0.00)) FOR [TransferCreditBalance]
GO
ALTER TABLE [dbo].[WN_BookingTransfers] ADD  CONSTRAINT [DF_WN_BookingTransfers_TransferFee]  DEFAULT ((0.00)) FOR [TransferFee]
GO
ALTER TABLE [dbo].[WN_BookingTransfers] ADD  CONSTRAINT [DF_WN_BookingTransfers_CreatedOn]  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_BookTour] ADD  CONSTRAINT [DF_WN_BookTour_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_BookTour] ADD  CONSTRAINT [DF_WN_BookTour_Status]  DEFAULT ((1)) FOR [Status]
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
ALTER TABLE [dbo].[WN_ChargeTypeAccountMapping] ADD  DEFAULT (getdate()) FOR [EffectiveFrom]
GO
ALTER TABLE [dbo].[WN_ChargeTypeRate] ADD  DEFAULT (getutcdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_ChargeTypeRate] ADD  DEFAULT ('Percentage') FOR [ChargeMode]
GO
ALTER TABLE [dbo].[WN_ChargeTypeRate] ADD  DEFAULT ((0.00)) FOR [FixedAmount]
GO
ALTER TABLE [dbo].[WN_ChargeTypes] ADD  DEFAULT ((1)) FOR [IsDebit]
GO
ALTER TABLE [dbo].[WN_ChargeTypes] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Complaints] ADD  CONSTRAINT [DF_WN_Complaints_Status]  DEFAULT (N'open') FOR [Status]
GO
ALTER TABLE [dbo].[WN_Complaints] ADD  CONSTRAINT [DF_WN_Complaints_IsFollowUp]  DEFAULT ((0)) FOR [IsFollowUp]
GO
ALTER TABLE [dbo].[WN_Complaints] ADD  CONSTRAINT [DF_WN_Complaints_Notified]  DEFAULT ((0)) FOR [ResolvedNotificationSent]
GO
ALTER TABLE [dbo].[WN_Complaints] ADD  CONSTRAINT [DF_WN_Complaints_CreatedOn]  DEFAULT (sysdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_ContactFeedback] ADD  CONSTRAINT [DF_WN_ContactFeedback_CreatedOn]  DEFAULT (sysdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_ContactFeedbackMoved] ADD  CONSTRAINT [DF_WN_ContactFeedbackMoved_MovedOn]  DEFAULT (sysutcdatetime()) FOR [MovedOn]
GO
ALTER TABLE [dbo].[WN_Contacts] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Contacts] ADD  DEFAULT ('Contact') FOR [ContactType]
GO
ALTER TABLE [dbo].[WN_Contacts] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_Contacts] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_ContactsFromBookTour] ADD  CONSTRAINT [DF_WN_ContactsFromBookTour_CopiedOn]  DEFAULT (sysutcdatetime()) FOR [CopiedOn]
GO
ALTER TABLE [dbo].[WN_Countries] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Countries] ADD  DEFAULT (N'PKR') FOR [CurrencyCode]
GO
ALTER TABLE [dbo].[WN_Countries] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Countries] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_CustomerAttendants] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_CustomerAttendants] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_CustomerKYCDocuments] ADD  DEFAULT ((1)) FOR [SlotNo]
GO
ALTER TABLE [dbo].[WN_CustomerKYCDocuments] ADD  DEFAULT ((1)) FOR [VersionNo]
GO
ALTER TABLE [dbo].[WN_CustomerKYCDocuments] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_CustomerKYCDocuments] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_CustomerKYCDocuments] ADD  DEFAULT (sysdatetime()) FOR [UploadedOn]
GO
ALTER TABLE [dbo].[WN_Customers] ADD  CONSTRAINT [DF__WN_Custom__IdGUI__6196EE08]  DEFAULT (newid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_Customers] ADD  CONSTRAINT [DF__WN_Custom__IsAct__628B1241]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Customers] ADD  CONSTRAINT [DF__WN_Custom__Creat__637F367A]  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_Customers] ADD  CONSTRAINT [DF_WN_Customers_AttendanceEnabled]  DEFAULT ((0)) FOR [AttendanceEnabled]
GO
ALTER TABLE [dbo].[WN_CustomerSTInvoice] ADD  DEFAULT (newid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_CustomerSTInvoice] ADD  DEFAULT ('9805.9200') FOR [TariffHeading]
GO
ALTER TABLE [dbo].[WN_CustomerSTInvoice] ADD  DEFAULT ('Business Support Services') FOR [TariffLabel]
GO
ALTER TABLE [dbo].[WN_CustomerSTInvoice] ADD  DEFAULT ((0.00)) FOR [RoomRentTaxRate]
GO
ALTER TABLE [dbo].[WN_CustomerSTInvoice] ADD  DEFAULT ((0.00)) FOR [RoomRentTaxAmount]
GO
ALTER TABLE [dbo].[WN_CustomerSTInvoice] ADD  DEFAULT ((0.00)) FOR [SecurityDepositTaxRate]
GO
ALTER TABLE [dbo].[WN_CustomerSTInvoice] ADD  DEFAULT ((0.00)) FOR [SecurityDepositTaxAmount]
GO
ALTER TABLE [dbo].[WN_CustomerSTInvoice] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_CustomInvoices] ADD  CONSTRAINT [DF_WN_CustomInvoices_CreatedOn]  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_DeviceToken] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_DeviceToken] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_EmailOutbox] ADD  CONSTRAINT [DF_WN_EmailOutbox_Status]  DEFAULT (N'Pending') FOR [Status]
GO
ALTER TABLE [dbo].[WN_EmailOutbox] ADD  CONSTRAINT [DF_WN_EmailOutbox_Attempts]  DEFAULT ((0)) FOR [Attempts]
GO
ALTER TABLE [dbo].[WN_EmailOutbox] ADD  CONSTRAINT [DF_WN_EmailOutbox_CreatedOn]  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_FeeReceipts] ADD  CONSTRAINT [DF_Table_1_PublicId]  DEFAULT (newsequentialid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_FeeReceipts] ADD  CONSTRAINT [DF_WN_FeeReceipts_InvoiceTypeId]  DEFAULT ((1)) FOR [InvoiceTypeId]
GO
ALTER TABLE [dbo].[WN_FeeReceipts] ADD  CONSTRAINT [DF_WN_FeeReceipts_RoomRentExclTax]  DEFAULT ((0.0000)) FOR [RoomRentExclTax]
GO
ALTER TABLE [dbo].[WN_FeeReceipts] ADD  CONSTRAINT [DF_WN_FeeReceipts_ServiceCharges]  DEFAULT ((0.0000)) FOR [ServiceCharges]
GO
ALTER TABLE [dbo].[WN_FeeReceipts] ADD  CONSTRAINT [DF_WN_FeeReceipts_TaxOnServiceCharges]  DEFAULT ((0.0000)) FOR [TaxOnServiceCharges]
GO
ALTER TABLE [dbo].[WN_FeeReceipts] ADD  CONSTRAINT [DF_WN_FeeReceipts_WHTAmount]  DEFAULT ((0.0000)) FOR [WHTAmount]
GO
ALTER TABLE [dbo].[WN_FeeReceipts] ADD  CONSTRAINT [DF_WN_FeeReceipts_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_FeeReceipts] ADD  CONSTRAINT [DF_WN_FeeReceipts_CreatedOn]  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
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
ALTER TABLE [dbo].[WN_GWN_ActionLog] ADD  CONSTRAINT [DF_WN_GWN_ActionLog_Ok]  DEFAULT ((0)) FOR [Ok]
GO
ALTER TABLE [dbo].[WN_GWN_ActionLog] ADD  CONSTRAINT [DF_WN_GWN_ActionLog_CreatedAt]  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_GWN_ClientAliases] ADD  CONSTRAINT [DF_WN_GWN_ClientAliases_UpdatedAt]  DEFAULT (sysutcdatetime()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[WN_GWN_NetworkLocations] ADD  CONSTRAINT [DF_WN_GWN_NetworkLocations_UpdatedAt]  DEFAULT (sysutcdatetime()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[WN_HIK_AccessGrants] ADD  CONSTRAINT [DF_WN_HIK_grants_state]  DEFAULT ('pending') FOR [sync_state]
GO
ALTER TABLE [dbo].[WN_HIK_BookingAccessOverrides] ADD  DEFAULT (sysdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_HIK_BookingAccessSuspensions] ADD  DEFAULT (sysdatetime()) FOR [SuspendedAt]
GO
ALTER TABLE [dbo].[WN_HIK_BookingAccessSuspensions] ADD  DEFAULT ((0)) FOR [MachinesBlocked]
GO
ALTER TABLE [dbo].[WN_HIK_Cards] ADD  CONSTRAINT [DF_WN_HIK_Cards_ad]  DEFAULT ((0)) FOR [Auto_delete]
GO
ALTER TABLE [dbo].[WN_HIK_Cards] ADD  CONSTRAINT [DF_WN_HIK_Cards_ca]  DEFAULT (sysdatetime()) FOR [Created_at]
GO
ALTER TABLE [dbo].[WN_HIK_Cards] ADD  CONSTRAINT [DF_WN_HIK_Cards_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_HIK_DashboardUsers] ADD  CONSTRAINT [DF_WN_HIK_DashboardUsers_created]  DEFAULT (sysdatetime()) FOR [Created_at]
GO
ALTER TABLE [dbo].[WN_HIK_DashboardUsers] ADD  CONSTRAINT [DF_WN_HIK_DashboardUsers_role]  DEFAULT ('user') FOR [Role]
GO
ALTER TABLE [dbo].[WN_HIK_DashboardUsers] ADD  CONSTRAINT [DF_WN_HIK_DashUsers_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_HIK_DevCache] ADD  CONSTRAINT [DF_WN_HIK_DevCache_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_HIK_Devices] ADD  CONSTRAINT [DF_WN_HIK_Devices_port]  DEFAULT ((80)) FOR [Port]
GO
ALTER TABLE [dbo].[WN_HIK_Devices] ADD  CONSTRAINT [DF_WN_HIK_Devices_https]  DEFAULT ((0)) FOR [Use_https]
GO
ALTER TABLE [dbo].[WN_HIK_Devices] ADD  CONSTRAINT [DF_WN_HIK_Devices_online]  DEFAULT ((0)) FOR [Online]
GO
ALTER TABLE [dbo].[WN_HIK_Devices] ADD  CONSTRAINT [DF_WN_HIK_Devices_created]  DEFAULT (sysdatetime()) FOR [Created_at]
GO
ALTER TABLE [dbo].[WN_HIK_EventCategories] ADD  CONSTRAINT [DF_WN_HIK_EvCat_denied]  DEFAULT ((0)) FOR [Is_denied]
GO
ALTER TABLE [dbo].[WN_HIK_EventCategories] ADD  CONSTRAINT [DF_WN_HIK_EvCat_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_HIK_Events] ADD  CONSTRAINT [DF_WN_HIK_Events_created]  DEFAULT (sysdatetime()) FOR [created_at]
GO
ALTER TABLE [dbo].[WN_HIK_FaceVault] ADD  CONSTRAINT [DF_WN_HIK_FaceVault_upd]  DEFAULT (sysdatetime()) FOR [updated_at]
GO
ALTER TABLE [dbo].[WN_HIK_FpVault] ADD  CONSTRAINT [DF_WN_HIK_FpVault_no]  DEFAULT ((1)) FOR [finger_no]
GO
ALTER TABLE [dbo].[WN_HIK_FpVault] ADD  CONSTRAINT [DF_WN_HIK_FpVault_upd]  DEFAULT (sysdatetime()) FOR [updated_at]
GO
ALTER TABLE [dbo].[WN_HIK_Groups] ADD  CONSTRAINT [DF_WN_HIK_Groups_ca]  DEFAULT (sysdatetime()) FOR [Created_at]
GO
ALTER TABLE [dbo].[WN_HIK_PendingOps] ADD  CONSTRAINT [DF_WN_HIK_PendingOps_att]  DEFAULT ((0)) FOR [attempts]
GO
ALTER TABLE [dbo].[WN_HIK_PendingOps] ADD  CONSTRAINT [DF_WN_HIK_PendingOps_created]  DEFAULT (sysdatetime()) FOR [created_at]
GO
ALTER TABLE [dbo].[WN_HIK_PersonMap] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_HIK_SyncLog] ADD  CONSTRAINT [DF_WN_HIK_log_ts]  DEFAULT (sysdatetime()) FOR [Timestamp]
GO
ALTER TABLE [dbo].[WN_HIK_Tags] ADD  CONSTRAINT [DF_WN_HIK_Tags_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_HIK_Tags] ADD  CONSTRAINT [DF_WN_HIK_Tags_ca]  DEFAULT (sysdatetime()) FOR [Created_at]
GO
ALTER TABLE [dbo].[WN_HIK_Users] ADD  CONSTRAINT [DF_WN_HIK_Users_role]  DEFAULT ('user') FOR [role]
GO
ALTER TABLE [dbo].[WN_HIK_Users] ADD  CONSTRAINT [DF_WN_HIK_Users_mc]  DEFAULT ((0)) FOR [machine_count]
GO
ALTER TABLE [dbo].[WN_HIK_Users] ADD  CONSTRAINT [DF_WN_HIK_Users_upd]  DEFAULT (sysdatetime()) FOR [updated_at]
GO
ALTER TABLE [dbo].[WN_HIK_Visitors] ADD  CONSTRAINT [DF_WN_HIK_Visitors_ad]  DEFAULT ((0)) FOR [Auto_delete]
GO
ALTER TABLE [dbo].[WN_HIK_Visitors] ADD  CONSTRAINT [DF_WN_HIK_Visitors_st]  DEFAULT ('active') FOR [Status]
GO
ALTER TABLE [dbo].[WN_HIK_Visitors] ADD  CONSTRAINT [DF_WN_HIK_Visitors_ca]  DEFAULT (sysdatetime()) FOR [Created_at]
GO
ALTER TABLE [dbo].[WN_IntegrationLog] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_IntegrationLog] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_InvoiceDeliveryQueue] ADD  DEFAULT ((0)) FOR [Attempts]
GO
ALTER TABLE [dbo].[WN_InvoiceDeliveryQueue] ADD  DEFAULT ('Pending') FOR [Status]
GO
ALTER TABLE [dbo].[WN_InvoiceDeliveryQueue] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_InvoiceLines] ADD  DEFAULT ((1)) FOR [Quantity]
GO
ALTER TABLE [dbo].[WN_InvoiceLines] ADD  DEFAULT ((0)) FOR [DiscountAmount]
GO
ALTER TABLE [dbo].[WN_InvoiceLines] ADD  DEFAULT ((0)) FOR [TaxRate]
GO
ALTER TABLE [dbo].[WN_InvoiceLines] ADD  DEFAULT ((0)) FOR [SortOrder]
GO
ALTER TABLE [dbo].[WN_InvoicePrepaidPeriods] ADD  DEFAULT ('Pending') FOR [Status]
GO
ALTER TABLE [dbo].[WN_InvoicePrepaidPeriods] ADD  DEFAULT (getdate()) FOR [CreatedAt]
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
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT ((1)) FOR [InvoiceTypeId]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  DEFAULT ((0.00)) FOR [SecurityDepositAmount]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  CONSTRAINT [DF_WN_Invoices_RoomRentExclTax]  DEFAULT ((0.0000)) FOR [RoomRentExclTax]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  CONSTRAINT [DF_WN_Invoices_ServiceCharges]  DEFAULT ((0.0000)) FOR [ServiceCharges]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  CONSTRAINT [DF_WN_Invoices_TaxOnServiceCharges]  DEFAULT ((0.0000)) FOR [TaxOnServiceCharges]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  CONSTRAINT [DF_WN_Invoices_WHTRate]  DEFAULT ((0.0000)) FOR [WHTRate]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  CONSTRAINT [DF_WN_Invoices_WHTAmount]  DEFAULT ((0.0000)) FOR [WHTAmount]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  CONSTRAINT [DF_WN_Invoices_IsCustom]  DEFAULT ((0)) FOR [IsCustom]
GO
ALTER TABLE [dbo].[WN_Invoices] ADD  CONSTRAINT [DF_WN_Invoices_OneBillAttempts]  DEFAULT ((0)) FOR [OneBillAttempts]
GO
ALTER TABLE [dbo].[WN_KYCDocumentTypes] ADD  DEFAULT ((0)) FOR [IndividualRequirement]
GO
ALTER TABLE [dbo].[WN_KYCDocumentTypes] ADD  DEFAULT ((0)) FOR [BusinessRequirement]
GO
ALTER TABLE [dbo].[WN_KYCDocumentTypes] ADD  DEFAULT ((0)) FOR [AllowMultiple]
GO
ALTER TABLE [dbo].[WN_KYCDocumentTypes] ADD  DEFAULT ((0)) FOR [RequiresHolderName]
GO
ALTER TABLE [dbo].[WN_KYCDocumentTypes] ADD  DEFAULT ((0)) FOR [RequiresExpiry]
GO
ALTER TABLE [dbo].[WN_KYCDocumentTypes] ADD  DEFAULT ((0)) FOR [SortOrder]
GO
ALTER TABLE [dbo].[WN_KYCDocumentTypes] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_LeaseTemplates] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_LeaseTemplates] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_Locations] ADD  CONSTRAINT [DF_WN_Locations_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Locations] ADD  CONSTRAINT [DF_WN_Locations_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Locations] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_LoginHistory] ADD  CONSTRAINT [DF_WN_LoginHistory_EventAt]  DEFAULT (sysutcdatetime()) FOR [EventAt]
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs] ADD  DEFAULT (newid()) FOR [Guid]
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs] ADD  DEFAULT ((360)) FOR [AllocatedMinutes]
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs] ADD  DEFAULT ('Confirmed') FOR [Status]
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedDate]
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
ALTER TABLE [dbo].[WN_MessageTemplates] ADD  DEFAULT (sysutcdatetime()) FOR [UpdatedOn]
GO
ALTER TABLE [dbo].[WN_MobileAccessVerifications] ADD  CONSTRAINT [DF_WN_MobileAccessVerifications_UnlockCount]  DEFAULT ((0)) FOR [UnlockCount]
GO
ALTER TABLE [dbo].[WN_NAV_BadgeReads] ADD  DEFAULT (sysutcdatetime()) FOR [ReadAt]
GO
ALTER TABLE [dbo].[WN_NetworkAlertLog] ADD  CONSTRAINT [DF_WN_NetworkAlertLog_SentAt]  DEFAULT (sysutcdatetime()) FOR [SentAt]
GO
ALTER TABLE [dbo].[WN_NetworkAlertRecipients] ADD  CONSTRAINT [DF_WN_NetworkAlertRecipients_IsActive]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_NetworkAlertRecipients] ADD  CONSTRAINT [DF_WN_NetworkAlertRecipients_CreatedOn]  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_NetworkAlertState] ADD  CONSTRAINT [DF_WN_NetworkAlertState_UpdatedAt]  DEFAULT (sysutcdatetime()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[WN_OfferingType] ADD  CONSTRAINT [DF_WN_OfferingType_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_OneBill_Payments] ADD  CONSTRAINT [DF_WN_OneBill_Payments_ReceivedOn]  DEFAULT (sysutcdatetime()) FOR [ReceivedOn]
GO
ALTER TABLE [dbo].[WN_PaymentMethods] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Payments] ADD  DEFAULT ((1)) FOR [PaymentMethodId]
GO
ALTER TABLE [dbo].[WN_Payments] ADD  DEFAULT (N'PKR') FOR [CurrencyCode]
GO
ALTER TABLE [dbo].[WN_Payments] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_Payments] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Persons] ADD  DEFAULT (newid()) FOR [PersonGuid]
GO
ALTER TABLE [dbo].[WN_Persons] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
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
ALTER TABLE [dbo].[WN_ProvinceChargeTypeTax] ADD  DEFAULT ((1)) FOR [TaxApplicable]
GO
ALTER TABLE [dbo].[WN_ProvinceChargeTypeTax] ADD  DEFAULT (getutcdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_ProvincesTemp] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_QuotationActivities] ADD  DEFAULT (newid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_QuotationActivities] ADD  DEFAULT ((1)) FOR [Version]
GO
ALTER TABLE [dbo].[WN_QuotationActivities] ADD  DEFAULT (getutcdate()) FOR [CreatedDate]
GO
ALTER TABLE [dbo].[WN_QuotationDetails] ADD  DEFAULT (newid()) FOR [Guid]
GO
ALTER TABLE [dbo].[WN_QuotationDetails] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedDate]
GO
ALTER TABLE [dbo].[WN_QuotationResponses] ADD  DEFAULT (newid()) FOR [IdGUID]
GO
ALTER TABLE [dbo].[WN_QuotationResponses] ADD  DEFAULT ((1)) FOR [Version]
GO
ALTER TABLE [dbo].[WN_QuotationResponses] ADD  DEFAULT (getutcdate()) FOR [RespondedDate]
GO
ALTER TABLE [dbo].[WN_QuotationResponses] ADD  DEFAULT (getutcdate()) FOR [CreatedDate]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotati__Guid__5BC90619]  DEFAULT (newid()) FOR [Guid]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Disco__5CBD2A52]  DEFAULT ((0.00)) FOR [DiscountPercentage]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Disco__5DB14E8B]  DEFAULT ((0.00)) FOR [DiscountAmount]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Statu__5EA572C4]  DEFAULT ('Active') FOR [Status]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Versi__5F9996FD]  DEFAULT ((1)) FOR [Version]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__IsAct__608DBB36]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Creat__6181DF6F]  DEFAULT (sysutcdatetime()) FOR [CreatedDate]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Disco__79596900]  DEFAULT ('Percentage') FOR [DiscountType]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Secur__7A4D8D39]  DEFAULT ((0)) FOR [SecurityDeposit]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Advan__0A83F502]  DEFAULT ((0)) FOR [AdvanceRentMonths]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Secur__0B78193B]  DEFAULT ((0)) FOR [SecurityDepositMonths]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Initi__0C6C3D74]  DEFAULT ((0.00)) FOR [InitialAmountPayable]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Remai__0D6061AD]  DEFAULT ((0.00)) FOR [RemainingContractRent]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF__WN_Quotat__Billi__262C0F77]  DEFAULT ((3)) FOR [BillingPeriodMonths]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF_WN_Quotations_OfferingType]  DEFAULT ('24/7') FOR [OfferingType]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF_WN_Quotations_PerSeatSupportRate]  DEFAULT ((2000.00)) FOR [PerSeatSupportRate]
GO
ALTER TABLE [dbo].[WN_Quotations] ADD  CONSTRAINT [DF_WN_Quotations_SendWhtInvoice]  DEFAULT ((0)) FOR [SendWhtInvoice]
GO
ALTER TABLE [dbo].[WN_Refunds] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_Refunds] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_Refunds] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_Roles] ADD  CONSTRAINT [DF_WN_Roles_IsActive]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Roles] ADD  CONSTRAINT [DF_WN_Roles_CreatedAt]  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] ADD  DEFAULT (newsequentialid()) FOR [PublicId]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] ADD  DEFAULT ((1)) FOR [StatusId]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] ADD  DEFAULT (sysutcdatetime()) FOR [HeldOn]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] ADD  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_ServiceSwitches] ADD  CONSTRAINT [DF_WN_ServiceSwitches_IsEnabled]  DEFAULT ((1)) FOR [IsEnabled]
GO
ALTER TABLE [dbo].[WN_ServiceSwitches] ADD  CONSTRAINT [DF_WN_ServiceSwitches_UpdatedAt]  DEFAULT (sysutcdatetime()) FOR [UpdatedAt]
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
ALTER TABLE [dbo].[WN_SpaceConfig] ADD  DEFAULT ((0)) FOR [PricePerSeat]
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
ALTER TABLE [dbo].[WN_Spaces] ADD  CONSTRAINT [DF__WN_Spaces__Capac__15A0A1F3]  DEFAULT ((1)) FOR [Capacity]
GO
ALTER TABLE [dbo].[WN_Spaces] ADD  CONSTRAINT [DF__WN_Spaces__IsAct__14AC7DBA]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Spaces] ADD  CONSTRAINT [DF_WN_Spaces_Capacity1]  DEFAULT ((1)) FOR [OptimumCapacity]
GO
ALTER TABLE [dbo].[WN_Spaces] ADD  CONSTRAINT [DF__WN_Spaces__MaxDi__58D78AD5]  DEFAULT ((10.00)) FOR [MaxDiscountPercent]
GO
ALTER TABLE [dbo].[WN_SpaceTypes] ADD  CONSTRAINT [DF_WN_SpaceTypes_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_SpaceTypes] ADD  CONSTRAINT [DF_WN_SpaceTypes_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_SpaceTypes] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_Tax] ADD  DEFAULT (getutcdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_UNIFI_ClientAliases] ADD  DEFAULT (sysdatetime()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[WN_UNIFI_History] ADD  DEFAULT ((0)) FOR [Wifi]
GO
ALTER TABLE [dbo].[WN_UNIFI_History] ADD  DEFAULT ((0)) FOR [Wired]
GO
ALTER TABLE [dbo].[WN_UNIFI_History] ADD  DEFAULT ((0)) FOR [Guest]
GO
ALTER TABLE [dbo].[WN_UNIFI_History] ADD  DEFAULT ((0)) FOR [Online]
GO
ALTER TABLE [dbo].[WN_UNIFI_History] ADD  DEFAULT ((0)) FOR [Offline]
GO
ALTER TABLE [dbo].[WN_UNIFI_MacFilterLog] ADD  CONSTRAINT [DF_WN_UNIFI_MacFilterLog_LoggedAt]  DEFAULT (sysutcdatetime()) FOR [LoggedAt]
GO
ALTER TABLE [dbo].[WN_UNIFI_Settings] ADD  CONSTRAINT [DF_WN_UNIFI_Settings_UpdatedAt]  DEFAULT (sysutcdatetime()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[WN_UNIFI_SiteLocations] ADD  CONSTRAINT [DF_WN_UNIFI_SiteLocations_UpdatedAt]  DEFAULT (sysutcdatetime()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[WN_UNIFI_SsidDevices] ADD  CONSTRAINT [DF_WN_UNIFI_SsidDevices_UpdatedAt]  DEFAULT (sysutcdatetime()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[WN_UserLocations] ADD  CONSTRAINT [DF_WN_UserLocations_CreatedOn]  DEFAULT (sysutcdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_UserPermissions] ADD  CONSTRAINT [DF_WN_UserPermissions_GrantedAt]  DEFAULT (sysutcdatetime()) FOR [GrantedAt]
GO
ALTER TABLE [dbo].[WN_Users] ADD  DEFAULT ((484)) FOR [CompanyId]
GO
ALTER TABLE [dbo].[WN_Users] ADD  CONSTRAINT [DF_WN_Users_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_Users] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[WN_WhatsApp_BotState] ADD  CONSTRAINT [DF_WN_WhatsApp_BotState_UpdatedOn]  DEFAULT (sysdatetime()) FOR [UpdatedOn]
GO
ALTER TABLE [dbo].[WN_WhatsApp_Contacts] ADD  CONSTRAINT [DF_WN_WhatsApp_Contacts_CreatedOn]  DEFAULT (sysdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_WhatsApp_Conversations] ADD  CONSTRAINT [DF_WN_WhatsApp_Conversations_Status]  DEFAULT (N'open') FOR [Status]
GO
ALTER TABLE [dbo].[WN_WhatsApp_Conversations] ADD  CONSTRAINT [DF_WN_WhatsApp_Conversations_Unread]  DEFAULT ((0)) FOR [UnreadCount]
GO
ALTER TABLE [dbo].[WN_WhatsApp_Conversations] ADD  CONSTRAINT [DF_WN_WhatsApp_Conversations_CreatedOn]  DEFAULT (sysdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_WhatsApp_Messages] ADD  CONSTRAINT [DF_WN_WhatsApp_Messages_Type]  DEFAULT (N'text') FOR [MessageType]
GO
ALTER TABLE [dbo].[WN_WhatsApp_Messages] ADD  CONSTRAINT [DF_WN_WhatsApp_Messages_CreatedOn]  DEFAULT (sysdatetime()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_WHTaxRate] ADD  CONSTRAINT [DF_WN_WHTaxRate_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[WN_WHTaxRate] ADD  CONSTRAINT [DF_WN_WHTaxRate_CreatedTime]  DEFAULT (getdate()) FOR [CreatedOn]
GO
ALTER TABLE [dbo].[WN_AccessCards]  WITH CHECK ADD  CONSTRAINT [FK_WN_AccessCards_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessCards] CHECK CONSTRAINT [FK_WN_AccessCards_Booking]
GO
ALTER TABLE [dbo].[WN_AccessCards]  WITH CHECK ADD  CONSTRAINT [FK_WN_AccessCards_CreatedBy] FOREIGN KEY([CreatedById])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessCards] CHECK CONSTRAINT [FK_WN_AccessCards_CreatedBy]
GO
ALTER TABLE [dbo].[WN_AccessCards]  WITH CHECK ADD  CONSTRAINT [FK_WN_AccessCards_Customer] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[WN_Customers] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessCards] CHECK CONSTRAINT [FK_WN_AccessCards_Customer]
GO
ALTER TABLE [dbo].[WN_AccessCards]  WITH CHECK ADD  CONSTRAINT [FK_WN_AccessCards_Location] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessCards] CHECK CONSTRAINT [FK_WN_AccessCards_Location]
GO
ALTER TABLE [dbo].[WN_AccessCards]  WITH CHECK ADD  CONSTRAINT [FK_WN_AccessCards_Space] FOREIGN KEY([SpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessCards] CHECK CONSTRAINT [FK_WN_AccessCards_Space]
GO
ALTER TABLE [dbo].[WN_AccessCards]  WITH CHECK ADD  CONSTRAINT [FK_WN_AccessCards_UpdatedBy] FOREIGN KEY([UpdatedById])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessCards] CHECK CONSTRAINT [FK_WN_AccessCards_UpdatedBy]
GO
ALTER TABLE [dbo].[WN_AccessStatus]  WITH CHECK ADD FOREIGN KEY([BookingDetailId])
REFERENCES [dbo].[WN_BookingDetails] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessStatus]  WITH CHECK ADD FOREIGN KEY([BookingDetailId])
REFERENCES [dbo].[WN_BookingDetails] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessStatus]  WITH CHECK ADD  CONSTRAINT [FK__WN_Access__Custo__364D68A7] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[WN_Customers] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessStatus] CHECK CONSTRAINT [FK__WN_Access__Custo__364D68A7]
GO
ALTER TABLE [dbo].[WN_AccessStatus]  WITH CHECK ADD  CONSTRAINT [FK__WN_Access__Custo__462ECAB5] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[WN_Customers] ([Id])
GO
ALTER TABLE [dbo].[WN_AccessStatus] CHECK CONSTRAINT [FK__WN_Access__Custo__462ECAB5]
GO
ALTER TABLE [dbo].[WN_AccessStatus]  WITH CHECK ADD FOREIGN KEY([PersonId])
REFERENCES [dbo].[WN_Persons] ([PersonId])
GO
ALTER TABLE [dbo].[WN_AccessStatus]  WITH CHECK ADD FOREIGN KEY([PersonId])
REFERENCES [dbo].[WN_Persons] ([PersonId])
GO
ALTER TABLE [dbo].[WN_Agreements]  WITH CHECK ADD  CONSTRAINT [FK_WN_Agreements_LeaseTemplate] FOREIGN KEY([TemplateVersionId])
REFERENCES [dbo].[WN_LeaseTemplates] ([Id])
GO
ALTER TABLE [dbo].[WN_Agreements] CHECK CONSTRAINT [FK_WN_Agreements_LeaseTemplate]
GO
ALTER TABLE [dbo].[WN_BookingAttendants]  WITH CHECK ADD FOREIGN KEY([BookingDetailId])
REFERENCES [dbo].[WN_BookingDetails] ([Id])
GO
ALTER TABLE [dbo].[WN_BookingAttendants]  WITH CHECK ADD FOREIGN KEY([BookingDetailId])
REFERENCES [dbo].[WN_BookingDetails] ([Id])
GO
ALTER TABLE [dbo].[WN_BookingAttendants]  WITH CHECK ADD  CONSTRAINT [FK__WN_Bookin__Custo__3929D552] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[WN_Customers] ([Id])
GO
ALTER TABLE [dbo].[WN_BookingAttendants] CHECK CONSTRAINT [FK__WN_Bookin__Custo__3929D552]
GO
ALTER TABLE [dbo].[WN_BookingAttendants]  WITH CHECK ADD  CONSTRAINT [FK__WN_Bookin__Custo__3F81CD26] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[WN_Customers] ([Id])
GO
ALTER TABLE [dbo].[WN_BookingAttendants] CHECK CONSTRAINT [FK__WN_Bookin__Custo__3F81CD26]
GO
ALTER TABLE [dbo].[WN_BookingAttendants]  WITH CHECK ADD FOREIGN KEY([PersonId])
REFERENCES [dbo].[WN_Persons] ([PersonId])
GO
ALTER TABLE [dbo].[WN_BookingAttendants]  WITH CHECK ADD FOREIGN KEY([PersonId])
REFERENCES [dbo].[WN_Persons] ([PersonId])
GO
ALTER TABLE [dbo].[WN_BookingDetails]  WITH CHECK ADD  CONSTRAINT [FK_WN_BookingDetails_SupportCharges] FOREIGN KEY([SupportChargesId])
REFERENCES [dbo].[WN_ChargeTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_BookingDetails] CHECK CONSTRAINT [FK_WN_BookingDetails_SupportCharges]
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
ALTER TABLE [dbo].[WN_BookingMeetingRoomEntitlements]  WITH CHECK ADD  CONSTRAINT [FK_Entitlement_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_BookingMeetingRoomEntitlements] CHECK CONSTRAINT [FK_Entitlement_Booking]
GO
ALTER TABLE [dbo].[WN_BookingMeetingRoomEntitlements]  WITH CHECK ADD  CONSTRAINT [FK_Entitlement_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_BookingMeetingRoomEntitlements] CHECK CONSTRAINT [FK_Entitlement_User]
GO
ALTER TABLE [dbo].[WN_ChallanAuditLog]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_ChallanAuditLog_Challan] FOREIGN KEY([ChallanId])
REFERENCES [dbo].[WN_Challans] ([Id])
GO
ALTER TABLE [dbo].[WN_ChallanAuditLog] NOCHECK CONSTRAINT [FK_WN_ChallanAuditLog_Challan]
GO
ALTER TABLE [dbo].[WN_ChallanAuditLog]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_ChallanAuditLog_User] FOREIGN KEY([UpdatedById])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_ChallanAuditLog] NOCHECK CONSTRAINT [FK_WN_ChallanAuditLog_User]
GO
ALTER TABLE [dbo].[WN_Challans]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_Challans_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_Challans] NOCHECK CONSTRAINT [FK_WN_Challans_Booking]
GO
ALTER TABLE [dbo].[WN_ChargeTypeRate]  WITH CHECK ADD  CONSTRAINT [FK_WN_ChargeTypeRate_WN_ChargeTypes] FOREIGN KEY([ChargeTypeId])
REFERENCES [dbo].[WN_ChargeTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_ChargeTypeRate] CHECK CONSTRAINT [FK_WN_ChargeTypeRate_WN_ChargeTypes]
GO
ALTER TABLE [dbo].[WN_Contacts]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_Contacts_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_Contacts] NOCHECK CONSTRAINT [FK_WN_Contacts_User]
GO
ALTER TABLE [dbo].[WN_CustomerAttendants]  WITH CHECK ADD  CONSTRAINT [FK__WN_Custom__Custo__38D4CF97] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[WN_Customers] ([Id])
GO
ALTER TABLE [dbo].[WN_CustomerAttendants] CHECK CONSTRAINT [FK__WN_Custom__Custo__38D4CF97]
GO
ALTER TABLE [dbo].[WN_CustomerAttendants]  WITH CHECK ADD  CONSTRAINT [FK__WN_Custom__Custo__3B121DC4] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[WN_Customers] ([Id])
GO
ALTER TABLE [dbo].[WN_CustomerAttendants] CHECK CONSTRAINT [FK__WN_Custom__Custo__3B121DC4]
GO
ALTER TABLE [dbo].[WN_CustomerAttendants]  WITH CHECK ADD FOREIGN KEY([PersonId])
REFERENCES [dbo].[WN_Persons] ([PersonId])
GO
ALTER TABLE [dbo].[WN_CustomerAttendants]  WITH CHECK ADD FOREIGN KEY([PersonId])
REFERENCES [dbo].[WN_Persons] ([PersonId])
GO
ALTER TABLE [dbo].[WN_Customers]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_Customers_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_Customers] NOCHECK CONSTRAINT [FK_WN_Customers_User]
GO
ALTER TABLE [dbo].[WN_CustomInvoices]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_CustomInvoices_Spaces] FOREIGN KEY([SpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
GO
ALTER TABLE [dbo].[WN_CustomInvoices] CHECK CONSTRAINT [FK_WN_CustomInvoices_Spaces]
GO
ALTER TABLE [dbo].[WN_Floors]  WITH CHECK ADD  CONSTRAINT [FK_WN_Floors_Location] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_Floors] CHECK CONSTRAINT [FK_WN_Floors_Location]
GO
ALTER TABLE [dbo].[WN_HIK_AccessGrants]  WITH CHECK ADD  CONSTRAINT [FK_WN_HIK_grants_device] FOREIGN KEY([device_id])
REFERENCES [dbo].[WN_HIK_Devices] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_HIK_AccessGrants] CHECK CONSTRAINT [FK_WN_HIK_grants_device]
GO
ALTER TABLE [dbo].[WN_HIK_Devices]  WITH CHECK ADD  CONSTRAINT [FK_WN_HIK_Devices_Group] FOREIGN KEY([Group_id])
REFERENCES [dbo].[WN_HIK_Groups] ([Id])
GO
ALTER TABLE [dbo].[WN_HIK_Devices] CHECK CONSTRAINT [FK_WN_HIK_Devices_Group]
GO
ALTER TABLE [dbo].[WN_HIK_Devices]  WITH CHECK ADD  CONSTRAINT [FK_WN_HIK_Devices_WN_Locations] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_HIK_Devices] CHECK CONSTRAINT [FK_WN_HIK_Devices_WN_Locations]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_InvoiceLines_ChargeType] FOREIGN KEY([ChargeTypeId])
REFERENCES [dbo].[WN_ChargeTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_InvoiceLines] NOCHECK CONSTRAINT [FK_WN_InvoiceLines_ChargeType]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_InvoiceLines_Invoice] FOREIGN KEY([InvoiceId])
REFERENCES [dbo].[WN_Invoices] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_InvoiceLines] NOCHECK CONSTRAINT [FK_WN_InvoiceLines_Invoice]
GO
ALTER TABLE [dbo].[WN_InvoicePrepaidPeriods]  WITH CHECK ADD  CONSTRAINT [FK_WN_InvoicePrepaidPeriods_WN_Bookings] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_InvoicePrepaidPeriods] CHECK CONSTRAINT [FK_WN_InvoicePrepaidPeriods_WN_Bookings]
GO
ALTER TABLE [dbo].[WN_InvoicePrepaidPeriods]  WITH CHECK ADD  CONSTRAINT [FK_WN_InvoicePrepaidPeriods_WN_Invoices] FOREIGN KEY([InvoiceId])
REFERENCES [dbo].[WN_Invoices] ([Id])
GO
ALTER TABLE [dbo].[WN_InvoicePrepaidPeriods] CHECK CONSTRAINT [FK_WN_InvoicePrepaidPeriods_WN_Invoices]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_Invoices_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_Invoices] NOCHECK CONSTRAINT [FK_WN_Invoices_Booking]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_Invoices_Locations] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_Invoices] CHECK CONSTRAINT [FK_WN_Invoices_Locations]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_Invoices_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_Invoices] NOCHECK CONSTRAINT [FK_WN_Invoices_User]
GO
ALTER TABLE [dbo].[WN_LeaseTemplates]  WITH CHECK ADD  CONSTRAINT [FK_WN_LeaseTemplates_CreatedBy] FOREIGN KEY([CreatedBy])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_LeaseTemplates] CHECK CONSTRAINT [FK_WN_LeaseTemplates_CreatedBy]
GO
ALTER TABLE [dbo].[WN_Locations]  WITH CHECK ADD  CONSTRAINT [FK_WN_Locations_Branch] FOREIGN KEY([BranchId])
REFERENCES [dbo].[Branches] ([Id])
GO
ALTER TABLE [dbo].[WN_Locations] CHECK CONSTRAINT [FK_WN_Locations_Branch]
GO
ALTER TABLE [dbo].[WN_Locations]  WITH CHECK ADD  CONSTRAINT [FK_WN_Locations_Provinces] FOREIGN KEY([ProvinceId])
REFERENCES [dbo].[WN_ProvincesTemp] ([Id])
GO
ALTER TABLE [dbo].[WN_Locations] CHECK CONSTRAINT [FK_WN_Locations_Provinces]
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs]  WITH CHECK ADD  CONSTRAINT [FK_UsageLog_Entitlement] FOREIGN KEY([EntitlementId])
REFERENCES [dbo].[WN_BookingMeetingRoomEntitlements] ([Id])
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs] CHECK CONSTRAINT [FK_UsageLog_Entitlement]
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs]  WITH CHECK ADD  CONSTRAINT [FK_UsageLog_MeetingBooking] FOREIGN KEY([MeetingRoomBookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs] CHECK CONSTRAINT [FK_UsageLog_MeetingBooking]
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs]  WITH CHECK ADD  CONSTRAINT [FK_UsageLog_PrivateBooking] FOREIGN KEY([PrivateSpaceBookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs] CHECK CONSTRAINT [FK_UsageLog_PrivateBooking]
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs]  WITH CHECK ADD  CONSTRAINT [FK_UsageLog_Space] FOREIGN KEY([MeetingRoomSpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs] CHECK CONSTRAINT [FK_UsageLog_Space]
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs]  WITH CHECK ADD  CONSTRAINT [FK_UsageLog_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_MeetingRoomUsageLogs] CHECK CONSTRAINT [FK_UsageLog_User]
GO
ALTER TABLE [dbo].[WN_MembershipPlanFeatures]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_MembershipPlanFeatures_Plan] FOREIGN KEY([PlanId])
REFERENCES [dbo].[WN_MembershipPlans] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_MembershipPlanFeatures] NOCHECK CONSTRAINT [FK_WN_MembershipPlanFeatures_Plan]
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
ALTER TABLE [dbo].[WN_PaymentLines]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_PaymentLines_ChargeType] FOREIGN KEY([ChargeTypeId])
REFERENCES [dbo].[WN_ChargeTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_PaymentLines] NOCHECK CONSTRAINT [FK_WN_PaymentLines_ChargeType]
GO
ALTER TABLE [dbo].[WN_PaymentLines]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_PaymentLines_Payment] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[WN_Payments] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_PaymentLines] NOCHECK CONSTRAINT [FK_WN_PaymentLines_Payment]
GO
ALTER TABLE [dbo].[WN_Payments]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_Payments_Memberships_MembershipId] FOREIGN KEY([MembershipId])
REFERENCES [dbo].[WN_MemberShips] ([Id])
GO
ALTER TABLE [dbo].[WN_Payments] NOCHECK CONSTRAINT [FK_WN_Payments_Memberships_MembershipId]
GO
ALTER TABLE [dbo].[WN_PlanFeatures]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_PlanFeatures_PricingPlans_PlanId] FOREIGN KEY([PlanId])
REFERENCES [dbo].[WN_PricingPlans] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_PlanFeatures] NOCHECK CONSTRAINT [FK_WN_PlanFeatures_PricingPlans_PlanId]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_PricingRules_BillingPeriod] FOREIGN KEY([BillingPeriodId])
REFERENCES [dbo].[WN_BillingPeriods] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] NOCHECK CONSTRAINT [FK_WN_PricingRules_BillingPeriod]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_PricingRules_Company] FOREIGN KEY([CompanyId])
REFERENCES [dbo].[Company] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] NOCHECK CONSTRAINT [FK_WN_PricingRules_Company]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_PricingRules_Location] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] NOCHECK CONSTRAINT [FK_WN_PricingRules_Location]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_PricingRules_Space] FOREIGN KEY([SpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] NOCHECK CONSTRAINT [FK_WN_PricingRules_Space]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_PricingRules_SpaceType] FOREIGN KEY([SpaceTypeId])
REFERENCES [dbo].[WN_SpaceTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_PricingRules] NOCHECK CONSTRAINT [FK_WN_PricingRules_SpaceType]
GO
ALTER TABLE [dbo].[WN_ProvinceChargeTypeTax]  WITH CHECK ADD  CONSTRAINT [FK_WN_ProvinceChargeTypeTax_ChargeTypes] FOREIGN KEY([ChargeTypeId])
REFERENCES [dbo].[WN_ChargeTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_ProvinceChargeTypeTax] CHECK CONSTRAINT [FK_WN_ProvinceChargeTypeTax_ChargeTypes]
GO
ALTER TABLE [dbo].[WN_ProvinceChargeTypeTax]  WITH CHECK ADD  CONSTRAINT [FK_WN_ProvinceChargeTypeTax_Provinces] FOREIGN KEY([ProvinceId])
REFERENCES [dbo].[WN_ProvincesTemp] ([Id])
GO
ALTER TABLE [dbo].[WN_ProvinceChargeTypeTax] CHECK CONSTRAINT [FK_WN_ProvinceChargeTypeTax_Provinces]
GO
ALTER TABLE [dbo].[WN_QuotationDetails]  WITH CHECK ADD  CONSTRAINT [FK_QuotationDetails_Quotation] FOREIGN KEY([QuotationId])
REFERENCES [dbo].[WN_Quotations] ([Id])
GO
ALTER TABLE [dbo].[WN_QuotationDetails] CHECK CONSTRAINT [FK_QuotationDetails_Quotation]
GO
ALTER TABLE [dbo].[WN_Quotations]  WITH CHECK ADD  CONSTRAINT [FK_Quotation_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_Quotations] CHECK CONSTRAINT [FK_Quotation_Booking]
GO
ALTER TABLE [dbo].[WN_Quotations]  WITH CHECK ADD  CONSTRAINT [FK_Quotation_Customer] FOREIGN KEY([CustomerId])
REFERENCES [dbo].[WN_Customers] ([Id])
GO
ALTER TABLE [dbo].[WN_Quotations] CHECK CONSTRAINT [FK_Quotation_Customer]
GO
ALTER TABLE [dbo].[WN_Quotations]  WITH CHECK ADD  CONSTRAINT [FK_Quotation_Space] FOREIGN KEY([SpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
GO
ALTER TABLE [dbo].[WN_Quotations] CHECK CONSTRAINT [FK_Quotation_Space]
GO
ALTER TABLE [dbo].[WN_RefreshTokens_Temp]  WITH CHECK ADD  CONSTRAINT [FK_WN_RefreshTokens_Users_UserId] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_RefreshTokens_Temp] CHECK CONSTRAINT [FK_WN_RefreshTokens_Users_UserId]
GO
ALTER TABLE [dbo].[WN_Refunds]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_Refunds_Payment] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[WN_Payments] ([Id])
GO
ALTER TABLE [dbo].[WN_Refunds] NOCHECK CONSTRAINT [FK_WN_Refunds_Payment]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_SecurityDeposits_Booking] FOREIGN KEY([BookingId])
REFERENCES [dbo].[WN_Bookings] ([Id])
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] NOCHECK CONSTRAINT [FK_WN_SecurityDeposits_Booking]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_SecurityDeposits_User] FOREIGN KEY([UserId])
REFERENCES [dbo].[WN_Users] ([Id])
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] NOCHECK CONSTRAINT [FK_WN_SecurityDeposits_User]
GO
ALTER TABLE [dbo].[WN_SpaceAmenities]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_SpaceAmenities_Amenity] FOREIGN KEY([AmenityId])
REFERENCES [dbo].[WN_Amenities] ([Id])
GO
ALTER TABLE [dbo].[WN_SpaceAmenities] NOCHECK CONSTRAINT [FK_WN_SpaceAmenities_Amenity]
GO
ALTER TABLE [dbo].[WN_SpaceAmenities]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_SpaceAmenities_Space] FOREIGN KEY([SpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[WN_SpaceAmenities] NOCHECK CONSTRAINT [FK_WN_SpaceAmenities_Space]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_SpaceInventoryConfig_Location] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] NOCHECK CONSTRAINT [FK_WN_SpaceInventoryConfig_Location]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_SpaceInventoryConfig_SpaceType] FOREIGN KEY([SpaceTypeId])
REFERENCES [dbo].[WN_SpaceTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] NOCHECK CONSTRAINT [FK_WN_SpaceInventoryConfig_SpaceType]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_SpacePricing_BillingPeriod] FOREIGN KEY([BillingPeriodId])
REFERENCES [dbo].[WN_BillingPeriods] ([Id])
GO
ALTER TABLE [dbo].[WN_SpacePricing] NOCHECK CONSTRAINT [FK_WN_SpacePricing_BillingPeriod]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_SpacePricing_Space] FOREIGN KEY([SpaceId])
REFERENCES [dbo].[WN_Spaces] ([Id])
GO
ALTER TABLE [dbo].[WN_SpacePricing] NOCHECK CONSTRAINT [FK_WN_SpacePricing_Space]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_SpacePricing_TierType] FOREIGN KEY([TierTypeId])
REFERENCES [dbo].[WN_PricingTierTypes] ([Id])
GO
ALTER TABLE [dbo].[WN_SpacePricing] NOCHECK CONSTRAINT [FK_WN_SpacePricing_TierType]
GO
ALTER TABLE [dbo].[WN_Spaces]  WITH CHECK ADD  CONSTRAINT [FK_WN_Spaces_BillingPeriods] FOREIGN KEY([BillingPeriodId])
REFERENCES [dbo].[WN_BillingPeriods] ([Id])
GO
ALTER TABLE [dbo].[WN_Spaces] CHECK CONSTRAINT [FK_WN_Spaces_BillingPeriods]
GO
ALTER TABLE [dbo].[WN_SpaceTypes]  WITH CHECK ADD  CONSTRAINT [FK_WN_SpaceTypes_Location] FOREIGN KEY([LocationId])
REFERENCES [dbo].[WN_Locations] ([Id])
GO
ALTER TABLE [dbo].[WN_SpaceTypes] CHECK CONSTRAINT [FK_WN_SpaceTypes_Location]
GO
ALTER TABLE [dbo].[WN_Tax]  WITH CHECK ADD  CONSTRAINT [FK_WN_Tax_Provinces] FOREIGN KEY([ProvinceId])
REFERENCES [dbo].[WN_ProvincesTemp] ([Id])
GO
ALTER TABLE [dbo].[WN_Tax] CHECK CONSTRAINT [FK_WN_Tax_Provinces]
GO
ALTER TABLE [dbo].[WN_Users]  WITH NOCHECK ADD  CONSTRAINT [FK_WN_Users_Company] FOREIGN KEY([CompanyId])
REFERENCES [dbo].[Company] ([Id])
GO
ALTER TABLE [dbo].[WN_Users] NOCHECK CONSTRAINT [FK_WN_Users_Company]
GO
ALTER TABLE [dbo].[WN_AccessCards]  WITH CHECK ADD  CONSTRAINT [CK_WN_AccessCards_DateRange] CHECK  (([EndDate]>=[StartDate]))
GO
ALTER TABLE [dbo].[WN_AccessCards] CHECK CONSTRAINT [CK_WN_AccessCards_DateRange]
GO
ALTER TABLE [dbo].[WN_Announcement]  WITH CHECK ADD CHECK  (([Status]='Failed' OR [Status]='Sent' OR [Status]='Sending' OR [Status]='Queued' OR [Status]='Draft'))
GO
ALTER TABLE [dbo].[WN_Announcement]  WITH CHECK ADD CHECK  (([TargetScope]='CustomList' OR [TargetScope]='Space' OR [TargetScope]='Location' OR [TargetScope]='All'))
GO
ALTER TABLE [dbo].[WN_Announcement]  WITH CHECK ADD CHECK  (([Type]='Alert' OR [Type]='Announcement'))
GO
ALTER TABLE [dbo].[WN_AnnouncementRecipient]  WITH CHECK ADD CHECK  (([Channel]='Email' OR [Channel]='Push'))
GO
ALTER TABLE [dbo].[WN_AnnouncementRecipient]  WITH CHECK ADD CHECK  (([Status]='Read' OR [Status]='Failed' OR [Status]='Sent' OR [Status]='Pending'))
GO
ALTER TABLE [dbo].[WN_AuthHandoff]  WITH CHECK ADD  CONSTRAINT [CK_WN_AuthHandoff_Target] CHECK  (([Target]='web' OR [Target]='cms'))
GO
ALTER TABLE [dbo].[WN_AuthHandoff] CHECK CONSTRAINT [CK_WN_AuthHandoff_Target]
GO
ALTER TABLE [dbo].[WN_BookingEmployeeRequests]  WITH CHECK ADD  CONSTRAINT [CK_WN_BookingEmployeeRequests_Status] CHECK  (([Status]=N'Removed' OR [Status]=N'Verified' OR [Status]=N'Pending'))
GO
ALTER TABLE [dbo].[WN_BookingEmployeeRequests] CHECK CONSTRAINT [CK_WN_BookingEmployeeRequests_Status]
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
ALTER TABLE [dbo].[WN_Bookings]  WITH CHECK ADD  CONSTRAINT [CK_WN_Bookings_Wht] CHECK  (([SendWhtInvoice]=(0) OR [WHTaxId]>(0) AND [WHTaxId]<(100)))
GO
ALTER TABLE [dbo].[WN_Bookings] CHECK CONSTRAINT [CK_WN_Bookings_Wht]
GO
ALTER TABLE [dbo].[WN_Challans]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_Challans_ValidUntil] CHECK  (([ValidUntil]>=CONVERT([date],sysutcdatetime())))
GO
ALTER TABLE [dbo].[WN_Challans] NOCHECK CONSTRAINT [CK_WN_Challans_ValidUntil]
GO
ALTER TABLE [dbo].[WN_ChargeTypeRate]  WITH CHECK ADD  CONSTRAINT [CK_WN_ChargeTypeRate_Dates] CHECK  (([EndDate] IS NULL OR [EndDate]>[StartDate]))
GO
ALTER TABLE [dbo].[WN_ChargeTypeRate] CHECK CONSTRAINT [CK_WN_ChargeTypeRate_Dates]
GO
ALTER TABLE [dbo].[WN_Complaints]  WITH CHECK ADD  CONSTRAINT [CK_WN_Complaints_Response] CHECK  (([ResolutionResponse] IS NULL OR ([ResolutionResponse]=N'persists' OR [ResolutionResponse]=N'resolved')))
GO
ALTER TABLE [dbo].[WN_Complaints] CHECK CONSTRAINT [CK_WN_Complaints_Response]
GO
ALTER TABLE [dbo].[WN_Complaints]  WITH CHECK ADD  CONSTRAINT [CK_WN_Complaints_Source] CHECK  (([Source]=N'manual' OR [Source]=N'whatsapp'))
GO
ALTER TABLE [dbo].[WN_Complaints] CHECK CONSTRAINT [CK_WN_Complaints_Source]
GO
ALTER TABLE [dbo].[WN_Complaints]  WITH CHECK ADD  CONSTRAINT [CK_WN_Complaints_Status] CHECK  (([Status]=N'closed' OR [Status]=N'resolved' OR [Status]=N'in_progress' OR [Status]=N'open'))
GO
ALTER TABLE [dbo].[WN_Complaints] CHECK CONSTRAINT [CK_WN_Complaints_Status]
GO
ALTER TABLE [dbo].[WN_ContactFeedback]  WITH CHECK ADD  CONSTRAINT [CK_WN_ContactFeedback_Outcome] CHECK  (([Outcome]=N'converted' OR [Outcome]=N'future_prospect' OR [Outcome]=N'not_interested'))
GO
ALTER TABLE [dbo].[WN_ContactFeedback] CHECK CONSTRAINT [CK_WN_ContactFeedback_Outcome]
GO
ALTER TABLE [dbo].[WN_Contacts]  WITH CHECK ADD  CONSTRAINT [CK_WN_Contacts_InquiryType] CHECK  (([InquiryType] IS NULL OR ([InquiryType]=N'Reference' OR [InquiryType]=N'WalkIn' OR [InquiryType]=N'WhatsApp' OR [InquiryType]=N'Website')))
GO
ALTER TABLE [dbo].[WN_Contacts] CHECK CONSTRAINT [CK_WN_Contacts_InquiryType]
GO
ALTER TABLE [dbo].[WN_Contacts]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_Contacts_Type] CHECK  (([ContactType]='BookTour' OR [ContactType]='Contact'))
GO
ALTER TABLE [dbo].[WN_Contacts] NOCHECK CONSTRAINT [CK_WN_Contacts_Type]
GO
ALTER TABLE [dbo].[WN_DeviceToken]  WITH CHECK ADD CHECK  (([Platform]='Android' OR [Platform]='iOS'))
GO
ALTER TABLE [dbo].[WN_EmailOutbox]  WITH CHECK ADD  CONSTRAINT [CK_WN_EmailOutbox_Status] CHECK  (([Status]=N'Discarded' OR [Status]=N'Failed' OR [Status]=N'Sent' OR [Status]=N'Sending' OR [Status]=N'Pending'))
GO
ALTER TABLE [dbo].[WN_EmailOutbox] CHECK CONSTRAINT [CK_WN_EmailOutbox_Status]
GO
ALTER TABLE [dbo].[WN_FeeReceipts]  WITH CHECK ADD  CONSTRAINT [CK_WN_FeeReceipts_DepositorCNIC] CHECK  (([DepositorCNIC] IS NULL OR [DepositorCNIC] like '[0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9][0-9]-[0-9]'))
GO
ALTER TABLE [dbo].[WN_FeeReceipts] CHECK CONSTRAINT [CK_WN_FeeReceipts_DepositorCNIC]
GO
ALTER TABLE [dbo].[WN_HIK_AccessGrants]  WITH CHECK ADD  CONSTRAINT [CK_WN_HIK_grants_state] CHECK  (([sync_state]='removing' OR [sync_state]='error' OR [sync_state]='synced' OR [sync_state]='pending'))
GO
ALTER TABLE [dbo].[WN_HIK_AccessGrants] CHECK CONSTRAINT [CK_WN_HIK_grants_state]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_InvoiceLines_Discount] CHECK  (([DiscountAmount]>=(0)))
GO
ALTER TABLE [dbo].[WN_InvoiceLines] NOCHECK CONSTRAINT [CK_WN_InvoiceLines_Discount]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_InvoiceLines_Quantity] CHECK  (([Quantity]>(0)))
GO
ALTER TABLE [dbo].[WN_InvoiceLines] NOCHECK CONSTRAINT [CK_WN_InvoiceLines_Quantity]
GO
ALTER TABLE [dbo].[WN_InvoiceLines]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_InvoiceLines_UnitPrice] CHECK  (([UnitPrice]>=(0)))
GO
ALTER TABLE [dbo].[WN_InvoiceLines] NOCHECK CONSTRAINT [CK_WN_InvoiceLines_UnitPrice]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_Invoices_DueDate] CHECK  (([DueOn]>=[IssuedOn]))
GO
ALTER TABLE [dbo].[WN_Invoices] NOCHECK CONSTRAINT [CK_WN_Invoices_DueDate]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_Invoices_Grand] CHECK  (([GrandTotal]>=(0)))
GO
ALTER TABLE [dbo].[WN_Invoices] NOCHECK CONSTRAINT [CK_WN_Invoices_Grand]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_Invoices_Paid] CHECK  (([PaidTotal]>=(0)))
GO
ALTER TABLE [dbo].[WN_Invoices] NOCHECK CONSTRAINT [CK_WN_Invoices_Paid]
GO
ALTER TABLE [dbo].[WN_Invoices]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_Invoices_SubTotal] CHECK  (([SubTotal]>=(0)))
GO
ALTER TABLE [dbo].[WN_Invoices] NOCHECK CONSTRAINT [CK_WN_Invoices_SubTotal]
GO
ALTER TABLE [dbo].[WN_NetworkAlertState]  WITH CHECK ADD  CONSTRAINT [CK_WN_NetworkAlertState_Kind] CHECK  (([Kind]=N'gwn' OR [Kind]=N'unifi' OR [Kind]=N'wan' OR [Kind]=N'hik'))
GO
ALTER TABLE [dbo].[WN_NetworkAlertState] CHECK CONSTRAINT [CK_WN_NetworkAlertState_Kind]
GO
ALTER TABLE [dbo].[WN_PaymentLines]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_PaymentLines_Amount] CHECK  (([Amount]<>(0)))
GO
ALTER TABLE [dbo].[WN_PaymentLines] NOCHECK CONSTRAINT [CK_WN_PaymentLines_Amount]
GO
ALTER TABLE [dbo].[WN_Persons]  WITH CHECK ADD CHECK  (([IdType]='Passport' OR [IdType]='CNIC'))
GO
ALTER TABLE [dbo].[WN_Persons]  WITH CHECK ADD CHECK  (([IdType]='Passport' OR [IdType]='CNIC'))
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_PricingRules_AdjType] CHECK  (([AdjustmentType]='FixedAmount' OR [AdjustmentType]='Percentage'))
GO
ALTER TABLE [dbo].[WN_PricingRules] NOCHECK CONSTRAINT [CK_WN_PricingRules_AdjType]
GO
ALTER TABLE [dbo].[WN_PricingRules]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_PricingRules_DayOfWeek] CHECK  (([DayOfWeek] IS NULL OR [DayOfWeek]>=(1) AND [DayOfWeek]<=(7)))
GO
ALTER TABLE [dbo].[WN_PricingRules] NOCHECK CONSTRAINT [CK_WN_PricingRules_DayOfWeek]
GO
ALTER TABLE [dbo].[WN_Quotations]  WITH CHECK ADD  CONSTRAINT [CK_WN_Quotations_Wht] CHECK  (([SendWhtInvoice]=(0) OR [WHTaxId]>(0) AND [WHTaxId]<(100)))
GO
ALTER TABLE [dbo].[WN_Quotations] CHECK CONSTRAINT [CK_WN_Quotations_Wht]
GO
ALTER TABLE [dbo].[WN_Refunds]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_Refunds_Amount] CHECK  (([Amount]>(0)))
GO
ALTER TABLE [dbo].[WN_Refunds] NOCHECK CONSTRAINT [CK_WN_Refunds_Amount]
GO
ALTER TABLE [dbo].[WN_SecurityDeposits]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_SecurityDeposits_Amount] CHECK  (([Amount]>(0)))
GO
ALTER TABLE [dbo].[WN_SecurityDeposits] NOCHECK CONSTRAINT [CK_WN_SecurityDeposits_Amount]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_SpaceInventoryConfig_MinCode] CHECK  (([MinCode]>=(0)))
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] NOCHECK CONSTRAINT [CK_WN_SpaceInventoryConfig_MinCode]
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_SpaceInventoryConfig_TotalSpaces] CHECK  (([TotalSpaces]>=(0)))
GO
ALTER TABLE [dbo].[WN_SpaceInventoryConfig] NOCHECK CONSTRAINT [CK_WN_SpaceInventoryConfig_TotalSpaces]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_SpacePricing_Dates] CHECK  (([EffectiveTo] IS NULL OR [EffectiveTo]>[EffectiveFrom]))
GO
ALTER TABLE [dbo].[WN_SpacePricing] NOCHECK CONSTRAINT [CK_WN_SpacePricing_Dates]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_SpacePricing_Deposit] CHECK  (([SecurityDeposit]>=(0)))
GO
ALTER TABLE [dbo].[WN_SpacePricing] NOCHECK CONSTRAINT [CK_WN_SpacePricing_Deposit]
GO
ALTER TABLE [dbo].[WN_SpacePricing]  WITH NOCHECK ADD  CONSTRAINT [CK_WN_SpacePricing_SeatPrice] CHECK  (([SeatPrice]>=(0)))
GO
ALTER TABLE [dbo].[WN_SpacePricing] NOCHECK CONSTRAINT [CK_WN_SpacePricing_SeatPrice]
GO
ALTER TABLE [dbo].[WN_Tax]  WITH CHECK ADD  CONSTRAINT [CK_WN_Tax_Dates] CHECK  (([EndDate] IS NULL OR [EndDate]>[StartDate]))
GO
ALTER TABLE [dbo].[WN_Tax] CHECK CONSTRAINT [CK_WN_Tax_Dates]
GO
ALTER TABLE [dbo].[WN_WhatsApp_Conversations]  WITH CHECK ADD  CONSTRAINT [CK_WN_WhatsApp_Conversations_Status] CHECK  (([Status]=N'closed' OR [Status]=N'open'))
GO
ALTER TABLE [dbo].[WN_WhatsApp_Conversations] CHECK CONSTRAINT [CK_WN_WhatsApp_Conversations_Status]
GO
ALTER TABLE [dbo].[WN_WhatsApp_Messages]  WITH CHECK ADD  CONSTRAINT [CK_WN_WhatsApp_Messages_Direction] CHECK  (([Direction]=N'outgoing' OR [Direction]=N'incoming'))
GO
ALTER TABLE [dbo].[WN_WhatsApp_Messages] CHECK CONSTRAINT [CK_WN_WhatsApp_Messages_Direction]
GO
