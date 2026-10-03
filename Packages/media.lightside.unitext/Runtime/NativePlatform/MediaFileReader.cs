using System;
using System.Threading.Tasks;

namespace LightSide
{
    /// <summary>
    /// Reads a file reference's bytes the way the platform requires — a path read on desktop, a
    /// <c>content://</c> resolver on Android, the captured browser blob on web. Source-agnostic: a clipboard
    /// file, a dropped file, and a picked file all resolve through here.
    /// </summary>
    internal static class MediaFileReader
    {
        public static byte[] Read(string fileReference)
        {
            if (string.IsNullOrEmpty(fileReference)) return null;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return ClipboardAndroid.ReadFile(fileReference);
#elif UNITY_WEBGL && !UNITY_EDITOR
                return ClipboardWebGL.ReadFile(fileReference);
#else
                return System.IO.File.ReadAllBytes(ResolvePath(fileReference));
#endif
            }
            catch { return null; }
        }

        /// <summary>
        /// Worker-thread variant so large media (a dropped video) never stalls the main
        /// thread. Web bytes are already in memory and complete synchronously; Android's
        /// content resolver goes through JNI, which requires the calling thread to be
        /// attached to the VM for the duration.
        /// </summary>
        public static Task<byte[]> ReadAsync(string fileReference)
        {
            if (string.IsNullOrEmpty(fileReference)) return Task.FromResult<byte[]>(null);
#if UNITY_WEBGL && !UNITY_EDITOR
            return Task.FromResult(Read(fileReference));
#elif UNITY_ANDROID && !UNITY_EDITOR
            return Task.Run(() =>
            {
                UnityEngine.AndroidJNI.AttachCurrentThread();
                try { return Read(fileReference); }
                finally { UnityEngine.AndroidJNI.DetachCurrentThread(); }
            });
#else
            return Task.Run(() => Read(fileReference));
#endif
        }

        private static string ResolvePath(string fileReference)
            => fileReference.StartsWith("file://", StringComparison.Ordinal)
                ? new Uri(fileReference).LocalPath
                : fileReference;
    }
}
