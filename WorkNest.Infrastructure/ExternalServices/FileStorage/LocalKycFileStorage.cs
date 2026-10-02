using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Configurations;

namespace WorkNest.Infrastructure.ExternalServices.FileStorage
{
    public class LocalKycFileStorage : IKycFileStorage
    {
        private readonly IWebHostEnvironment _env;
        private readonly string _absoluteRootPath;

        public LocalKycFileStorage(IWebHostEnvironment env, IOptions<KycStorageSettings> settings)
        {
            _env = env;
            var configPath = settings.Value.RootPath;
            if (string.IsNullOrWhiteSpace(configPath))
            {
                configPath = "App_Data/KYC Documents";
            }

            // Ensure storage path is outside wwwroot
            if (Path.IsPathRooted(configPath))
            {
                _absoluteRootPath = Path.GetFullPath(configPath);
            }
            else
            {
                _absoluteRootPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, configPath));
            }

            if (!Directory.Exists(_absoluteRootPath))
            {
                Directory.CreateDirectory(_absoluteRootPath);
            }
        }

        private static string SanitizeSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return "unknown";
            // Allow alphanumeric, underscores, hyphens only
            var cleaned = Regex.Replace(segment, @"[^a-zA-Z0-9_\-]", "_");
            return cleaned.Trim('_');
        }

        public async Task<string> SaveAsync(string folderName, string fileName, Stream fileStream)
        {
            var safeFolder = SanitizeSegment(folderName);
            var safeFile = Path.GetFileName(fileName);
            // Replace any '+' or invalid characters
            safeFile = safeFile.Replace("+", "_");

            var targetDir = Path.GetFullPath(Path.Combine(_absoluteRootPath, safeFolder));

            // Prevent path traversal
            if (!targetDir.StartsWith(_absoluteRootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid storage directory path traversal detected.");
            }

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var targetFilePath = Path.GetFullPath(Path.Combine(targetDir, safeFile));

            if (!targetFilePath.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid file path traversal detected.");
            }

            // Save stream to physical file
            await using var output = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            if (fileStream.CanSeek)
            {
                fileStream.Seek(0, SeekOrigin.Begin);
            }
            await fileStream.CopyToAsync(output);

            // Return relative path: folderName/fileName
            return Path.Combine(safeFolder, safeFile).Replace('\\', '/');
        }

        public Task<Stream?> OpenReadAsync(string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return Task.FromResult<Stream?>(null);

            var normalized = storedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_absoluteRootPath, normalized));

            // Check primary path
            if (File.Exists(fullPath))
            {
                Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult<Stream?>(stream);
            }

            // Check fallback paths (e.g. if uploaded before folder path change)
            var fallbacks = new[]
            {
                Path.GetFullPath(Path.Combine(_env.ContentRootPath, "KYC Documents", normalized)),
                Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", normalized)),
                Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "KYC Documents", normalized)),
                Path.GetFullPath(Path.Combine("F:\\WN_APIs\\WorkNest.API\\App_Data", normalized)),
                Path.GetFullPath(Path.Combine("F:\\WN_APIs\\WorkNest.API\\App_Data\\KYC Documents", normalized))
            };

            foreach (var fb in fallbacks)
            {
                if (File.Exists(fb))
                {
                    Stream stream = new FileStream(fb, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return Task.FromResult<Stream?>(stream);
                }
            }

            return Task.FromResult<Stream?>(null);
        }

        public Task<bool> DeleteAsync(string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return Task.FromResult(false);

            try
            {
                var normalized = storedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(_absoluteRootPath, normalized));

                if (!fullPath.StartsWith(_absoluteRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(false);
                }

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return Task.FromResult(true);
                }
            }
            catch
            {
                // Ignore physical delete errors during rollback
            }

            return Task.FromResult(false);
        }
    }
}
