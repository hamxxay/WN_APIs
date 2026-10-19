using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// The check a background service makes at the top of each cycle: <c>if (await _gate.IsOnAsync(ct)) ...work...</c>.
    /// Asks <see cref="IServiceSwitch"/> (fails open) and remembers the last answer, so the service's log gets one line
    /// when it is switched off ("X paused by the service switch.") and one when it is switched on again ("X resumed."),
    /// never one per cycle. One gate per service, shared by all its loops (thread-safe).
    /// </summary>
    public sealed class ServiceSwitchGate
    {
        private const int Off = 0, On = 1;

        private readonly IServiceSwitch _switch;
        private readonly ILogger _logger;
        private int _state = On; // a service starts running; switched off at the first check = one "paused" line

        public ServiceSwitchGate(IServiceSwitch serviceSwitch, string serviceName, ILogger logger)
        {
            var info = BackgroundServiceCatalog.Get(serviceName);
            _switch = serviceSwitch;
            _logger = logger;
            Name = info.Name;
            DisplayName = info.DisplayName;
        }

        public string Name { get; }
        public string DisplayName { get; }

        /// <summary>True = do this cycle's work; false = paused by the switch (or the service is stopping).</summary>
        public async Task<bool> IsOnAsync(CancellationToken ct = default)
        {
            bool on;
            try
            {
                on = await _switch.IsEnabledAsync(Name, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false; // shutting down: the loop's own delay ends it
            }
            catch (Exception)
            {
                on = true; // IServiceSwitch already fails open and logs; this only guards against a surprise
            }

            var before = Interlocked.Exchange(ref _state, on ? On : Off);
            if (before == On && !on)
                _logger.LogInformation("{Service} paused by the service switch.", DisplayName);
            else if (before == Off && on)
                _logger.LogInformation("{Service} resumed.", DisplayName);
            return on;
        }
    }
}
