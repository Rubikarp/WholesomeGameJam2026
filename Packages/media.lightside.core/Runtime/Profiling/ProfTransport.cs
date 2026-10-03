#if UNITY_EDITOR || PROF_ENABLE
using System;
using System.IO;
using System.Text;
using UnityEngine;
#endif

namespace LightSide
{
    /// <summary>
    /// Writes a serialized capture to disk: the project <c>Benchmarks/</c> folder in the editor,
    /// <see cref="Application.persistentDataPath"/> in players (pull via adb/Xcode/Files). Compiles to a
    /// no-op in players without <c>PROF_ENABLE</c>.
    /// </summary>
    public static class ProfTransport
    {
#if UNITY_EDITOR || PROF_ENABLE
        /// <summary>Ships UTF-8 <paramref name="text"/> (a capture JSON or Chrome trace) under <paramref name="filename"/>.</summary>
        public static void Ship(string text, string filename) => Ship(Encoding.UTF8.GetBytes(text), filename);

        /// <summary>Ships raw <paramref name="bytes"/> under <paramref name="filename"/>. A failed write is logged as a warning, never thrown.</summary>
        public static void Ship(byte[] bytes, string filename)
        {
            try
            {
                var path = WriteFile(bytes, filename);
                Debug.Log($"[ProfTransport] wrote {path} ({bytes.Length} B)");
            }
            catch (Exception e) { Debug.LogWarning($"[ProfTransport] write failed for {filename}: {e.Message}"); }
        }

        private static string WriteFile(byte[] bytes, string filename)
        {
#if UNITY_EDITOR
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Benchmarks"));
#else
            var dir = Application.persistentDataPath;
#endif
            var path = Path.Combine(dir, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Delete(temp);
            else File.Move(temp, path);
            return path;
        }
#else
        public static void Ship(string text, string filename) { }
        public static void Ship(byte[] bytes, string filename) { }
#endif
    }
}
