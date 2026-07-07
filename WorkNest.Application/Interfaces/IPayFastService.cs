namespace WorkNest.Application.Interfaces
{
    /// <summary>PayFast payment gateway operations.</summary>
    public interface IPayFastService
    {
        /// <summary>Builds the full PayFast payment payload including signature.</summary>
        Dictionary<string, string> BuildPayload(string bookingId, double amount, string description,
            string customerEmail, string customerName, string orderId);

        /// <summary>Verifies an incoming IPN notify callback signature.</summary>
        bool VerifySignature(Dictionary<string, string> notifyData);
    }
}
