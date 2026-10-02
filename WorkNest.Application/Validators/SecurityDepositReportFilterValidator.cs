using FluentValidation;
using WorkNest.Application.DTOs.Reports;

namespace WorkNest.Application.Validators
{
    public class SecurityDepositReportFilterValidator : AbstractValidator<SecurityDepositReportFilterDto>
    {
        public SecurityDepositReportFilterValidator()
        {
            RuleFor(x => x)
                .Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.FromDate.Value <= x.ToDate.Value)
                .WithMessage("FromDate cannot be greater than ToDate.");
        }
    }
}
