using System;
using System.IO;
using System.Security.Cryptography;

namespace MHLab.Patch.Core.Utilities
{
    public static class Hashing
    {
        public static string GetFileHash(string filePath)
        {
            using (var sha1 = new SHA1CryptoServiceProvider())
                return GetHash(filePath, sha1);
        }

        private static string GetHash(string filePath, HashAlgorithm hasher)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return GetHash(fs, hasher);
        }

        private static string GetHash(Stream s, HashAlgorithm hasher)
        {
            var hash = hasher.ComputeHash(s);
            var hashStr = Convert.ToBase64String(hash);
            return hashStr.TrimEnd('=');
        }
    }
}
