using FluentValidation;
using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.DTOs.Gallery;
using WorkNest.Application.DTOs.Space;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.DTOs.Location;
using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Application.DTOs.PlanFeature;

namespace WorkNest.Application.Validators
{
    public class ContactRequestValidator : AbstractValidator<ContactRequest>
    {
        public ContactRequestValidator()
        {
            RuleFor(x => x.FullName).NotEmpty().MaximumLength(255);
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Message).MaximumLength(1000);
            RuleFor(x => x.Phone).MaximumLength(20);
        }
    }

    public class GalleryUpsertRequestValidator : AbstractValidator<GalleryUpsertRequest>
    {
        public GalleryUpsertRequestValidator()
        {
            RuleFor(x => x.ImageUrl).NotEmpty();
        }
    }

    public class SpaceInsertRequestValidator : AbstractValidator<SpaceInsertRequest>
    {
        public SpaceInsertRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.LocationId).NotEmpty();
            RuleFor(x => x.SpaceTypeId).NotEmpty();
        }
    }

    public class BookingRequestValidator : AbstractValidator<BookingRequest>
    {
        public BookingRequestValidator()
        {
            RuleFor(x => x.StartDateTime).NotEmpty();
            RuleFor(x => x.EndDateTime).NotEmpty();
        }
    }

    public class SmartBookingRequestValidator : AbstractValidator<SmartBookingRequest>
    {
        public SmartBookingRequestValidator()
        {
            RuleFor(x => x.CategoryCode).NotEmpty();
            RuleFor(x => x.StartDateTime).NotEmpty();
            RuleFor(x => x.EndDateTime).NotEmpty();
        }
    }

    public class PayFastInitiateRequestValidator : AbstractValidator<PayFastInitiateRequest>
    {
        public PayFastInitiateRequestValidator()
        {
            RuleFor(x => x.BookingId).GreaterThan(0);
            RuleFor(x => x.CustomerEmail).NotEmpty().EmailAddress();
            RuleFor(x => x.CustomerName).NotEmpty();
        }
    }

    public class LocationUpsertRequestValidator : AbstractValidator<LocationUpsertRequest>
    {
        public LocationUpsertRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.BranchId).GreaterThan(0);
            RuleFor(x => x.CityId).GreaterThan(0);
        }
    }

    public class SpaceConfigUpdateRequestValidator : AbstractValidator<SpaceConfigUpdateRequest>
    {
        public SpaceConfigUpdateRequestValidator()
        {
            // No required fields — updatedBy is optional
        }
    }

    public class PlanFeatureRequestValidator : AbstractValidator<PlanFeatureRequest>
    {
        public PlanFeatureRequestValidator()
        {
            RuleFor(x => x.PlanId).GreaterThan(0);
            RuleFor(x => x.FeatureName).NotEmpty();
        }
    }

    /// <summary>WHT invoice: when ticked, the rate is required and must be 0.01-99.99 (same rule as the DB CHECK).</summary>
    public class AdminBookingRequestWhtValidator : AbstractValidator<WorkNest.Application.DTOs.Booking.AdminBookingRequest>
    {
        public AdminBookingRequestWhtValidator()
        {
            When(x => x.SendWhtInvoice, () =>
                // the Id of the chosen WN_WHTaxRate row (dropdown); the service checks it is an active rate
                RuleFor(x => x.WhtRate)
                    .NotNull().WithMessage("Select a WHT rate when a WHT invoice is generated.")
                    .GreaterThan(0m).WithMessage("Select a WHT rate from the list."));
        }
    }
}
