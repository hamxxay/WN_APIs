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

        // ---- Sync jobs (ports of HIK isapi.js read calls) ----
        /// <summary>GET deviceInfo: true when the machine answers (used as the online probe).</summary>
        Task<bool> PingAsync(HikDeviceConnection device, TimeSpan timeout);
        Task<DateTime?> GetDeviceTimeAsync(HikDeviceConnection device);
        /// <summary>Write the current Pakistan time (or tzOffsetMinutes east of UTC) to the machine.</summary>
        Task<HikIsapiResult> SetDeviceTimeAsync(HikDeviceConnection device, int tzOffsetMinutes = 300);
        Task<HikSearchPage> SearchPersonsAsync(HikDeviceConnection device, int position, int maxResults, TimeSpan timeout);
        Task<HikSearchPage> SearchEventsAsync(HikDeviceConnection device, int position, int maxResults, TimeSpan timeout);
        Task<HikSearchPage> ReadAllCardsAsync(HikDeviceConnection device, int position, int maxResults, TimeSpan timeout);
        /// <summary>Card numbers of one person; null when the machine could not be read.</summary>
        Task<List<string>?> ReadCardsAsync(HikDeviceConnection device, string employeeNo);
        /// <summary>Fingerprint templates of one person; null when the machine could not be read.</summary>
        Task<List<HikFingerprintTemplate>?> ReadFingerprintsAsync(HikDeviceConnection device, string employeeNo);
        /// <summary>Face recognition templates (modelData) of one person; null when the machine could not be read.</summary>
        Task<List<string>?> ReadFacesAsync(HikDeviceConnection device, string employeeNo);
        Task<HikIsapiResult> AddFaceByModelAsync(HikDeviceConnection device, string employeeNo, string modelData);
        Task<HikIsapiResult> DeleteCardAsync(HikDeviceConnection device, string cardNo);
        Task<HikIsapiResult> RemoteControlDoorAsync(HikDeviceConnection device, string cmd = "open", int doorNo = 1);
        /// <summary>Raw UserInfo of one person; Found=false when not on the machine; Result.Ok=false when unknown.</summary>
        Task<(System.Text.Json.Nodes.JsonObject? Person, HikIsapiResult Result)> GetPersonRecordAsync(HikDeviceConnection device, string employeeNo);
        /// <summary>Add (modify=false) or modify a person exactly as given (name, validity, enabled, admin).</summary>
        Task<HikIsapiResult> WritePersonAsync(HikDeviceConnection device, HikPersonRecord person, bool modify);

        // ---- Machines / users endpoints (ports of HIK isapi.js) ----
        /// <summary>GET deviceInfo (model, serial, firmware). Ok=false with Error when the machine did not answer.</summary>
        Task<HikDeviceInfo> GetDeviceInfoAsync(HikDeviceConnection device, TimeSpan timeout);
        /// <summary>AccessControl capabilities (face / fingerprint / card support, person counts); null when unavailable.</summary>
        Task<System.Text.Json.Nodes.JsonNode?> GetCapabilitiesAsync(HikDeviceConnection device);
        /// <summary>Delete every fingerprint of a person (face, cards and profile stay).</summary>
        Task<HikIsapiResult> DeleteFingerprintsAsync(HikDeviceConnection device, string employeeNo);
        /// <summary>Delete the enrolled face of a person (fingerprints, cards and profile stay).</summary>
        Task<HikIsapiResult> DeleteFaceAsync(HikDeviceConnection device, string employeeNo);
    }
}
