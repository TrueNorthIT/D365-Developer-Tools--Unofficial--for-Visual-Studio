using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Persistence
{
    /// <summary>
    /// Reads/writes small JSON documents under %LocalAppData%\D365DeveloperTools. Guards each file
    /// with a named, machine-wide mutex: unlike the VS Code extension (one process per workspace),
    /// several devenv.exe processes can legitimately read/write the same recents/connection files
    /// concurrently.
    /// </summary>
    internal static class JsonFileStore
    {
        public static readonly string RootDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D365DeveloperTools");

        public static T Load<T>(string path) where T : class
        {
            return WithFileLock(path, () =>
            {
                if (!File.Exists(path)) { return null; }
                var json = File.ReadAllText(path, Encoding.UTF8);
                return string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<T>(json);
            });
        }

        public static void Save<T>(string path, T value)
        {
            WithFileLock<object>(path, () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? RootDirectory);
                var json = JsonConvert.SerializeObject(value, Formatting.Indented);

                var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tempPath, json, Encoding.UTF8);

                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempPath, path);
                }

                return null;
            });
        }

        public static void Delete(string path)
        {
            WithFileLock<object>(path, () =>
            {
                if (File.Exists(path)) { File.Delete(path); }
                return null;
            });
        }

        public static string HashKey(string key)
        {
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "");
            }
        }

        private static T WithFileLock<T>(string path, Func<T> action) where T : class
        {
            var mutexName = "Global\\D365DevTools_" + HashKey(path);
            using (var mutex = new Mutex(false, mutexName))
            {
                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
                    return action();
                }
                finally
                {
                    if (acquired) { mutex.ReleaseMutex(); }
                }
            }
        }
    }
}
