using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// UTF-8 marshaling helpers the BCL lacks: HGlobal-allocated buffers (the allocator
    /// the native clipboard plugins free against) and the parallel-array item marshaling
    /// the iOS / WebGL <c>SetItems</c> entry points share. For plain pointer↔string
    /// conversion use <see cref="Marshal.PtrToStringUTF8(IntPtr)"/> /
    /// <see cref="Marshal.StringToCoTaskMemUTF8"/> directly (.NET Standard 2.1).
    /// </summary>
    internal static class MarshalUtf8
    {
        /// <summary>
        /// Allocates a null-terminated UTF-8 copy of <paramref name="str"/> on the HGlobal
        /// heap. Free with <c>Marshal.FreeHGlobal</c> — note the different allocator from
        /// <c>Marshal.StringToCoTaskMemUTF8</c>. <see langword="null"/> is treated as empty.
        /// </summary>
        public static IntPtr AllocHGlobalUtf8(string str)
            => AllocHGlobalUtf8(string.IsNullOrEmpty(str)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(str));

        /// <summary>
        /// Allocates a null-terminated copy of already-UTF-8 <paramref name="utf8"/> bytes on
        /// the HGlobal heap — no intermediate string round-trip. Free with
        /// <c>Marshal.FreeHGlobal</c>. <see langword="null"/> is treated as empty.
        /// </summary>
        public static IntPtr AllocHGlobalUtf8(byte[] utf8)
        {
            int len = utf8?.Length ?? 0;
            IntPtr ptr = Marshal.AllocHGlobal(len + 1);
            if (len > 0) Marshal.Copy(utf8, 0, ptr, len);
            Marshal.WriteByte(ptr, len, 0);
            return ptr;
        }

        /// <summary>
        /// Marshals text items into parallel UTF-8 (format, payload) pointer arrays,
        /// invokes <paramref name="call"/>, and frees every buffer on return — the
        /// choreography the iOS and WebGL <c>SetItems</c> natives share. Callers pass the
        /// pre-partitioned text list of a <see cref="ClipboardWrite"/>; the image payload
        /// travels alongside as pointer + length (a C-string boundary would truncate its
        /// bytes at the first NUL). Returns <see langword="false"/> without invoking when
        /// the list is empty.
        /// </summary>
        public static bool WithUtf8Items(IReadOnlyList<ClipboardItem> items, Action<IntPtr[], IntPtr[], int> call)
        {
            int count = items.Count;
            if (count == 0) return false;

            var formats = new IntPtr[count];
            var payloads = new IntPtr[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    formats[i] = AllocHGlobalUtf8(items[i].Format.Identifier);
                    payloads[i] = AllocHGlobalUtf8(items[i].Data);
                }
                call(formats, payloads, count);
                return true;
            }
            finally
            {
                for (int i = 0; i < count; i++)
                {
                    if (formats[i] != IntPtr.Zero) Marshal.FreeHGlobal(formats[i]);
                    if (payloads[i] != IntPtr.Zero) Marshal.FreeHGlobal(payloads[i]);
                }
            }
        }
    }
}
