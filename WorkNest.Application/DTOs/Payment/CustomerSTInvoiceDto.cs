namespace WorkNest.Application.DTOs.Payment
{
    public class CustomerSTInvoiceLineDto
    {
        public string Description { get; set; } = string.Empty;
        public decimal ExclusiveAmount { get; set; }
        public decimal TaxPercentage { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotal => ExclusiveAmount + TaxAmount;
    }

    public class CustomerSTInvoiceDto
    {
        public int STInvoiceId { get; set; }
        public Guid STPublicId { get; set; }
        public int CustomerInvoiceId { get; set; }
        public string STInvoiceNumber { get; set; } = string.Empty;
        public string ParentInvoiceNumber { get; set; } = string.Empty;
        public DateTime IssuedOn { get; set; }
        public DateTime DueOn { get; set; }
        public string CurrencyCode { get; set; } = "PKR";
        public string? CenterName { get; set; }

        public string TariffHeading { get; set; } = "9805.9200";
        public string TariffLabel { get; set; } = "Business Support Services";

        public string RoomRentDescription { get; set; } = "Room Rent (Exclusive of Service Charge)";
        public decimal RoomRentAmount { get; set; }
        public decimal RoomRentTaxRate { get; set; }
        public decimal RoomRentTaxAmount { get; set; }

        public string ServiceChargeDescription { get; set; } = "Service Charges";
        public decimal ServiceChargeAmount { get; set; }
        public decimal ServiceChargeTaxRate { get; set; }
        public decimal ServiceChargeTaxAmount { get; set; }

        public string? SecurityDepositDescription { get; set; }
        public decimal? SecurityDepositAmount { get; set; }
        public decimal SecurityDepositTaxRate { get; set; }
        public decimal SecurityDepositTaxAmount { get; set; }

        public decimal SubTotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal GrandTotal { get; set; }
        public DateTime CreatedOn { get; set; }

        public string CustomerName { get; set; } = string.Empty;
        public string CustomerAddress { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string SntnNtnNic { get; set; } = string.Empty;

        public string VendorLegalName { get; set; } = "WorkNest Coworking Spaces (Pvt) Ltd";
        public string VendorAddress { get; set; } = "3rd Floor EOBI Building-II, I-8 Markaz, Islamabad";
        public string VendorPhone { get; set; } = "+92 309 9771774 / +92 308 0256000";
        public string VendorNtn { get; set; } = "7492018-3";
        public string? VendorLogoPath { get; set; }

        public List<CustomerSTInvoiceLineDto> LineItems { get; set; } = new();
    }
}
