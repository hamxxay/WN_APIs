using WorkNest.Application.DTOs.Transfer;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    public class TransferAddendumPdfService : ITransferAddendumPdfService
    {
        public byte[] GenerateAddendumPdf(
            ActiveBookingSummaryDto oldBooking,
            TargetSpaceSummaryDto newSpace,
            TransferSettlementResult settlement,
            int newBookingId,
            string notes)
        {
            return TransferAddendumPdfGenerator.Generate(oldBooking, newSpace, settlement, newBookingId, notes);
        }
    }
}

