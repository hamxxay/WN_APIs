using System;
using System.Collections.Generic;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Services;

namespace WorkNest.Application.Helpers
{
    public static class ChallanFieldBuilder
    {
        public static void BuildForChallan(ChallanResponseDto dto)
        {
            if (dto == null) return;
            ChallanCalculationService.ApplyToChallan(dto);
        }

        public static void BuildForQuotation(QuotationResponse dto)
        {
            if (dto == null) return;
            ChallanCalculationService.ApplyToQuotation(dto);
        }
    }
}