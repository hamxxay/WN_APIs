using System.Text.Json;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Talks to the UniFi Site Manager cloud API (api.ui.com, X-API-KEY header) and, through its
    /// connector, to the console's Network API. Mirrors apiGet / apiGetAll / net in "Unifi UI" server.js.
    /// </summary>
    public interface IUnifiClient
    {
        /// <summary>True when Unifi:ApiKey is set in appsettings.</summary>
        bool IsConfigured { get; }
        /// <summary>GET a Site Manager endpoint (e.g. /v1/hosts) and return the parsed JSON body.</summary>
        Task<JsonElement> GetAsync(string path, IDictionary<string, string>? query = null, CancellationToken ct = default);
        /// <summary>GET a paged Site Manager endpoint, following nextToken, and return the concatenated `data` arrays.</summary>
        Task<List<JsonElement>> GetAllAsync(string path, IDictionary<string, string>? query = null, CancellationToken ct = default);
        /// <summary>Call the console Network API via /v1/connector/consoles/{consoleId}/proxy/network{path}.
        /// A non-null body is sent as JSON (POST unless another method is given).</summary>
        Task<JsonElement> NetworkAsync(string consoleId, string path, object? body = null, HttpMethod? method = null, CancellationToken ct = default);
    }

    /// <summary>
    /// A change was sent to the UniFi console but no answer came back in time (cloud connector 408 / 504, or our own
    /// timeout). The change may or may not have been saved: callers re-read the console to find out.
    /// </summary>
    public class UnifiTimeoutException : InvalidOperationException
    {
        public UnifiTimeoutException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
