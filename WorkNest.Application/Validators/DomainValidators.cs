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
            RuleFor(x => x.Message).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Phone).NotEmpty().MaximumLength(20);
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
            RuleFor(x => x.LocationId).GreaterThan(0);
            RuleFor(x => x.SpaceTypeId).GreaterThan(0);
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
            RuleFor(x => x.SpaceCategory).NotEmpty();
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
        }
    }

    public class SpaceConfigUpdateRequestValidator : AbstractValidator<SpaceConfigUpdateRequest>
    {
        public SpaceConfigUpdateRequestValidator()
        {
            RuleFor(x => x.TotalSpaces).GreaterThanOrEqualTo(1);
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
}
