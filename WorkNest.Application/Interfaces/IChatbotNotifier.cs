namespace WorkNest.Application.Interfaces
{
    /// <summary>Asks the WorkNest WhatsApp chatbot to message a customer (the WhatsApp token lives only in the bot).</summary>
    public interface IChatbotNotifier
    {
        /// <summary>"Your complaint has been resolved — 1. the issue persists / 2. resolved". Returns (sent, error).</summary>
        Task<(bool Sent, string? Error)> SendComplaintResolvedAsync(string phone, string complaintNo);
    }
}
