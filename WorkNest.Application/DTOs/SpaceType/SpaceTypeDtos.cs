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
    }
}
