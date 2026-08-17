namespace WorkNest.Application.DTOs.AmountField
{
    public class AmountFieldDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsDebit { get; set; }
        public bool IsActive { get; set; }
        public int? AccountId { get; set; }
        public string? AccountDescription { get; set; }
    }

    public class AmountFieldUpdateAccountRequest
    {
        public int? AccountId { get; set; }
    }
}
