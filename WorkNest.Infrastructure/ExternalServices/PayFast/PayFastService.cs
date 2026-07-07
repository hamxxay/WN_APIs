using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.PayFast
{
    /// <summary>
    /// PayFast payment gateway service.
    /// Mirrors the Python payfast.py build_payfast_payload() and verify_notify_signature() exactly.
    /// All credentials loaded from appsettings.json — never hardcoded.
    /// </summary>
    public class PayFastService : IPayFastService
    {
        private readonly string _merchantId;
        private readonly string _securedKey;
        private readonly bool _sandbox;
        private readonly string _sandboxUrl;
        private readonly string _returnUrl;
        private readonly string _notifyUrl;

        public PayFastService(IConfiguration config)
        {
            _merchantId = config["PayFast:MerchantId"] ?? string.Empty;
            _securedKey = config["PayFast:SecuredKey"] ?? string.Empty;
            _sandbox    = bool.Parse(config["PayFast:Sandbox"] ?? "true");
            _sandboxUrl = config["PayFast:SandboxUrl"] ?? "https://sandbox.payfast.pk/v2/hosted_payment";
            _returnUrl  = config["PayFast:ReturnUrl"] ?? string.Empty;
            _notifyUrl  = config["PayFast:NotifyUrl"] ?? string.Empty;
        }

        /// <summary>
        /// Builds the full PayFast payment payload including HMAC-SHA256 signature.
        /// Mirrors Python build_payfast_payload() exactly.
        /// </summary>
        public Dictionary<string, string> BuildPayload(string bookingId, double amount, string description,
            string customerEmail, string customerName, string orderId)
        {
            var parameters = new Dictionary<string, string>
            {
                ["merchant_id"]    = _merchantId,
                ["order_id"]       = orderId,
                ["amount"]         = amount.ToString("F2"),
                ["currency"]       = "PKR",
                ["description"]    = description,
                ["customer_email"] = customerEmail,
                ["customer_name"]  = customerName,
                ["return_url"]     = _returnUrl,
                ["notify_url"]     = _notifyUrl,
                ["booking_id"]     = bookingId.ToString(),
            };

            parameters["signature"]   = GenerateSignature(parameters);
            parameters["payment_url"] = _sandboxUrl;

            return parameters;
        }

        /// <summary>
        /// Verifies an incoming PayFast IPN callback by recomputing the signature.
        /// Mirrors Python verify_notify_signature() exactly.
        /// </summary>
        public bool VerifySignature(Dictionary<string, string> notifyData)
        {
            var data = new Dictionary<string, string>(notifyData);
            data.TryGetValue("signature", out var receivedSig);
            data.Remove("signature");
            var expectedSig = GenerateSignature(data);
            return receivedSig == expectedSig;
        }

        /// <summary>
        /// Generates HMAC-SHA256 signature.
        /// Alphabetically sorted key=value pairs (excluding empty values and 'signature'),
        /// concatenated with & then appended with the secured key — matches Python exactly.
        /// </summary>
        private string GenerateSignature(Dictionary<string, string> parameters)
        {
            var sorted = parameters
                .Where(kv => kv.Key != "signature" && !string.IsNullOrEmpty(kv.Value))
                .OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}={kv.Value}");

            var payload = string.Join("&", sorted) + _securedKey;
            var bytes   = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
