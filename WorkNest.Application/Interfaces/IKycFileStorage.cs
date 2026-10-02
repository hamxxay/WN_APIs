using System.IO;
using System.Threading.Tasks;

namespace WorkNest.Application.Interfaces
{
    public interface IKycFileStorage
    {
        /// <summary>
        /// Saves a file into the customer-specific folder.
        /// </summary>
        /// <param name="folderName">Customer folder name (e.g., WN00012_a1b2c3d4e5f6)</param>
        /// <param name="fileName">Unique sanitized file name (e.g., WN00012_CNIC_FRONT_1234567890ab.pdf)</param>
        /// <param name="fileStream">File content stream</param>
        /// <returns>Relative stored path for saving in database</returns>
        Task<string> SaveAsync(string folderName, string fileName, Stream fileStream);

        /// <summary>
        /// Opens a readable stream for a stored KYC document.
        /// </summary>
        /// <param name="storedPath">Relative stored path from database</param>
        /// <returns>Readable file stream or null if not found</returns>
        Task<Stream?> OpenReadAsync(string storedPath);

        /// <summary>
        /// Deletes a file from physical storage (e.g., in rollback scenarios).
        /// </summary>
        /// <param name="storedPath">Relative stored path</param>
        Task<bool> DeleteAsync(string storedPath);
    }
}
