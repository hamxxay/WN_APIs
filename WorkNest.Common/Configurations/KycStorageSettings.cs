namespace WorkNest.Common.Configurations
{
    public class KycStorageSettings
    {
        public string RootPath { get; set; } = "KYC Documents";
        public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024; // 5 MB default
        public string[] AllowedExtensions { get; set; } = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
    }
}
