using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Threading;
using UnityEngine;
#endif
using static System.Runtime.InteropServices.CallingConvention;

namespace LightSide
{
    internal static unsafe class Zstd
    {
    #if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
        private const string LibraryName = "__Internal";
    #else
        private const string LibraryName = "unitext_native";
    #endif

        private const int StreamBufferSize = 64 * 1024;
        private const int Sha256Size = 32;

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern int ut_zstd_decompress(void* src, int srcSize, void* dst, int dstCapacity);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern long ut_zstd_get_frame_content_size(void* src, int srcSize);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern IntPtr ut_zstd_stream_create();

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_zstd_stream_destroy(IntPtr stream);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern int ut_zstd_stream_decompress(IntPtr stream,
            void* src, int srcSize, out int srcConsumed,
            void* dst, int dstCapacity, out int dstWritten);

    #if UNITY_EDITOR
        private const string EditorLibraryName = "unitext_native_editor";

        [DllImport(EditorLibraryName, CallingConvention = Cdecl)]
        private static extern int ut_zstd_compress_bound(int srcSize);

        [DllImport(EditorLibraryName, CallingConvention = Cdecl)]
        private static extern int ut_zstd_compress(void* src, int srcSize, void* dst, int dstCapacity, int level);

        public static byte[] Compress(byte[] data, int level = 22)
        {
            if (data == null || data.Length == 0) return data;

            int bound = ut_zstd_compress_bound(data.Length);
            var output = new byte[bound];

            fixed (byte* src = data)
            fixed (byte* dst = output)
            {
                int written = ut_zstd_compress(src, data.Length, dst, bound, level);
                if (written <= 0)
                    throw new InvalidOperationException("Zstd compression failed");

                if (written < output.Length)
                    Array.Resize(ref output, written);

                return output;
            }
        }
    #endif

        public static bool IsCompressed(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            return data[0] == 0x28 && data[1] == 0xB5 && data[2] == 0x2F && data[3] == 0xFD;
        }

        public static long GetFrameContentSize(byte[] compressedData)
        {
            if (compressedData == null) throw new ArgumentNullException(nameof(compressedData));
            return GetFrameContentSize(new ReadOnlySpan<byte>(compressedData));
        }

        public static long GetFrameContentSize(ReadOnlySpan<byte> compressedData)
        {
            if (compressedData.IsEmpty)
                throw new ArgumentException("Zstd frame is empty.", nameof(compressedData));

            fixed (byte* src = compressedData)
            {
                long contentSize = ut_zstd_get_frame_content_size(src, compressedData.Length);
                if (contentSize < 0)
                    throw new InvalidDataException("Zstd frame does not declare a valid content size.");
                return contentSize;
            }
        }

        public static byte[] Decompress(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length == 0) return compressedData;

            long contentSize = GetFrameContentSize(compressedData);
            if (contentSize <= 0 || contentSize > int.MaxValue)
                throw new InvalidDataException("Zstd frame has an unsupported decompressed size.");

            var output = new byte[(int)contentSize];
            fixed (byte* src = compressedData)
            fixed (byte* dst = output)
            {
                int written = ut_zstd_decompress(src, compressedData.Length, dst, output.Length);
                if (written != output.Length)
                    throw new InvalidDataException(
                        $"Zstd decompression failed: expected {contentSize} bytes, got {written}.");
                return output;
            }
        }

        /// <summary>Sequentially decompresses one complete Zstd frame into an empty file, validates
        /// its raw length and compressed SHA-256, and flushes the file without closing either stream.</summary>
        public static void DecompressStreamToFile(Stream compressedInput, FileStream rawOutput,
            long expectedRawLength, ReadOnlySpan<byte> expectedCompressedSha256)
        {
            if (compressedInput == null) throw new ArgumentNullException(nameof(compressedInput));
            if (rawOutput == null) throw new ArgumentNullException(nameof(rawOutput));
            if (!compressedInput.CanRead)
                throw new ArgumentException("Compressed input is not readable.", nameof(compressedInput));
            if (!rawOutput.CanWrite)
                throw new ArgumentException("Raw output is not writable.", nameof(rawOutput));
            if (rawOutput.Position != 0 || rawOutput.Length != 0)
                throw new ArgumentException("Raw output must be an empty file positioned at its start.", nameof(rawOutput));
            if (expectedRawLength <= 0 || expectedRawLength > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(expectedRawLength));
            if (expectedCompressedSha256.Length != Sha256Size)
                throw new ArgumentException("Expected compressed SHA-256 must contain 32 bytes.",
                    nameof(expectedCompressedSha256));

            Span<byte> expectedHash = stackalloc byte[Sha256Size];
            expectedCompressedSha256.CopyTo(expectedHash);

            IntPtr decoder = ut_zstd_stream_create();
            if (decoder == IntPtr.Zero)
                throw new InvalidOperationException("Unable to create the Zstd streaming decoder.");

            byte[] inputBuffer = null;
            byte[] outputBuffer = null;
            try
            {
                inputBuffer = ArrayPool<byte>.Rent(StreamBufferSize);
                outputBuffer = ArrayPool<byte>.Rent(StreamBufferSize);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int inputOffset = 0;
                int inputCount = 0;
                long rawLength = 0;
                bool inputEnded = false;

                while (true)
                {
                    if (inputOffset == inputCount && !inputEnded)
                    {
                        inputOffset = 0;
                        inputCount = compressedInput.Read(inputBuffer, 0, StreamBufferSize);
                        inputEnded = inputCount == 0;
                        if (!inputEnded) hash.AppendData(inputBuffer, 0, inputCount);
                    }

                    int available = inputCount - inputOffset;
                    int status;
                    int consumed;
                    int written;
                    fixed (byte* src = inputBuffer)
                    fixed (byte* dst = outputBuffer)
                    {
                        status = ut_zstd_stream_decompress(decoder,
                            src + inputOffset, available, out consumed,
                            dst, StreamBufferSize, out written);
                    }

                    if (status < 0)
                        throw new InvalidDataException($"Zstd streaming decompression failed with status {status}.");
                    if (status > 1)
                        throw new InvalidDataException($"Zstd streaming decoder returned invalid status {status}.");
                    if ((uint)consumed > (uint)available || (uint)written > StreamBufferSize)
                        throw new InvalidDataException("Zstd streaming decoder returned invalid buffer progress.");

                    inputOffset += consumed;
                    if (written != 0)
                    {
                        rawLength += written;
                        if (rawLength > expectedRawLength)
                            throw new InvalidDataException(
                                $"Decompressed data exceeds the expected {expectedRawLength} bytes.");
                        rawOutput.Write(outputBuffer, 0, written);
                    }

                    if (status == 1)
                    {
                        if (inputOffset != inputCount || (!inputEnded && compressedInput.ReadByte() != -1))
                            throw new InvalidDataException("Compressed input contains data after its Zstd frame.");
                        if (rawLength != expectedRawLength)
                            throw new InvalidDataException(
                                $"Zstd frame produced {rawLength} bytes instead of {expectedRawLength}.");

                        byte[] actualHash = hash.GetHashAndReset();
                        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                            throw new InvalidDataException("Compressed source SHA-256 does not match its catalog identity.");

                        rawOutput.Flush(true);
                        return;
                    }

                    if (consumed == 0 && written == 0)
                    {
                        if (inputEnded)
                            throw new EndOfStreamException(
                                "Compressed input ended before the Zstd frame was complete.");
                        throw new InvalidDataException("Zstd streaming decoder made no progress.");
                    }
                }
            }
            finally
            {
                if (inputBuffer != null) ArrayPool<byte>.Return(inputBuffer);
                if (outputBuffer != null) ArrayPool<byte>.Return(outputBuffer);
                ut_zstd_stream_destroy(decoder);
            }
        }

        /// <summary>Creates a new output file and removes it if streaming decompression or
        /// integrity validation fails.</summary>
        public static void DecompressStreamToFile(Stream compressedInput, string outputPath,
            long expectedRawLength, ReadOnlySpan<byte> expectedCompressedSha256)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is empty.", nameof(outputPath));

            bool created = false;
            try
            {
                using (var output = new FileStream(outputPath, FileMode.CreateNew,
                           FileAccess.Write, FileShare.None, StreamBufferSize, FileOptions.SequentialScan))
                {
                    created = true;
                    DecompressStreamToFile(compressedInput, output,
                        expectedRawLength, expectedCompressedSha256);
                }
            }
            catch (Exception failure)
            {
                if (!created) throw;
                try
                {
                    File.Delete(outputPath);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(failure, cleanupFailure);
                }
                throw;
            }
        }

    #if UNITY_ANDROID && !UNITY_EDITOR
        private static int androidAssetManagerInitialized;

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern int ut_android_asset_manager_init(IntPtr virtualMachine, IntPtr assetManager);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern IntPtr ut_android_asset_open(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out long length);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern int ut_android_asset_read(IntPtr asset, void* destination, int capacity);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_android_asset_close(IntPtr asset);

        /// <summary>Captures Android's application AssetManager and its JNI lifetime on Unity's
        /// main thread so subsequent asset opens and reads require no Unity or Java calls.</summary>
        public static void InitializeAndroidAssetManager()
        {
            if (!MainThread.IsCurrent)
                throw new InvalidOperationException("Android AssetManager must be initialized on Unity's main thread.");

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (activity == null)
                throw new InvalidOperationException("UnityPlayer.currentActivity is unavailable.");
            using var assetManager = activity.Call<AndroidJavaObject>("getAssets");
            if (assetManager == null)
                throw new InvalidOperationException("Android application AssetManager is unavailable.");

            int status = ut_android_asset_manager_init(
                AndroidJNI.GetJavaVM(), assetManager.GetRawObject());
            if (status != 1)
                throw new InvalidOperationException(
                    $"Native Android AssetManager initialization failed with status {status}.");
            Volatile.Write(ref androidAssetManagerInitialized, 1);
        }

        /// <summary>Opens one independent sequential Android asset handle; the returned stream may
        /// be consumed by a worker after <see cref="InitializeAndroidAssetManager"/> ran on the main thread.</summary>
        public static Stream OpenAndroidAsset(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Android asset path is empty.", nameof(path));
            if (Volatile.Read(ref androidAssetManagerInitialized) == 0)
                throw new InvalidOperationException("Android AssetManager has not been initialized.");

            IntPtr asset = ut_android_asset_open(path, out long length);
            if (asset == IntPtr.Zero)
                throw new FileNotFoundException("Android packaged asset was not found.", path);
            if (length < 0)
            {
                ut_android_asset_close(asset);
                throw new IOException($"Android packaged asset '{path}' has an invalid length.");
            }
            return new AndroidAssetStream(asset, length);
        }

        private sealed class AndroidAssetStream : Stream
        {
            private readonly object gate = new();
            private readonly long length;
            private IntPtr asset;
            private long position;

            internal AndroidAssetStream(IntPtr asset, long length)
            {
                this.asset = asset;
                this.length = length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => length;
            public override long Position
            {
                get => position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (buffer == null) throw new ArgumentNullException(nameof(buffer));
                if ((uint)offset > (uint)buffer.Length)
                    throw new ArgumentOutOfRangeException(nameof(offset));
                if ((uint)count > (uint)(buffer.Length - offset))
                    throw new ArgumentOutOfRangeException(nameof(count));
                if (count == 0) return 0;

                lock (gate)
                {
                    if (asset == IntPtr.Zero) throw new ObjectDisposedException(nameof(AndroidAssetStream));
                    int read;
                    fixed (byte* destination = &buffer[offset])
                        read = ut_android_asset_read(asset, destination, count);
                    if (read < 0)
                        throw new IOException("Unable to read the Android packaged asset.");
                    position += read;
                    return read;
                }
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                lock (gate)
                {
                    if (asset != IntPtr.Zero)
                    {
                        ut_android_asset_close(asset);
                        asset = IntPtr.Zero;
                    }
                }
                base.Dispose(disposing);
            }

            ~AndroidAssetStream() => Dispose(false);
        }
    #endif
    }
}
