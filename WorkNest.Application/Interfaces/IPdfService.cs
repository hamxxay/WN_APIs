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
        /// <summary>
        /// Agreement page 1. <paramref name="securityDepositMonths"/> labels the deposit ("PKR 126,000 (2 months)"; 0 = amount
        /// only); a 0 deposit (request.SecurityDeposit) has no deposit row.
        /// </summary>
        byte[] GenerateAgreementPdf(WorkNest.Application.DTOs.Agreement.SendAgreementRequest request, string quotationNumber, int securityDepositMonths = 0);
        /// <summary>One-page "Electronic signature certificate" appended to an e-signed agreement.</summary>
        byte[] GenerateESignatureCertificatePdf(WorkNest.Application.DTOs.Agreement.AgreementESignatureEvidence evidence);
    }
}
