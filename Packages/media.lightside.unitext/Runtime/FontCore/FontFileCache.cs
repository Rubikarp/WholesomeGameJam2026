using System;
using System.IO;
using System.Security.Cryptography;

namespace LightSide
{
    internal static class FontFileCache
    {
        private static readonly string root = Path.Combine(
            Path.GetTempPath(), "UniText", "FontCache", "snapshots");

        internal static FileFontSource OpenSnapshot(string sourcePath)
        {
            sourcePath = Path.GetFullPath(sourcePath);
            var sourceInfo = new FileInfo(sourcePath);
            if (!sourceInfo.Exists)
                throw new FileNotFoundException("Font file does not exist.", sourcePath);
            if (sourceInfo.Length <= 0 || sourceInfo.Length > int.MaxValue)
                throw new InvalidDataException($"Font file '{sourcePath}' has an unsupported length.");
            return PublishSnapshot(sourceInfo);
        }

        private static FileFontSource PublishSnapshot(FileInfo sourceInfo)
        {
            var expectedLength = sourceInfo.Length;
            var expectedTimestamp = sourceInfo.LastWriteTimeUtc;
            Directory.CreateDirectory(root);
            var temporaryPath = Path.Combine(root,
                $"{Guid.NewGuid():N}.tmp");
            try
            {
                byte[] digest;
                using (var input = new FileStream(sourceInfo.FullName, FileMode.Open,
                           FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
                using (var output = new FileStream(temporaryPath, FileMode.CreateNew,
                           FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[65536];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        hash.AppendData(buffer, 0, count);
                        output.Write(buffer, 0, count);
                    }
                    output.Flush(true);
                    if (output.Length != expectedLength)
                        throw new IOException($"Font file '{sourceInfo.FullName}' changed while it was copied.");
                    digest = hash.GetHashAndReset();
                }

                sourceInfo.Refresh();
                if (!sourceInfo.Exists || sourceInfo.Length != expectedLength
                                       || sourceInfo.LastWriteTimeUtc != expectedTimestamp)
                    throw new IOException($"Font file '{sourceInfo.FullName}' changed while it was copied.");

                var finalPath = Path.Combine(root, ToHex(digest) + ".sfnt");
                try { File.Move(temporaryPath, finalPath); }
                catch (IOException) when (IsPublished(finalPath, expectedLength)) { }
                if (!IsPublished(finalPath, expectedLength))
                    throw new InvalidDataException("The immutable font snapshot was not published completely.");
                return FileFontSource.OpenFile(finalPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static bool IsPublished(string path, long length)
            => File.Exists(path) && new FileInfo(path).Length == length;

        private static string ToHex(byte[] value)
        {
            var characters = new char[value.Length * 2];
            const string digits = "0123456789abcdef";
            for (var i = 0; i < value.Length; i++)
            {
                characters[i * 2] = digits[value[i] >> 4];
                characters[i * 2 + 1] = digits[value[i] & 15];
            }
            return new string(characters);
        }
    }
}
