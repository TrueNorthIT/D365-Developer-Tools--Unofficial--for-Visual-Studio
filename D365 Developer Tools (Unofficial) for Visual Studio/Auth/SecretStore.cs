using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Auth
{
    /// <summary>
    /// At-rest protection for client secrets, roughly equivalent to VS Code's OS-keychain-backed
    /// SecretStorage. Uses DPAPI (CurrentUser scope) — the same security boundary VS Code's
    /// SecretStorage relies on via Windows Credential Manager.
    /// </summary>
    internal static class SecretStore
    {
        private static readonly string SecretsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D365DeveloperTools", "secrets");

        public static void Store(string environmentUrl, string clientId, string secret)
        {
            Directory.CreateDirectory(SecretsDirectory);
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), optionalEntropy: null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(GetPath(environmentUrl, clientId), protectedBytes);
        }

        public static string TryGet(string environmentUrl, string clientId)
        {
            var path = GetPath(environmentUrl, clientId);
            if (!File.Exists(path)) { return null; }

            try
            {
                var protectedBytes = File.ReadAllBytes(path);
                var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException)
            {
                // Secret was written by a different user profile, or is corrupt — treat as absent.
                return null;
            }
        }

        public static void Delete(string environmentUrl, string clientId)
        {
            var path = GetPath(environmentUrl, clientId);
            if (File.Exists(path)) { File.Delete(path); }
        }

        private static string GetPath(string environmentUrl, string clientId)
        {
            var key = $"{environmentUrl}|{clientId}";
            string hash;
            using (var sha256 = SHA256.Create())
            {
                hash = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "");
            }

            return Path.Combine(SecretsDirectory, hash + ".bin");
        }
    }
}
