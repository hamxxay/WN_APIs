using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Hikvision machine ⇄ database sync (replaces the HIK_Access-Control Node scheduler, which can no longer
    /// reach SAC400). Each job reads the machines over ISAPI and writes the WN_HIK_* tables.
    /// </summary>
    public interface IHikSyncService
    {
        /// <summary>Job names accepted by <see cref="RunJobAsync"/>.</summary>
        IReadOnlyList<string> JobNames { get; }

        /// <summary>Runs one job now (skipped when that job is already running). Returns its status.</summary>
        Task<HikSyncJobStatus> RunJobAsync(string job);

        /// <summary>Last run of every job.</summary>
        IReadOnlyList<HikSyncJobStatus> GetStatus();

        /// <summary>"Test" button: connects to one machine now and saves online / offline. Null when the machine doesn't exist.</summary>
        Task<HikDeviceTestResult?> TestDeviceAsync(int deviceId);

        /// <summary>"Test all": tests every machine in parallel and saves online / offline.</summary>
        Task<List<HikDeviceTestResult>> TestAllDevicesAsync();
    }
}
