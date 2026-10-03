using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LightSide
{
    public static partial class GpuUpload
    {
        private struct AcquiredSlot
        {
            internal uint id;
            internal uint generation;
            internal NativeArray<byte> view;
        }

        private const int BuilderCount = 2;
        private const int MaxBuilderCount = 64;
        private const int BatchTargetCapacity = 4;
        private const int HistoryCapacity = 258;
        private const int ReservedHistory = -2;
        private const int DetachedHistory = -3;
        private static readonly List<AcquiredSlot> acquiredSlots = new();
        private static BatchBuilder[] builders;
        private static int[] freeBuilders;
        private static int freeBuilderCount;
        private static BatchBuilder[] submittedBuilders;
        private static int submittedBuilderCount;
        private static HistoryEntry[] history;
        private static int[] historyNext;
        private static int historyHead;
        private static int historyTail = -1;

        /// <summary>Byte capacity of a standard upload slot; zero until a session initializes.</summary>
        public static int SlotCapacityBytes => deviceInfo.SlotBytes;

        /// <summary>Maximum regional writes accepted in one batch; zero until a session initializes.</summary>
        public static int MaxRegionsPerBatch => deviceInfo.MaxRegionsPerBatch;

        /// <summary>
        /// Acquires a writable upload slot of at least minBytes. Standard requests are served from
        /// the resident ring; larger requests up to <see cref="GpuUploadDeviceInfo.MaxStagingBytes"/>
        /// allocate a dedicated transient slot destroyed at retirement, and anything above that
        /// ceiling is refused. <see cref="GpuUploadError.Backpressure"/> means the ring is
        /// exhausted: drain an outstanding ticket and retry. Jobs may write into the view until
        /// the slot is submitted or released.
        /// </summary>
        public static bool TryAcquireSlot(int minBytes, out GpuUploadSlot slot,
            out GpuUploadError error)
        {
            slot = default;
            if (minBytes <= 0)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            Initialize();
            CheckDeviceEpoch();
            if (!CanAdmitNewWork)
            {
                error = AdmissionError;
                return false;
            }
            error = GpuUploadAbi.AcquireSlot((uint)minBytes, out uint id, out uint generation,
                out uint capacityBytes, out IntPtr mapped, out bool contractViolation);
            if (contractViolation)
            {
                ReleaseSlotNative(id, generation);
                DisableCorruptedSession(error);
                return false;
            }
            if (error != GpuUploadError.None)
            {
                ObserveBackendError(error);
                return false;
            }
            NativeArray<byte> view;
            unsafe
            {
                view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                    (void*)mapped, (int)capacityBytes, Allocator.None);
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, AtomicSafetyHandle.Create());
#endif
            try
            {
                acquiredSlots.Add(new AcquiredSlot { id = id, generation = generation, view = view });
            }
            catch (OutOfMemoryException)
            {
                ReleaseSlotView(view);
                ReleaseSlotNative(id, generation);
                error = GpuUploadError.OutOfMemory;
                return false;
            }
            slot = new GpuUploadSlot(id, generation, sessionGeneration, view);
            error = GpuUploadError.None;
            return true;
        }

        /// <summary>
        /// Returns an acquired, unsubmitted slot to the ring and invalidates the handle;
        /// idempotent for stale or default handles. Every job writing into the view must be
        /// complete.
        /// </summary>
        public static void ReleaseSlot(ref GpuUploadSlot slot)
        {
            uint id = slot.id;
            uint generation = slot.generation;
            bool currentSession = slot.sessionGeneration == sessionGeneration;
            slot = default;
            if (generation == 0) return;
            ConsumeAcquiredSlot(id, generation);
            if (!currentSession) return;
            var error = ReleaseSlotNative(id, generation);
            if (error != GpuUploadError.None && error != GpuUploadError.NotInitialized)
                ObserveBackendError(error);
        }

        private static GpuUploadError ReleaseSlotNative(uint id, uint generation)
        {
            try
            {
                return GpuUploadAbi.ReleaseSlot(id, generation);
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                               || exception is EntryPointNotFoundException
                                               || exception is BadImageFormatException)
            {
                RecordBindingFailure(exception);
                bindingUnavailable = true;
                availabilityError = GpuUploadError.UnsupportedBackend;
                return GpuUploadError.UnsupportedBackend;
            }
        }

        private static void ReleaseAndConsumeSlot(ref GpuUploadSlot slot)
        {
            uint id = slot.id;
            uint generation = slot.generation;
            ConsumeSlot(ref slot);
            if (generation != 0) ReleaseSlotNative(id, generation);
        }

        /// <summary>
        /// Claims an allocation-free batch builder whose regions address bytes of one upload slot
        /// supplied at submit. <see cref="GpuUploadError.Backpressure"/> means every builder is in
        /// flight: drain an outstanding ticket and retry.
        /// </summary>
        public static bool TryBeginBatch(GpuUploadBatchOptions options, out GpuUploadBatch batch,
            out GpuUploadError error) =>
            TryBeginBatch(GpuUploadOverlapPolicy.ValidateNonOverlapping, options, out batch,
                out error);

        /// <summary>Claims an allocation-free batch builder with an explicit destination-overlap policy.</summary>
        public static bool TryBeginBatch(GpuUploadOverlapPolicy overlapPolicy,
            GpuUploadBatchOptions options, out GpuUploadBatch batch, out GpuUploadError error)
        {
            batch = default;
            if (!TryGetBatchFlags(overlapPolicy, options, out uint batchFlags))
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            Initialize();
            CheckDeviceEpoch();
            if (!CanAdmitNewWork)
            {
                error = AdmissionError;
                return false;
            }
            if (!TryEnsureBuilders(out error)) return false;
            if (freeBuilderCount == 0 && !TryGrowBuilders(out error)) return false;
            int index = freeBuilders[--freeBuilderCount];
            var builder = builders[index];
            if (!TryBeginBuilder(builder))
            {
                freeBuilders[freeBuilderCount++] = index;
                error = GpuUploadError.InternalError;
                return false;
            }
            builder.batchFlags = batchFlags;
            batch = new GpuUploadBatch(index, builder.generation);
            error = GpuUploadError.None;
            return true;
        }

        internal static bool IsSlotAcquired(in GpuUploadSlot slot) =>
            slot.generation != 0 && slot.sessionGeneration == sessionGeneration
            && FindAcquiredSlot(slot.id, slot.generation) >= 0;

        private static void ConsumeSlot(ref GpuUploadSlot slot)
        {
            ConsumeAcquiredSlot(slot.id, slot.generation);
            slot = default;
        }

        private static bool ConsumeAcquiredSlot(uint id, uint generation)
        {
            int index = FindAcquiredSlot(id, generation);
            if (index < 0) return false;
            ReleaseSlotView(acquiredSlots[index].view);
            int last = acquiredSlots.Count - 1;
            acquiredSlots[index] = acquiredSlots[last];
            acquiredSlots.RemoveAt(last);
            return true;
        }

        private static int FindAcquiredSlot(uint id, uint generation)
        {
            for (int i = 0; i < acquiredSlots.Count; i++)
                if (acquiredSlots[i].id == id && acquiredSlots[i].generation == generation)
                    return i;
            return -1;
        }

        private static void ReleaseSlotView(in NativeArray<byte> view)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (view.IsCreated)
                AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(view));
#endif
        }

        private static void ReleaseAcquiredSlotsForTeardown()
        {
            bool releaseUnavailable = false;
            for (int i = 0; i < acquiredSlots.Count; i++)
            {
                var slot = acquiredSlots[i];
                ReleaseSlotView(slot.view);
                if (releaseUnavailable) continue;
                releaseUnavailable = ReleaseSlotNative(slot.id, slot.generation)
                                     == GpuUploadError.UnsupportedBackend;
            }
            acquiredSlots.Clear();
        }

        private static BatchBuilder NewBuilder(int blobBytes, int regionLimit, int freeIndex)
        {
            var builder = new BatchBuilder();
            builder.blob = new NativeArray<byte>(blobBytes, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            builder.targets = new GpuUploadTarget[BatchTargetCapacity];
            builder.targetHandles = new GpuUploadAbi.TargetHandle[BatchTargetCapacity];
            builder.targetIndices = new Dictionary<GpuUploadTarget, int>(BatchTargetCapacity);
            builder.regionEnds = new ulong[regionLimit];
            builder.freeIndex = freeIndex;
            return builder;
        }

        private static int BuilderBlobBytes(int regionLimit) =>
            checked(GpuUploadAbi.BatchSize + BatchTargetCapacity * GpuUploadAbi.TargetSize
                + regionLimit * GpuUploadAbi.RegionSize);

        private static bool TryGrowBuilders(out GpuUploadError error)
        {
            if (builders.Length >= MaxBuilderCount)
            {
                error = GpuUploadError.Backpressure;
                return false;
            }
            int regionLimit = deviceInfo.MaxRegionsPerBatch;
            try
            {
                var builder = NewBuilder(BuilderBlobBytes(regionLimit), regionLimit,
                    builders.Length);
                int count = builders.Length + 1;
                Array.Resize(ref builders, count);
                Array.Resize(ref freeBuilders, count);
                Array.Resize(ref submittedBuilders, count);
                builders[count - 1] = builder;
                freeBuilders[freeBuilderCount++] = count - 1;
                error = GpuUploadError.None;
                return true;
            }
            catch (OutOfMemoryException)
            {
                error = GpuUploadError.OutOfMemory;
                return false;
            }
        }

        private static bool TryEnsureBuilders(out GpuUploadError error)
        {
            if (builders != null)
            {
                error = GpuUploadError.None;
                return true;
            }
            int regionLimit = deviceInfo.MaxRegionsPerBatch;
            BatchBuilder[] allocated = null;
            try
            {
                int blobBytes = BuilderBlobBytes(regionLimit);
                allocated = new BatchBuilder[BuilderCount];
                var free = new int[BuilderCount];
                for (int i = 0; i < allocated.Length; i++)
                {
                    allocated[i] = NewBuilder(blobBytes, regionLimit, i);
                    free[i] = i;
                }
                var submitted = new BatchBuilder[BuilderCount];
                var entries = new HistoryEntry[HistoryCapacity];
                var next = new int[HistoryCapacity];
                for (int i = 0; i < next.Length - 1; i++) next[i] = i + 1;
                next[next.Length - 1] = -1;
                builders = allocated;
                freeBuilders = free;
                freeBuilderCount = free.Length;
                submittedBuilders = submitted;
                submittedBuilderCount = 0;
                history = entries;
                historyNext = next;
                historyHead = 0;
                historyTail = next.Length - 1;
                error = GpuUploadError.None;
                return true;
            }
            catch (OutOfMemoryException)
            {
                DisposeBuilders(allocated);
                error = GpuUploadError.OutOfMemory;
                return false;
            }
            catch
            {
                DisposeBuilders(allocated);
                error = GpuUploadError.InternalError;
                return false;
            }
        }

        private static void DisposeBuilders(BatchBuilder[] value)
        {
            if (value == null) return;
            for (int i = 0; i < value.Length; i++)
                if (value[i]?.blob.IsCreated == true) value[i].blob.Dispose();
        }

        private static void TrackSubmitted(BatchBuilder builder)
        {
            builder.submittedIndex = submittedBuilderCount;
            submittedBuilders[submittedBuilderCount++] = builder;
        }

        private static void UntrackSubmitted(BatchBuilder builder)
        {
            int index = builder.submittedIndex;
            if (index < 0 || index >= submittedBuilderCount
                || !ReferenceEquals(submittedBuilders[index], builder)) return;
            int last = --submittedBuilderCount;
            var moved = submittedBuilders[last];
            submittedBuilders[last] = null;
            if (index != last)
            {
                submittedBuilders[index] = moved;
                moved.submittedIndex = index;
            }
            builder.submittedIndex = -1;
        }

        private static void StoreStatus(int historyIndex, in GpuUploadTicket ticket,
            in GpuUploadStatus status)
        {
            if (history == null || historyIndex < 0 || historyIndex >= history.Length) return;
            ref var entry = ref history[historyIndex];
            bool readObserved = entry.readObserved && entry.serial == ticket.serial
                                                   && entry.epoch == ticket.epoch;
            entry = new HistoryEntry
            {
                epoch = ticket.epoch,
                serial = ticket.serial,
                status = status,
                readObserved = readObserved
            };
        }

        private static int ReserveHistory()
        {
            if (historyNext == null || historyHead < 0 && !TryGrowHistory()) return -1;
            int index = historyHead;
            historyHead = historyNext[index];
            if (historyHead < 0) historyTail = -1;
            historyNext[index] = ReservedHistory;
            return index;
        }

        private static bool TryGrowHistory()
        {
            int length = historyNext.Length;
            if (length > int.MaxValue / 2) return false;
            try
            {
                var grownHistory = new HistoryEntry[length * 2];
                var grownNext = new int[length * 2];
                Array.Copy(history, grownHistory, length);
                Array.Copy(historyNext, grownNext, length);
                for (int i = length; i < grownNext.Length - 1; i++) grownNext[i] = i + 1;
                grownNext[grownNext.Length - 1] = -1;
                history = grownHistory;
                historyNext = grownNext;
                historyHead = length;
                historyTail = grownNext.Length - 1;
                return true;
            }
            catch (OutOfMemoryException)
            {
                return false;
            }
        }

        private static void DetachHistory(int index)
        {
            if (historyNext == null || index < 0 || index >= historyNext.Length
                || historyNext[index] != ReservedHistory) return;
            if (history[index].readObserved) ReleaseHistory(index);
            else historyNext[index] = DetachedHistory;
        }

        private static void ReleaseHistory(int index)
        {
            if (historyNext == null || index < 0 || index >= historyNext.Length
                || historyNext[index] != ReservedHistory
                && historyNext[index] != DetachedHistory) return;
            historyNext[index] = -1;
            if (historyTail < 0) historyHead = index;
            else historyNext[historyTail] = index;
            historyTail = index;
        }

        private static bool TryGetRetainedStatus(in GpuUploadTicket ticket,
            out GpuUploadStatus status)
        {
            status = default;
            if (history == null || ticket.sessionGeneration != sessionGeneration) return false;
            int index = ticket.historyIndex;
            if (index < 0 || index >= history.Length) return false;
            ref var entry = ref history[index];
            if (entry.serial != ticket.serial || entry.epoch != ticket.epoch) return false;
            status = entry.status;
            if (status.State != GpuUploadSubmissionState.Pending
                && status.GpuState != GpuUploadGpuState.Pending)
            {
                entry.readObserved = true;
                if (historyNext[index] == DetachedHistory) ReleaseHistory(index);
            }
            return true;
        }

        private static bool TryGetSubmittedBuilder(in GpuUploadTicket ticket,
            out BatchBuilder builder)
        {
            int index = ticket.builder;
            if (builders != null && ticket.sessionGeneration == sessionGeneration
                && index >= 0 && index < builders.Length)
            {
                builder = builders[index];
                if (builder != null && builder.state == BuilderState.Submitted
                    && builder.generation == ticket.builderGeneration
                    && builder.serial == ticket.serial)
                    return true;
            }
            builder = null;
            return false;
        }

        private static void PrepareSessionForDeviceEpoch()
        {
            submittedBuilderCount = 0;
            if (submittedBuilders != null)
                Array.Clear(submittedBuilders, 0, submittedBuilders.Length);
            if (builders == null) return;
            for (int i = 0; i < builders.Length; i++)
            {
                var builder = builders[i];
                if (builder == null || builder.state == BuilderState.Free) continue;
                FreeBuilder(builder);
            }
            for (int i = 0; i < historyNext.Length; i++)
                if (historyNext[i] == DetachedHistory) ReleaseHistory(i);
        }

        private static void CleanupSession()
        {
            submittedBuilderCount = 0;
            if (builders != null)
            {
                for (int i = 0; i < builders.Length; i++)
                {
                    var builder = builders[i];
                    if (builder == null) continue;
                    if (builder.state != BuilderState.Free) FreeBuilder(builder);
                    if (builder.blob.IsCreated) builder.blob.Dispose();
                }
            }
            builders = null;
            freeBuilders = null;
            freeBuilderCount = 0;
            submittedBuilders = null;
            history = null;
            historyNext = null;
            historyHead = 0;
            historyTail = -1;
        }
    }
}
