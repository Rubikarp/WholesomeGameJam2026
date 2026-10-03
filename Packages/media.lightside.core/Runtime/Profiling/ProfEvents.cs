using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LightSide
{
    internal enum ProfEvType : byte { ZoneBegin, ZoneEnd }

    internal struct ProfEv
    {
        public long ticks;
        public long value;
        public int id;
        public ProfEvType type;
    }

    /// <summary>
    /// Per-thread event log: a chunked, single-writer (owning thread) append buffer. The hot path only writes
    /// raw begin/end events here; the call tree is reconstructed offline from the events, not built inline. Blocks
    /// are pooled across captures (Reset keeps them), and the block-list layout lets a future drain worker consume
    /// whole filled blocks while the owner keeps appending to the tail.
    /// </summary>
    internal sealed class ProfThreadLog
    {
        private const int BlockBits = 13;
        private const int BlockSize = 1 << BlockBits;
        private const int BlockMask = BlockSize - 1;

        private readonly List<ProfEv[]> blocks = new();
        private ProfEv[] current;
        private long count;

        /// <summary>Cumulative bytes the log itself allocated (block growth) plus other profiler-internal allocations the owner books here. <see cref="Prof"/> subtracts it from every probe read so the profiler's own garbage never lands in a user zone's delta.</summary>
        internal long overheadBytes;

        public ProfThreadLog()
        {
            current = new ProfEv[BlockSize];
            blocks.Add(current);
        }

        public long Count => count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(long ticks, long value, int id, ProfEvType type)
        {
            int i = (int)(count & BlockMask);
            if (i == 0 && count != 0)
            {
                int b = (int)(count >> BlockBits);
                if (b == blocks.Count)
                {
                    long before = ProfAlloc.Bytes();
                    blocks.Add(new ProfEv[BlockSize]);
                    overheadBytes += ProfAlloc.Bytes() - before;
                }
                current = blocks[b];
            }
            ref var e = ref current[i];
            e.ticks = ticks;
            e.value = value;
            e.id = id;
            e.type = type;
            count++;
        }

        public void Reset()
        {
            count = 0;
            if (blocks.Count == 0)
            {
                current = new ProfEv[BlockSize];
                blocks.Add(current);
            }
            else current = blocks[0];
        }

        public void ReleaseBlocks()
        {
            count = 0;
            current = null;
            blocks.Clear();
        }

        public ref ProfEv At(long index) => ref blocks[(int)(index >> BlockBits)][(int)(index & BlockMask)];
    }
}
