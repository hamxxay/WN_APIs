namespace WorkNest.Application.DTOs.AmountField
{
    public class AmountFieldDto
    {
        public int Id { get; set; }
        public string Entity { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Currency { get; set; } = "PKR";
    }
}
