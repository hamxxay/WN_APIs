using WorkNest.Application.DTOs.Transfer;

namespace WorkNest.Application.Interfaces
{
    public interface ITransferAddendumPdfService
    {
        byte[] GenerateAddendumPdf(
            ActiveBookingSummaryDto oldBooking,
            TargetSpaceSummaryDto newSpace,
            TransferSettlementResult settlement,
            int newBookingId,
            string notes);
    }
}

