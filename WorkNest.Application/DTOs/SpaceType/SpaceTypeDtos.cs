namespace WorkNest.Application.DTOs.SpaceType
{
    public class SpaceTypeUpsertRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public byte? CategoryId { get; set; }
        public short? Capacity { get; set; }
        public bool HourlyAllowed { get; set; }
        public int? AccountReceivableId { get; set; }
        public int? RentAccountId { get; set; }
        public int? ServicesIncomeId { get; set; }
        public int? SalesTaxId { get; set; }
        public int? SecurityReceivedId { get; set; }

        /// <summary>
        /// The location the type belongs to (each location has its own space types). Create: missing = the admin's only
        /// location, or location 1 (I-8) for a super admin. Update: missing = the type stays where it is.
        /// </summary>
        public int? LocationId { get; set; }
    }

    public class SpaceTypeDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public byte? CategoryId { get; set; }
        public short? Capacity { get; set; }
        public bool HourlyAllowed { get; set; }
        public int? AccountReceivableId { get; set; }
        public int? RentAccountId { get; set; }
        public int? ServicesIncomeId { get; set; }
        public int? SalesTaxId { get; set; }
        public int? SecurityReceivedId { get; set; }
        public string? AccountReceivableName { get; set; }
        public string? RentAccountName { get; set; }
        public string? ServicesIncomeName { get; set; }
        public string? SalesTaxName { get; set; }
        public string? SecurityReceivedName { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
    }

    /// <summary>
    /// A rule of the WN_SpaceTypes_* procedures (THROW 50000, plain text for users): a name that already exists at the
    /// location, a type with spaces at another location that can't be moved, an unknown location. Thrown by the
    /// repository so the message can be shown (400); any other database error is not caught.
    /// </summary>
    public sealed class SpaceTypeRuleException : Exception
    {
        public SpaceTypeRuleException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }
}
