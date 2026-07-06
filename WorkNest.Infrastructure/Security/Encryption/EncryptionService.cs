using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Security.Encryption
{
    /// <summary>
    /// AES-256 encryption/decryption and SHA-256 hashing service.
    /// Keys are loaded from appsettings.json — never hardcoded.
    /// </summary>
    public class EncryptionService : IEncryptionService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public EncryptionService(IConfiguration configuration)
        {
            var keyStr = configuration["Encryption:Key"]
                ?? throw new InvalidOperationException("Encryption:Key is not configured.");
            var ivStr = configuration["Encryption:IV"]
                ?? throw new InvalidOperationException("Encryption:IV is not configured.");

            // Derive a 32-byte key and 16-byte IV from config strings
            using var sha256 = SHA256.Create();
            _key = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyStr));
            _iv = MD5.HashData(Encoding.UTF8.GetBytes(ivStr));
        }

        /// <summary>Encrypts plain text using AES-256-CBC and returns Base64 ciphertext.</summary>
        public string Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            return Convert.ToBase64String(cipherBytes);
        }

        /// <summary>Decrypts a Base64 AES-256-CBC ciphertext back to plain text.</summary>
        public string Decrypt(string cipherText)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var cipherBytes = Convert.FromBase64String(cipherText);
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }

        /// <summary>Returns a SHA-256 hex hash of the input string.</summary>
        public string Hash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
