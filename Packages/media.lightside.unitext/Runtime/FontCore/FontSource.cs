using System;
using System.Collections.Generic;
using System.IO;
#if !UNITY_ANDROID || UNITY_EDITOR
using System.IO.MemoryMappedFiles;
#endif
using System.Runtime.InteropServices;
using System.Threading;

namespace LightSide
{
    internal abstract class FontSource
    {
        internal abstract string Identity { get; }
        internal abstract int Length { get; }
        internal abstract long OwnedByteCount { get; }
        internal abstract FontBackingLease Open();

        internal byte[] CopyBytes()
        {
            using var lease = Open();
            var result = new byte[lease.Length];
            Marshal.Copy(lease.Pointer, result, 0, result.Length);
            return result;
        }

        internal unsafe int ComputeLegacyHash()
        {
            using var lease = Open();
            return UniTextFont.ComputeFontDataHash(
                new ReadOnlySpan<byte>((void*)lease.Pointer, lease.Length));
        }
    }

    internal sealed class ArrayFontSource : FontSource
    {
        private static long nextIdentity;
        private readonly byte[] data;
        private readonly ArrayFontBacking backing;
        private readonly string identity;

        internal ArrayFontSource(byte[] data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) throw new ArgumentException("Font data is empty.", nameof(data));
            backing = new ArrayFontBacking(data);
            identity = $"array:{Interlocked.Increment(ref nextIdentity):X16}";
        }

        internal override string Identity => identity;
        internal override int Length => data.Length;
        internal override long OwnedByteCount => data.LongLength;
        internal override FontBackingLease Open() => backing.Open();
    }

    internal sealed class FileFontSource : FontSource
    {
        private static readonly Dictionary<string, WeakReference<FileFontSource>> sources = new();
        private static readonly object sourcesGate = new();

        private readonly MappedFontBacking backing;
        private readonly string filePath;
        private readonly string identity;
        private readonly int length;

        private FileFontSource(string path)
        {
            var fileLength = new FileInfo(path).Length;
            if (fileLength <= 0 || fileLength > int.MaxValue)
                throw new InvalidDataException($"Font file '{path}' has an unsupported length.");

            filePath = path;
            length = (int)fileLength;
            var timestamp = File.GetLastWriteTimeUtc(path).Ticks;
            identity = $"file:{path}|{length:X8}|{timestamp:X16}";
            backing = new MappedFontBacking(path, length);
        }

        internal override string Identity => identity;
        internal string FilePath => filePath;
        internal override int Length => length;
        internal override long OwnedByteCount => 0;
        internal override FontBackingLease Open() => backing.Open();

        internal static FileFontSource OpenFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Font file path is empty.", nameof(path));

            path = Path.GetFullPath(path);
            var candidate = new FileFontSource(path);
            lock (sourcesGate)
            {
                if (sources.TryGetValue(candidate.identity, out var weak)
                    && weak.TryGetTarget(out var existing))
                {
                    candidate.backing.DisposeUnused();
                    return existing;
                }

                sources[candidate.identity] = new WeakReference<FileFontSource>(candidate);
                return candidate;
            }
        }

        internal static FileFontSource OpenEphemeral(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Font file path is empty.", nameof(path));

            path = Path.GetFullPath(path);
            var source = new FileFontSource(path);
            try
            {
                File.Delete(path);
                if (File.Exists(path))
                    throw new IOException($"Unable to remove ephemeral font file '{path}'.");
                return source;
            }
            catch
            {
                source.backing.DisposeUnused();
                throw;
            }
        }

        internal static void ResetRegistry()
        {
            lock (sourcesGate)
            {
                sources.Clear();
                sources.TrimExcess();
            }
        }
    }

    internal abstract class FontBacking
    {
        private readonly object gate = new();
        private IntPtr pointer;
        private int length;
        private int leases;

        internal FontBackingLease Open()
        {
            lock (gate)
            {
                if (leases == 0)
                {
                    try { OpenCore(out pointer, out length); }
                    catch
                    {
                        CloseCore();
                        pointer = IntPtr.Zero;
                        length = 0;
                        throw;
                    }
                    if (pointer == IntPtr.Zero || length <= 0)
                    {
                        CloseCore();
                        pointer = IntPtr.Zero;
                        length = 0;
                        throw new InvalidOperationException("Font backing did not provide readable data.");
                    }
                }

                leases++;
                return new FontBackingLease(this, pointer, length);
            }
        }

        internal void Release()
        {
            lock (gate)
            {
                if (--leases != 0) return;
                CloseCore();
                pointer = IntPtr.Zero;
                length = 0;
            }
        }

        protected abstract void OpenCore(out IntPtr pointer, out int length);
        protected abstract void CloseCore();
    }

    internal sealed class FontBackingLease : IDisposable
    {
        private FontBacking owner;
        private readonly IntPtr pointer;
        private readonly int length;

        internal FontBackingLease(FontBacking owner, IntPtr pointer, int length)
        {
            this.owner = owner;
            this.pointer = pointer;
            this.length = length;
        }

        internal IntPtr Pointer => Volatile.Read(ref owner) != null ? pointer : IntPtr.Zero;
        internal int Length => Volatile.Read(ref owner) != null ? length : 0;

        ~FontBackingLease() => Dispose();

        public void Dispose()
        {
            var previous = Interlocked.Exchange(ref owner, null);
            if (previous == null) return;
            previous.Release();
            GC.SuppressFinalize(this);
        }
    }

    internal sealed class ArrayFontBacking : FontBacking
    {
        private readonly byte[] data;
        private GCHandle handle;

        internal ArrayFontBacking(byte[] data) => this.data = data;

        protected override void OpenCore(out IntPtr pointer, out int length)
        {
            handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            pointer = handle.AddrOfPinnedObject();
            length = data.Length;
        }

        protected override void CloseCore()
        {
            if (handle.IsAllocated) handle.Free();
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal sealed class MappedFontBacking : FontBacking
    {
        private const int ProtRead = 1;
        private const int MapPrivate = 2;
        private int descriptor;
        private readonly int length;
        private IntPtr mapping;

        internal MappedFontBacking(string path, int length)
        {
            descriptor = open(path, 0);
            if (descriptor < 0)
                throw new IOException($"Unable to open mapped font file '{path}'.");
            this.length = length;
        }

        protected override void OpenCore(out IntPtr pointer, out int mappedLength)
        {
            mapping = mmap(IntPtr.Zero, (UIntPtr)(uint)length,
                ProtRead, MapPrivate, descriptor, IntPtr.Zero);
            if (mapping == new IntPtr(-1)) mapping = IntPtr.Zero;
            pointer = mapping;
            mappedLength = length;
        }

        protected override void CloseCore()
        {
            if (mapping == IntPtr.Zero) return;
            munmap(mapping, (UIntPtr)(uint)length);
            mapping = IntPtr.Zero;
        }

        internal void DisposeUnused() => CloseDescriptor();

        ~MappedFontBacking()
        {
            CloseCore();
            CloseDescriptor();
        }

        private void CloseDescriptor()
        {
            var previous = Interlocked.Exchange(ref descriptor, -1);
            if (previous >= 0) close(previous);
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int open(string path, int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern IntPtr mmap(IntPtr address, UIntPtr length,
            int protection, int flags, int descriptor, IntPtr offset);

        [DllImport("libc", SetLastError = true)]
        private static extern int munmap(IntPtr address, UIntPtr length);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int descriptor);
    }
#else
    internal unsafe sealed class MappedFontBacking : FontBacking
    {
        private readonly FileStream stream;
        private readonly int length;
        private MemoryMappedFile mapping;
        private MemoryMappedViewAccessor view;
        private byte* acquiredPointer;

        internal MappedFontBacking(string path, int length)
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.RandomAccess);
            this.length = length;
        }

        protected override void OpenCore(out IntPtr pointer, out int mappedLength)
        {
            mapping = MemoryMappedFile.CreateFromFile(stream, null, 0,
                MemoryMappedFileAccess.Read, HandleInheritability.None, true);
            view = mapping.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
            byte* raw = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref raw);
            acquiredPointer = raw;
            pointer = (IntPtr)(raw + view.PointerOffset);
            mappedLength = length;
        }

        protected override void CloseCore()
        {
            if (acquiredPointer != null)
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
                acquiredPointer = null;
            }
            view?.Dispose();
            view = null;
            mapping?.Dispose();
            mapping = null;
        }

        internal void DisposeUnused() => stream.Dispose();

        ~MappedFontBacking()
        {
            CloseCore();
            stream.Dispose();
        }
    }
#endif

    internal sealed class FreeTypeFace : IDisposable
    {
        private FontBackingLease backing;
        private IntPtr pointer;

        private FreeTypeFace(FontBackingLease backing, IntPtr pointer)
        {
            this.backing = backing;
            this.pointer = pointer;
        }

        internal IntPtr Pointer => pointer;

        internal static FreeTypeFace TryCreate(FontSource source, int faceIndex)
        {
            if (source == null) return null;
            var backing = source.Open();
            var pointer = FT.LoadFace(backing.Pointer, backing.Length, faceIndex);
            if (pointer != IntPtr.Zero) return new FreeTypeFace(backing, pointer);
            backing.Dispose();
            return null;
        }

        ~FreeTypeFace() => Dispose();

        public void Dispose()
        {
            var previous = Interlocked.Exchange(ref pointer, IntPtr.Zero);
            if (previous == IntPtr.Zero) return;
            if (FT.IsInitialized) FT.UnloadFace(previous);
            backing.Dispose();
            backing = null;
            GC.SuppressFinalize(this);
        }
    }
}
