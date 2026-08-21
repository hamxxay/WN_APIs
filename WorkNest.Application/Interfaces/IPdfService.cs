using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Quotation;

namespace WorkNest.Application.Interfaces
{
    public interface IPdfService
    {
        byte[] GenerateQuotationPdf(QuotationResponse quotation);
        byte[] GenerateBookingConfirmationPdf(ChallanResponseDto challan);
        byte[] GenerateAdvanceInvoicePdf(WorkNest.Application.DTOs.Payment.AdvanceInvoicePdfDto inv);
    }
}
