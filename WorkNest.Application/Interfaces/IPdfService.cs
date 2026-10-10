using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.DTOs.Payment;

namespace WorkNest.Application.Interfaces
{
    public interface IPdfService
    {
        byte[] GenerateQuotationPdf(QuotationResponse quotation);
        byte[] GenerateBookingConfirmationPdf(ChallanResponseDto challan);
        byte[] GenerateAdvanceInvoicePdf(WorkNest.Application.DTOs.Payment.AdvanceInvoicePdfDto inv);
        byte[] GenerateSalesTaxInvoicePdf(CustomerSTInvoiceDto dto);
        byte[] GenerateAgreementPdf(WorkNest.Application.DTOs.Agreement.SendAgreementRequest request, string quotationNumber);
        /// <summary>One-page "Electronic signature certificate" appended to an e-signed agreement.</summary>
        byte[] GenerateESignatureCertificatePdf(WorkNest.Application.DTOs.Agreement.AgreementESignatureEvidence evidence);
    }
}
