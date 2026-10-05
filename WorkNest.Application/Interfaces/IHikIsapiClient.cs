using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Talks directly to Hikvision access terminals over ISAPI (HTTP Digest auth).
    /// Mirrors HIK_Access-Control backend/src/isapi.js.
    /// </summary>
    public interface IHikIsapiClient
    {
        /// <summary>Create the person on the device; if the employee # already exists, modify it instead.</summary>
        Task<HikIsapiResult> UpsertPersonAsync(HikDeviceConnection device, string employeeNo, string name, string validBegin, string validEnd, bool enabled = true);
        /// <summary>Block (enabled=false) or unblock a person on the device, keeping their credentials.
        /// Returns SubStatusCode "employeeNoNotExist" when the person is not on that device.</summary>
        Task<HikIsapiResult> SetPersonEnabledAsync(HikDeviceConnection device, string employeeNo, bool enabled);
        /// <summary>true/false = person is / isn't on the device; null = unknown (offline, busy or error).</summary>
        Task<bool?> PersonExistsAsync(HikDeviceConnection device, string employeeNo);
        /// <summary>Current Allowed/Blocked state (Valid.enable) on the device; null = unknown / not there.</summary>
        Task<bool?> GetPersonEnabledAsync(HikDeviceConnection device, string employeeNo);
        /// <summary>Remove the person (and their credentials) from the device.</summary>
        Task<HikIsapiResult> DeletePersonAsync(HikDeviceConnection device, string employeeNo);
        Task<HikIsapiResult> AddCardAsync(HikDeviceConnection device, string employeeNo, string cardNo);
        Task<HikCardCaptureResult> CaptureCardAsync(HikDeviceConnection device);
        Task<HikFingerprintCaptureResult> CaptureFingerprintAsync(HikDeviceConnection device, int fingerNo);
        Task<HikIsapiResult> AddFingerprintAsync(HikDeviceConnection device, string employeeNo, string fingerData, int fingerNo);
        Task<HikFaceCaptureResult> CaptureFaceAsync(HikDeviceConnection device);
        Task<HikIsapiResult> AddFaceByImageAsync(HikDeviceConnection device, string employeeNo, byte[] jpeg);
    }
}
