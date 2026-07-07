using System;
using System.IO;
using System.Security.Cryptography;

namespace EDNexus.Core.Settings
{
    public static class Hashing
    {
        /// <summary>Compute SHA256 hex string for the given file path.</summary>
        public static string ComputeSha256Hex(string path)
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(fs);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
