namespace WorkNest.Application.Interfaces
{
    /// <summary>AES-256 encryption, decryption, and SHA-256 hashing.</summary>
    public interface IEncryptionService
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
        string Hash(string input);
    }
}
