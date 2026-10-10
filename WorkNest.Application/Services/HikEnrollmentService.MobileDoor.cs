using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    public partial class HikEnrollmentService : IHikMobileDoorService
    {
        /// <summary>Why this person can't open doors on this booking right now; null when they can.</summary>
        private string? MobileDoorBlocked(HikAttendantContext? ctx)
        {
            if (ctx == null) return "You are not an access user on this booking.";
            if (!ctx.IsEnabled) return "Your access is turned off. Contact your company admin or reception.";
            if (ctx.IsSuspended) return "Door access is suspended because an invoice is unpaid. Please pay it or contact reception.";
            var now = _clock.Now;
            if (ctx.StartDateTime is DateTime start && start > now) return "Your booking has not started yet.";
            if (ctx.EndDateTime is DateTime end && end < now) return "Your booking has ended.";
            return null;
        }

        private async Task<(HikAttendantContext? Ctx, List<MobileDoorDto> Doors, List<HikDeviceConnection> Devices)> MobileDoorsOfAsync(int bookingDetailId, int personId)
        {
            var ctx = await _db.GetHikAttendantContextDbAsync(bookingDetailId, personId);
            if (ctx == null) return (null, new(), new());
            var (all, room) = await ResolveBookingMachinesAsync(ctx);
            var roomIds = room.Select(d => d.Id).ToHashSet();
            var doors = all
                .Select(d => new MobileDoorDto { DeviceId = d.Id, Name = d.Name, Kind = roomIds.Contains(d.Id) ? "room" : "entrance", Online = d.Online })
                .OrderBy(d => d.Kind == "room" ? 0 : 1).ThenBy(d => d.Name)
                .ToList();
            return (ctx, doors, all);
        }

        public async Task<MobileDoorsResult> GetDoorsAsync(int bookingDetailId, int personId)
        {
            var (ctx, doors, _) = await MobileDoorsOfAsync(bookingDetailId, personId);
            var blocked = MobileDoorBlocked(ctx);
            if (blocked != null) return new MobileDoorsResult { Message = blocked, SpaceName = ctx?.SpaceName };
            if (doors.Count == 0)
                return new MobileDoorsResult { Message = "No door machine is set up for your room yet. Please contact reception.", SpaceName = ctx!.SpaceName };
            return new MobileDoorsResult { Ok = true, Message = "Choose the door to unlock.", SpaceName = ctx!.SpaceName, Doors = doors };
        }

        public async Task<MobileOpenDoorResult> OpenDoorAsync(int bookingDetailId, int personId, int deviceId, string? byEmail)
        {
            var (ctx, doors, devices) = await MobileDoorsOfAsync(bookingDetailId, personId);
            var blocked = MobileDoorBlocked(ctx);
            if (blocked != null) return new MobileOpenDoorResult { Message = blocked };
            var device = devices.FirstOrDefault(d => d.Id == deviceId);
            if (device == null) return new MobileOpenDoorResult { Message = "You can't open this door. Choose one of your doors." };

            var result = await _isapi.RemoteControlDoorAsync(device, "open");
            var who = $"{ctx!.Name} ({byEmail ?? "app"})";
            try
            {
                await _db.InsertHikDoorLogDbAsync(device.Id, "mobile-unlock", result.Ok,
                    result.Ok ? $"Unlocked from the app by {who} for {ctx.SpaceName}" : $"App unlock by {who} failed: {result.Error}");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not log the app unlock of device {Device}", device.Id); }

            if (result.Ok) return new MobileOpenDoorResult { Ok = true, Door = device.Name, Message = $"{device.Name} is unlocked." };
            _logger.LogWarning("App unlock of {Device} by {Who} failed: {Error}", device.Name, who, result.Error);
            return new MobileOpenDoorResult
            {
                Door = device.Name,
                Message = result.Unreachable
                    ? $"{device.Name} can't be reached right now. Please use your card, fingerprint or face, or ask reception."
                    : $"{device.Name} did not unlock. Please try again or ask reception."
            };
        }
    }
}
