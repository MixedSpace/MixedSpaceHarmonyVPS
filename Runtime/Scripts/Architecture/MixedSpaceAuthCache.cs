using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MixedSpace
{
    /// <summary>Installs native-issued token caches before verification. File existence never grants authorization.</summary>
    public static class MixedSpaceAuthCache
    {
        public const string StreamingSubdirectory = "xrloc";
        public const string WindowsCacheDirectoryName = "vps.njyjxr";

        public static string GetCacheFileName(string developerToken)
        {
            if (string.IsNullOrWhiteSpace(developerToken)) return null;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(developerToken));
                var name = new StringBuilder("auth_cache_");
                foreach (byte value in hash) name.Append(value.ToString("x2"));
                return name.Append(".dat").ToString();
            }
        }

        public static string GetCacheDirectory()
        {
#if UNITY_OPENHARMONY && !UNITY_EDITOR
            var buffer = new byte[512];
            int status = OH_AbilityRuntime_ApplicationContextGetCacheDir(buffer, buffer.Length, out int written);
            if (status != 0 || written <= 0 || written > buffer.Length) return null;
            int end = Array.IndexOf(buffer, (byte)0, 0, written);
            return Encoding.UTF8.GetString(buffer, 0, end >= 0 ? end : written);
#elif UNITY_EDITOR_WIN
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root)) root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root)) root = Environment.GetEnvironmentVariable("TEMP");
            return string.IsNullOrEmpty(root) ? Directory.GetCurrentDirectory() :
                Path.Combine(root, WindowsCacheDirectoryName);
#else
            // Cache-directory contracts for other native platforms have not been supplied.
            return null;
#endif
        }

#if UNITY_OPENHARMONY && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("libability_runtime.so",
            CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int OH_AbilityRuntime_ApplicationContextGetCacheDir(
            [System.Runtime.InteropServices.Out] byte[] buffer, int bufferSize, out int writeLength);
#endif

        public static string GetCacheFilePath(string developerToken)
        {
            string name = GetCacheFileName(developerToken);
            if (name == null) return null;
            string directory = GetCacheDirectory();
            return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, name);
        }

        public static bool IsInstalled(string developerToken)
        {
            string path = GetCacheFilePath(developerToken);
            return !string.IsNullOrEmpty(path) && File.Exists(path) && new FileInfo(path).Length > 0;
        }

        /// <summary>Call on the Unity thread. Missing/invalid files never bypass the subsequent native token check.</summary>
        public static Task<bool> InstallFromStreamingAssetsAsync(string developerToken,
            CancellationToken cancellationToken = default) =>
            InstallFromStreamingAssetsAsync(developerToken, cancellationToken, false);

        internal static Task<bool> RefreshFromStreamingAssetsAsync(string developerToken,
            CancellationToken cancellationToken) =>
            InstallFromStreamingAssetsAsync(developerToken, cancellationToken, true);

        private static async Task<bool> InstallFromStreamingAssetsAsync(string developerToken,
            CancellationToken cancellationToken, bool replaceExisting)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string name = GetCacheFileName(developerToken);
                string destination = GetCacheFilePath(developerToken);
                if (name == null || destination == null) return false;
                if (!replaceExisting && File.Exists(destination) && new FileInfo(destination).Length > 0) return true;
                byte[] bytes;
#if UNITY_OPENHARMONY && !UNITY_EDITOR
                string source = Application.streamingAssetsPath.TrimEnd('/') + "/" + StreamingSubdirectory + "/" + name;
                if (File.Exists(source))
                    bytes = File.ReadAllBytes(source);
                else
                {
                if (!source.Contains("://")) source = new Uri(source).AbsoluteUri;
                using (var request = UnityWebRequest.Get(source))
                {
                    request.timeout = 10;
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (cancellationToken.IsCancellationRequested) request.Abort();
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (request.result != UnityWebRequest.Result.Success) return false;
                    bytes = request.downloadHandler.data;
                }
                }
#else
                string source = Path.Combine(Application.streamingAssetsPath, StreamingSubdirectory, name);
                if (!File.Exists(source)) return false;
                bytes = File.ReadAllBytes(source);
                await Task.CompletedTask;
#endif
                cancellationToken.ThrowIfCancellationRequested();
                if (bytes == null || bytes.Length == 0) return false;
                InstallBytes(destination, bytes, replaceExisting);
                MixedSpaceLogger.Log("XRLoc authorization cache installed; native verification will determine validity.",
                    MixedSpaceLogger.LoggingLevel.Verbose);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                // No token, payload, or token-derived path is written to logs.
                MixedSpaceLogger.LogWarning("XRLoc authorization cache installation failed (" + error.GetType().Name + "); continuing native verification.");
                return false;
            }
        }

        internal static void InstallBytes(string destination, byte[] bytes) =>
            InstallBytes(destination, bytes, false);

        private static void InstallBytes(string destination, byte[] bytes, bool replaceExisting)
        {
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("Empty authorization cache.", nameof(bytes));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (replaceExisting)
                {
                    // Replace only after a native rejection; the refreshed cache is verified again.
                    File.Copy(temporary, destination, true);
                    return;
                }
                // Another SDK instance may have installed the same cache while awaiting the APK read.
                if (File.Exists(destination))
                {
                    if (new FileInfo(destination).Length > 0) return;
                    File.Delete(destination);
                }
                try { File.Move(temporary, destination); }
                catch (IOException)
                {
                    if (!File.Exists(destination) || new FileInfo(destination).Length == 0) throw;
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
