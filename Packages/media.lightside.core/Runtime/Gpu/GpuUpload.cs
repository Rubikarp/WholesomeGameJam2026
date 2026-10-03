using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>
    /// Records validated regional texture uploads through the native v6 backends
    /// or the synchronous WebGL2 backend. Upload memory is acquired as native-owned slots;
    /// command placement, mips, and publication policy remain explicit consumer decisions.
    /// Device, slot, target, batch, and pump calls are main-thread operations; jobs may fill
    /// an acquired slot view before it is submitted.
    /// </summary>
    public static partial class GpuUpload
    {
        private static readonly CatZone zone = Cat.Zone("GpuUpload");
        private const uint KnownCapabilityMask = ((1u << 9) - 1) & ~(1u << 3);

        private enum BuilderState : byte
        {
            Free,
            Building,
            Submitted,
            Abandoned
        }

        private sealed class BatchBuilder
        {
            internal NativeArray<byte> blob;
            internal GpuUploadTarget[] targets;
            internal GpuUploadAbi.TargetHandle[] targetHandles;
            internal Dictionary<GpuUploadTarget, int> targetIndices;
            internal ulong[] regionEnds;
            internal BuilderState state;
            internal ulong generation;
            internal int targetCount;
            internal int regionCount;
            internal int regionLimit;
            internal int regionTableOffset;
            internal IntPtr routeKey;
            internal ulong serial;
            internal bool closing;
            internal bool updateCountPublished;
            internal bool updateCountCallerManaged;
            internal uint batchFlags;
            internal bool publicationMarkerAdded;
            internal GpuUploadSequence sequence;
            internal uint sequenceOrdinal;
            internal bool retireQueued;
            internal int freeIndex;
            internal int submittedIndex = -1;
            internal int historyIndex = -1;
        }

        private struct HistoryEntry
        {
            internal ulong epoch;
            internal ulong serial;
            internal GpuUploadStatus status;
            internal bool readObserved;
        }

        private static bool initialized;
        private static bool supported;
        private static bool bindingUnavailable;
        private static bool sessionCorrupted;
        private static bool automaticPumpFailureLogged;
        private static GpuUploadDeviceInfo deviceInfo;
        private static GpuUploadError availabilityError = GpuUploadError.NotInitialized;
        private static string bindingFailureDetail;
        private static bool drainNativeOrphans;
        private static readonly List<GpuUploadTarget> targets = new();
        private static CommandBuffer immediateCommandBuffer;
        private static CommandBuffer retireCommandBuffer;
        private static IntPtr uploadEvent;
        private static IntPtr boundaryEvent;
        private static IntPtr retireEvent;
        private static IntPtr pollEvent;
        private static int eventBase;
        private static ulong immediateSerial;
        private static ulong handleGeneration;
        private static ulong sessionGeneration = 1;
        private static int lastEpochCheckFrame = -1;
        private static bool CanCallNative => initialized && !bindingUnavailable
                                                         && !sessionCorrupted
                                                         && deviceInfo.GraphicsDeviceEpoch != 0;
        private static bool CanAdmitNewWork => supported && CanCallNative;
        private static GpuUploadError AdmissionError => availabilityError == GpuUploadError.None
            ? GpuUploadError.InternalError
            : availabilityError;

        static GpuUpload()
        {
            CoreLoop.BeforeUpdating += PumpAutomatically;
#if UNITY_EDITOR
            EditorLifecycle.UnmanagedCleaning += ResetSession;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime()
        {
            DeviceEpochChanged = null;
            ResetSession();
        }

        /// <summary>Occurs after a new graphics-device epoch invalidates every registered target.</summary>
        public static event Action<ulong> DeviceEpochChanged;

        /// <summary>Last observed availability of a complete ABI-v6 upload implementation.</summary>
        public static bool IsSupported => supported;

        /// <summary>Last observed capabilities and limits of the graphics-device epoch.</summary>
        public static GpuUploadDeviceInfo Info => deviceInfo;

        /// <summary>
        /// Latest graphics-device availability result; <see cref="GpuUploadError.None"/> means
        /// the observed session accepts new work.
        /// </summary>
        public static GpuUploadError AvailabilityError => availabilityError;

        /// <summary>Formats an error for failure surfaces; only <see cref="GpuUploadError.UnsupportedBackend"/> expands with the recorded native-binding failure cause, when one produced it.</summary>
        internal static string Describe(GpuUploadError error) =>
            error == GpuUploadError.UnsupportedBackend && bindingFailureDetail != null
                ? $"{error}: {bindingFailureDetail}"
                : error.ToString();

        private static void RecordBindingFailure(Exception exception) =>
            bindingFailureDetail ??= exception switch
            {
                DllNotFoundException =>
                    $"DllNotFoundException — the operating system refused to load '{GpuUploadAbi.LibName}'; Editor.log records the OS reason under \"Plugins: Failed to load\" (security policies such as Windows Smart App Control block the load while leaving the file on disk)",
                EntryPointNotFoundException =>
                    $"EntryPointNotFoundException — a stale '{GpuUploadAbi.LibName}' native library is loaded in this process; restart the Unity Editor",
                _ =>
                    $"BadImageFormatException — the '{GpuUploadAbi.LibName}' native library is corrupted or has a mismatched architecture; reinstall the package"
            };

        /// <summary>Checks color-texture support for one exact Unity format and dimension.</summary>
        public static bool Supports(GraphicsFormat format, TextureDimension dimension)
            => Supports(format, dimension, GpuUploadResourceKind.Texture, GpuUploadAspect.Color);

        /// <summary>Checks one exact format, dimension, native resource, and aspect combination.</summary>
        public static bool Supports(GraphicsFormat format, TextureDimension dimension,
            GpuUploadResourceKind resourceKind, GpuUploadAspect aspect)
        {
            var query = GpuUploadSupportQuery.ForFormat(format, dimension, resourceKind, aspect);
            return TryQuerySupport(query, out var supportInfo, out _)
                   && supportInfo.Supported;
        }

        /// <summary>Checks one exact stable format, dimension, native resource, and aspect combination.</summary>
        public static bool Supports(GpuUploadFormat format, GpuUploadDimension dimension,
            GpuUploadResourceKind resourceKind = GpuUploadResourceKind.Texture,
            GpuUploadAspect aspect = GpuUploadAspect.Color) =>
            TryQuerySupport(format, dimension, resourceKind, aspect,
                out var supportInfo, out _) && supportInfo.Supported;

        /// <summary>
        /// Registers one immutable Unity texture storage generation and acquires its native
        /// lifetime. The registered mip count is the declared upload window: regions may only
        /// address levels below it, and the physical resource must be able to hold it — it may
        /// carry more levels (Unity realizes truncated-mip RenderTextures on Vulkan/Metal as a
        /// full chain plus a sampler max-LOD clamp while reporting the declared count). Only
        /// explicitly submitted mip levels are changed; RenderTexture automatic mip generation
        /// remains consumer-controlled.
        /// </summary>
        public static bool TryRegister(Texture texture, out GpuUploadTarget target,
            out GpuUploadError error)
        {
            target = null;
            if (!TrySelectPrimaryAspect(texture, out var aspect, out error)) return false;
            return TryRegister(texture, aspect, out target, out error);
        }

        /// <summary>
        /// Registers the color or depth/stencil native resource selected by an explicit aspect.
        /// A combined depth/stencil registration may subsequently address either supported aspect.
        /// </summary>
        public static bool TryRegister(Texture texture, GpuUploadAspect aspect,
            out GpuUploadTarget target, out GpuUploadError error)
        {
            target = null;
            Initialize();
            Pump();
            if (!CanAdmitNewWork)
            {
                error = AdmissionError;
                return false;
            }
            if (!TryBuildRegistration(texture, aspect, out var info, out uint flags,
                    out var supports, out var registration, out error))
                return false;
            for (int i = 0; i < targets.Count; i++)
            {
                var existing = targets[i];
                if (existing != null && ReferenceEquals(existing.texture, texture)
                                     && existing.info.ResourceKind == info.ResourceKind
                                     && (existing.state == GpuUploadTargetState.Active
                                         || existing.state == GpuUploadTargetState.Closing))
                {
                    error = GpuUploadError.TargetBusy;
                    return false;
                }
            }
            GpuUploadTarget candidate;
            try
            {
                int requiredCapacity = checked(targets.Count + 1);
                if (targets.Capacity < requiredCapacity)
                {
                    int grownCapacity = targets.Capacity == 0
                        ? 4
                        : checked(targets.Capacity * 2);
                    targets.Capacity = Math.Max(requiredCapacity, grownCapacity);
                }
                candidate = new GpuUploadTarget(texture, info, default,
                    deviceInfo.GraphicsDeviceEpoch, flags, supports);
            }
            catch (Exception exception) when (exception is OutOfMemoryException
                                               || exception is OverflowException)
            {
                error = GpuUploadError.OutOfMemory;
                return false;
            }
            error = GpuUploadAbi.RegisterTarget(ref registration, out var handle,
                out bool contractViolation);
            if (contractViolation)
            {
                candidate.handle = handle;
                candidate.closeRequested = true;
                targets.Add(candidate);
                DisableCorruptedSession(error);
                return false;
            }
            if (error != GpuUploadError.None)
            {
                ObserveBackendError(error);
                return false;
            }
            candidate.handle = handle;
            targets.Add(candidate);
            target = candidate;
            return true;
        }

        /// <summary>
        /// Queues a submission boundary and completion poll without waiting for GPU completion.
        /// A successful return means progress was requested, not that capacity is already available;
        /// retry the operation that reported backpressure according to consumer policy.
        /// </summary>
        /// <param name="error">
        /// The exact session or capability failure, or <see cref="GpuUploadError.None"/> on success.
        /// </param>
        /// <returns>Whether the progress request was queued and the session remains usable.</returns>
        public static bool TryRequestProgress(out GpuUploadError error)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Pump();
#else
            if (!GpuSubmitBoundary.TryInsert())
            {
                error = CanAdmitNewWork
                    ? GpuUploadError.UnsupportedFeature
                    : AdmissionError;
                return false;
            }
            Pump();
#endif
            if (!CanAdmitNewWork)
            {
                error = AdmissionError;
                return false;
            }
            error = GpuUploadError.None;
            return true;
        }

        /// <summary>Polls submissions, publishes texture update counts, and retires targets and slots.</summary>
        public static void Pump()
        {
            Initialize();
            CheckDeviceEpoch();
            if (!CanCallNative) return;
            bool needsPoll = false;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            PollNativeOrphans(ref needsPoll);
            if (!CanCallNative) return;
            for (int i = 0; i < submittedBuilderCount;)
            {
                var builder = submittedBuilders[i];
                PollSubmission(builder, ref needsPoll);
                if (!CanCallNative) return;
                if (i < submittedBuilderCount && ReferenceEquals(submittedBuilders[i], builder)) i++;
            }
#endif
            needsPoll |= PruneTargets();
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (CanCallNative && needsPoll && pollEvent != IntPtr.Zero
                          && (deviceInfo.Capabilities & GpuUploadCapabilities.CompletionPoll) != 0)
            {
                try
                {
                    GL.IssuePluginEvent(pollEvent, eventBase + GpuUploadAbi.PollEvent);
                }
                catch
                {
                    DisableCorruptedSession(GpuUploadError.BackendFailed);
                }
            }
#endif
        }

        internal static void PumpAutomatically()
        {
            if (!ShouldPumpAutomatically()) return;
            try
            {
                Pump();
            }
            catch (Exception exception)
            {
                if (!automaticPumpFailureLogged)
                {
                    automaticPumpFailureLogged = true;
                    zone.MeowError($"[GpuUpload] Automatic pump failed: {exception}");
                }
                try
                {
                    DisableCorruptedSession(GpuUploadError.BackendFailed);
                }
                catch
                {
                    sessionCorrupted = true;
                    supported = false;
                    bindingUnavailable = true;
                    availabilityError = GpuUploadError.BackendFailed;
                    PreserveUnprovenSession();
                }
            }
        }

        private static bool ShouldPumpAutomatically()
        {
            if (!initialized || bindingUnavailable || sessionCorrupted) return false;
            return targets.Count != 0 || submittedBuilderCount != 0 || drainNativeOrphans;
        }

#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private static void PollNativeOrphans(ref bool needsPoll)
        {
            if (!drainNativeOrphans) return;
            var stats = new GpuUploadAbi.Stats();
            var error = GpuUploadAbi.GetStats(ref stats);
            if (error != GpuUploadError.None)
            {
                ObserveBackendError(error);
                return;
            }
            if (HasInvalidStats(stats))
            {
                ObserveBackendError(GpuUploadError.AbiMismatch);
                return;
            }
            if (stats.poolNodesInFlight == 0)
            {
                drainNativeOrphans = false;
                return;
            }
            needsPoll = true;
        }
#endif

        private static void PollSubmission(BatchBuilder builder, ref bool needsPoll)
        {
            if (!CanCallNative || builder == null || builder.state != BuilderState.Submitted
                               || builder.serial == 0) return;
            if (builder.closing && !builder.retireQueued)
            {
                TryQueueCloseAndRetire(builder);
                if (!CanCallNative || builder.state != BuilderState.Submitted) return;
            }
            var nativeStatus = new GpuUploadAbi.SubmissionStatus();
            var error = GpuUploadAbi.GetSubmissionStatus(builder.serial, ref nativeStatus);
            if (error != GpuUploadError.None)
            {
                if (error == GpuUploadError.SubmissionNotFound)
                {
                    DisableCorruptedSession(GpuUploadError.SubmissionNotFound);
                    return;
                }
                ObserveBackendError(error);
                return;
            }
            if (!TryParseSubmissionStatus(nativeStatus, builder.serial,
                    deviceInfo.GraphicsDeviceEpoch, builder.regionCount,
                    out var status))
                return;
            if (status.GpuState == GpuUploadGpuState.Pending) needsPoll = true;
            ProcessStatus(builder, status);
        }

        private static bool PruneTargets()
        {
            bool closing = false;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                var target = targets[i];
                if (target == null || target.state == GpuUploadTargetState.Retired
                                   || target.state == GpuUploadTargetState.Stale)
                {
                    targets.RemoveAt(i);
                    continue;
                }
                if (target.closeRequested && target.state == GpuUploadTargetState.Active)
                {
                    CloseTarget(target);
                    if (!CanCallNative) return closing;
                }
                if (target.state == GpuUploadTargetState.Closing)
                {
                    RefreshTargetState(target);
                    if (!CanCallNative) return closing;
                    if (target.state == GpuUploadTargetState.Retired)
                        targets.RemoveAt(i);
                    else if (target.state == GpuUploadTargetState.Closing)
                        closing = true;
                }
            }
            return closing;
        }

        /// <summary>Reads the latest retained result for a submission serial.</summary>
        public static bool TryGetStatus(in GpuUploadTicket ticket, out GpuUploadStatus status)
        {
            status = default;
            if (!ticket.IsValid || !TryGetRetainedStatus(ticket, out status)) return false;
            if (ticket.epoch != deviceInfo.GraphicsDeviceEpoch)
                return false;
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            bool terminal = status.State != GpuUploadSubmissionState.Pending
                            && status.GpuState != GpuUploadGpuState.Pending;
            if (terminal)
            {
                if (status.RetireObserved) return true;
                if (!TryGetSubmittedBuilder(ticket, out var retainedBuilder)
                    || !retainedBuilder.closing)
                    return true;
            }
            if (!CanCallNative) return true;
            CheckDeviceEpoch();
            if (ticket.sessionGeneration != sessionGeneration
                || ticket.epoch != deviceInfo.GraphicsDeviceEpoch)
                return false;
            if (!CanCallNative) return true;
            if (!TryGetSubmittedBuilder(ticket, out var activeBuilder))
            {
                if (terminal) return true;
                DisableCorruptedSession();
                return false;
            }
            var nativeStatus = new GpuUploadAbi.SubmissionStatus();
            var error = GpuUploadAbi.GetSubmissionStatus(ticket.serial, ref nativeStatus);
            if (error != GpuUploadError.None)
            {
                if (error == GpuUploadError.SubmissionNotFound)
                {
                    if (status.State == GpuUploadSubmissionState.Pending)
                        DisableCorruptedSession(GpuUploadError.SubmissionNotFound);
                    return false;
                }
                ObserveBackendError(error);
                return false;
            }
            if (!TryParseSubmissionStatus(nativeStatus, ticket.serial, ticket.epoch,
                    activeBuilder.regionCount,
                    out status))
                return false;
            ProcessStatus(activeBuilder, status);
            return true;
#endif
        }

        /// <summary>Reads aggregate native slot-ring and submission counters without resetting them.</summary>
        public static bool TryGetStats(out GpuUploadStats stats, out GpuUploadError error)
        {
            stats = default;
            Initialize();
            CheckDeviceEpoch();
            if (!CanCallNative)
            {
                error = AdmissionError;
                return false;
            }
            var nativeStats = new GpuUploadAbi.Stats();
            error = GpuUploadAbi.GetStats(ref nativeStats);
            if (error != GpuUploadError.None)
            {
                ObserveBackendError(error);
                return false;
            }
            if (HasInvalidStats(nativeStats))
            {
                error = GpuUploadError.AbiMismatch;
                ObserveBackendError(error);
                return false;
            }
            stats = new GpuUploadStats(nativeStats);
            return true;
        }

        internal static bool IsBatchBuilding(in GpuUploadBatch batch) =>
            TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building, out _);

        internal static unsafe bool TryAddRegion(in GpuUploadBatch batch, GpuUploadTarget target,
            in GpuUploadRegion region, out GpuUploadError error)
        {
            if (!CanAdmitNewWork)
            {
                error = AdmissionError;
                return false;
            }
            if (!TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building,
                    out var builder)
                || target == null)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            if (builder.publicationMarkerAdded)
            {
                error = GpuUploadError.InvalidLayout;
                return false;
            }
            if (target.state == GpuUploadTargetState.Stale
                || target.epoch != deviceInfo.GraphicsDeviceEpoch)
            {
                error = GpuUploadError.TargetStale;
                return false;
            }
            if (target.closeRequested || target.state != GpuUploadTargetState.Active)
            {
                error = GpuUploadError.TargetClosing;
                return false;
            }
            if (builder.regionCount >= builder.regionLimit)
            {
                error = GpuUploadError.Backpressure;
                return false;
            }
            if (region.SlotOffset > int.MaxValue)
            {
                error = GpuUploadError.SourceOutOfRange;
                return false;
            }
            if (!TryValidateRegion(target, region, out ulong spanBytes, out error))
                return false;
            if (!builder.targetIndices.TryGetValue(target, out int targetIndex))
            {
                if (builder.targetCount > 0
                    && (deviceInfo.Capabilities & GpuUploadCapabilities.MultiTarget) == 0)
                {
                    error = GpuUploadError.UnsupportedFeature;
                    return false;
                }
                if (builder.targetCount >= builder.targets.Length)
                {
                    error = GpuUploadError.Backpressure;
                    return false;
                }
                targetIndex = builder.targetCount;
                builder.targetIndices.Add(target, targetIndex);
                byte* basePointer = (byte*)builder.blob.GetUnsafePtr();
                if (builder.regionCount > 0)
                    UnsafeUtility.MemMove(
                        basePointer + builder.regionTableOffset + GpuUploadAbi.TargetSize,
                        basePointer + builder.regionTableOffset,
                        builder.regionCount * GpuUploadAbi.RegionSize);
                builder.regionTableOffset += GpuUploadAbi.TargetSize;
                builder.targetCount = targetIndex + 1;
                builder.targets[targetIndex] = target;
                builder.targetHandles[targetIndex] = target.handle;
                var nativeTargets = (GpuUploadAbi.Target*)(basePointer + GpuUploadAbi.BatchSize);
                var info = target.info;
                nativeTargets[targetIndex] = new GpuUploadAbi.Target
                {
                    token = target.handle.token,
                    generation = target.handle.generation,
                    width = (uint)info.Width,
                    height = (uint)info.Height,
                    depth = (uint)info.Depth,
                    layers = (uint)info.Layers,
                    mipCount = (uint)info.MipCount,
                    format = (uint)info.Format,
                    dimension = (uint)info.Dimension,
                    sampleCount = (uint)info.SampleCount,
                    flags = target.abiFlags
                };
            }
            else if (builder.targetHandles[targetIndex].token != target.handle.token
                     || builder.targetHandles[targetIndex].generation != target.handle.generation)
            {
                error = GpuUploadError.TargetStale;
                return false;
            }
            byte* blobPointer = (byte*)builder.blob.GetUnsafePtr();
            var nativeRegions = (GpuUploadAbi.Region*)(blobPointer + builder.regionTableOffset);
            builder.regionEnds[builder.regionCount] = (ulong)region.SlotOffset + spanBytes;
            nativeRegions[builder.regionCount++] = new GpuUploadAbi.Region
            {
                targetIndex = (uint)targetIndex,
                mipLevel = (uint)region.MipLevel,
                aspect = (uint)region.Aspect,
                destinationX = region.DestinationX,
                destinationY = region.DestinationY,
                destinationZ = region.DestinationZ,
                width = (uint)region.Width,
                height = (uint)region.Height,
                depth = (uint)region.Depth,
                baseLayer = (uint)region.BaseLayer,
                layerCount = (uint)region.LayerCount,
                slotOffset = checked((uint)region.SlotOffset),
                sourceRowPitch = (uint)region.SourceRowPitch,
                sourceImagePitch = (uint)region.SourceImagePitch,
                sourceLayerPitch = (uint)region.SourceLayerPitch
            };
            error = GpuUploadError.None;
            return true;
        }

        internal static GpuUploadSubmitResult Submit(in GpuUploadBatch batch,
            ref GpuUploadSlot slot, int writtenBytes)
        {
            if (!TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building,
                    out var builder))
            {
                var staleAdmission = StaleBatchAdmission(batch);
                if (staleAdmission == GpuUploadAdmission.SessionAbandoned)
                    ReleaseAndConsumeSlot(ref slot);
                return new GpuUploadSubmitResult(staleAdmission, default,
                    GpuUploadContentState.Unchanged, GpuUploadError.InvalidArgument);
            }
            Initialize();
            CheckDeviceEpoch();
            if (!TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building,
                    out builder))
            {
                ReleaseAndConsumeSlot(ref slot);
                return new GpuUploadSubmitResult(GpuUploadAdmission.SessionAbandoned, default,
                    GpuUploadContentState.Unchanged,
                    availabilityError == GpuUploadError.None
                        ? GpuUploadError.DeviceLost : availabilityError);
            }
            if (!CanAdmitNewWork)
                return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadContentState.Unchanged, AdmissionError);
            if (!IsSlotAcquired(slot))
                return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadContentState.Unchanged, GpuUploadError.SlotNotFound,
                    GpuUploadAdmissionStage.Slot);
            if (writtenBytes < 0 || writtenBytes > slot.Capacity)
                return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadContentState.Unchanged, GpuUploadError.SourceOutOfRange,
                    GpuUploadAdmissionStage.Slot);
            for (int i = 0; i < builder.regionCount; i++)
            {
                if (builder.regionEnds[i] > (ulong)writtenBytes)
                    return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                        GpuUploadContentState.Unchanged, GpuUploadError.SourceOutOfRange,
                        GpuUploadAdmissionStage.Region, i);
            }
            if (!TrySerialize(builder, slot.id, slot.generation, (uint)writtenBytes,
                    out int bytes, out var error))
                return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadContentState.Unchanged, error);

#if UNITY_WEBGL && !UNITY_EDITOR
            ulong serial = NextImmediateSerial();
            if (serial == 0)
                return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadContentState.Unchanged, GpuUploadError.InternalError);
            unsafe
            {
                var header = (GpuUploadAbi.Batch*)builder.blob.GetUnsafePtr();
                header->serial = serial;
                builder.serial = serial;
                var callError = GpuUploadAbi.ExecuteWebGlBatch((IntPtr)header, bytes);
                if (!TryParseWebGlResult(header, builder, bytes, serial, slot.id,
                        slot.generation, (uint)writtenBytes, callError,
                        out var status, out var stage))
                {
                    PublishUpdateCounts(builder);
                    builder.state = BuilderState.Abandoned;
                    ReleaseAndConsumeSlot(ref slot);
                    DisableCorruptedSession(GpuUploadError.AbiMismatch);
                    return new GpuUploadSubmitResult(GpuUploadAdmission.SessionAbandoned, default,
                        GpuUploadContentState.MayHaveChanged, GpuUploadError.AbiMismatch);
                }
                if (status.Error != GpuUploadError.None
                    || status.State != GpuUploadSubmissionState.Encoded)
                {
                    if (status.ContentState != GpuUploadContentState.Unchanged)
                    {
                        PublishUpdateCounts(builder);
                        builder.updateCountPublished = false;
                    }
                    if (status.State != GpuUploadSubmissionState.Rejected)
                        ReleaseAndConsumeSlot(ref slot);
                    ObserveBackendError(status.Error);
                    if (builder.state != BuilderState.Building)
                    {
                        ReleaseAndConsumeSlot(ref slot);
                        return new GpuUploadSubmitResult(
                            GpuUploadAdmission.SessionAbandoned, default,
                            status.ContentState, status.Error, stage, status.FailedRegion);
                    }
                    return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                        status.ContentState, status.Error, stage, status.FailedRegion);
                }
                builder.historyIndex = ReserveHistory();
                if (builder.historyIndex < 0)
                {
                    PublishUpdateCounts(builder);
                    builder.state = BuilderState.Abandoned;
                    ConsumeSlot(ref slot);
                    DisableCorruptedSession();
                    return new GpuUploadSubmitResult(GpuUploadAdmission.SessionAbandoned, default,
                        status.ContentState, GpuUploadError.InternalError);
                }
                var ticket = new GpuUploadTicket(sessionGeneration,
                    deviceInfo.GraphicsDeviceEpoch, serial, builder.historyIndex,
                    builder.freeIndex, builder.generation);
                ConsumeSlot(ref slot);
                ProcessStatus(builder, status);
                return new GpuUploadSubmitResult(GpuUploadAdmission.Admitted, ticket,
                    status.ContentState, GpuUploadError.None);
            }
#else
            if (!TryCreateSubmission(builder, bytes, out var ticket, out error,
                    out var admission))
            {
                if (admission == GpuUploadAdmission.SessionAbandoned)
                {
                    ReleaseAndConsumeSlot(ref slot);
                    return new GpuUploadSubmitResult(admission, default,
                        GpuUploadContentState.Unchanged, error);
                }
                if (!TryReadRejection(builder, error, out var stage, out int failedRegion))
                {
                    ObserveBackendError(GpuUploadError.AbiMismatch);
                    return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                        GpuUploadContentState.Unchanged, GpuUploadError.AbiMismatch);
                }
                if (stage == GpuUploadAdmissionStage.Slot
                    && error == GpuUploadError.SlotNotFound)
                    ConsumeSlot(ref slot);
                return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadContentState.Unchanged, error, stage, failedRegion);
            }
            ConsumeSlot(ref slot);
            try
            {
                immediateCommandBuffer.Clear();
                immediateCommandBuffer.IssuePluginEventAndData(uploadEvent,
                    eventBase + GpuUploadAbi.UploadEvent, builder.routeKey);
                immediateCommandBuffer.IssuePluginEventAndData(retireEvent,
                    eventBase + GpuUploadAbi.RetireEvent, builder.routeKey);
                builder.closing = true;
                builder.retireQueued = true;
                Graphics.ExecuteCommandBuffer(immediateCommandBuffer);
                PublishUpdateCounts(builder);
                error = GpuUploadError.None;
            }
            catch
            {
                builder.closing = true;
                TryQueueCloseAndRetire(builder);
                if (builder.state == BuilderState.Submitted && !builder.updateCountPublished)
                    PublishUpdateCounts(builder);
                error = GpuUploadError.BackendFailed;
                if (!CanCallNative)
                    return new GpuUploadSubmitResult(GpuUploadAdmission.SessionAbandoned,
                        default, GpuUploadContentState.MayHaveChanged, AdmissionError);
            }
            return new GpuUploadSubmitResult(GpuUploadAdmission.Admitted, ticket,
                GpuUploadContentState.MayHaveChanged, error);
#endif
        }

        internal static GpuUploadRecordResult RecordOnce(in GpuUploadBatch batch,
            ref GpuUploadSlot slot, int writtenBytes, CommandBuffer commandBuffer,
            GpuUploadRecordOptions options)
        {
            if (!TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building,
                    out var builder))
            {
                var staleAdmission = StaleBatchAdmission(batch);
                if (staleAdmission == GpuUploadAdmission.SessionAbandoned)
                    ReleaseAndConsumeSlot(ref slot);
                return new GpuUploadRecordResult(0, 0,
                    staleAdmission, default,
                    GpuUploadError.InvalidArgument);
            }
            if (commandBuffer == null)
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadError.InvalidArgument);
            if ((options & ~GpuUploadRecordOptions.CallerManagedUpdateCount) != 0)
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadError.InvalidArgument);
#if UNITY_WEBGL && !UNITY_EDITOR
            return new GpuUploadRecordResult(0, 0,
                GpuUploadAdmission.NotAdmitted, default,
                GpuUploadError.UnsupportedFeature);
#else
            Initialize();
            CheckDeviceEpoch();
            if (!TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building,
                    out builder))
            {
                ReleaseAndConsumeSlot(ref slot);
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.SessionAbandoned, default,
                    availabilityError == GpuUploadError.None
                        ? GpuUploadError.DeviceLost : availabilityError);
            }
            if (!CanAdmitNewWork)
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.NotAdmitted, default,
                    AdmissionError);
            if ((deviceInfo.Capabilities & GpuUploadCapabilities.CommandBuffer) == 0)
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadError.UnsupportedFeature);
            if (!IsSlotAcquired(slot))
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadError.SlotNotFound);
            if (writtenBytes < 0 || writtenBytes > slot.Capacity)
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadError.SourceOutOfRange);
            for (int i = 0; i < builder.regionCount; i++)
            {
                if (builder.regionEnds[i] > (ulong)writtenBytes)
                    return new GpuUploadRecordResult(0, 0,
                        GpuUploadAdmission.NotAdmitted, default,
                        GpuUploadError.SourceOutOfRange);
            }
            if (!TrySerialize(builder, slot.id, slot.generation, (uint)writtenBytes,
                    out int bytes, out var error))
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.NotAdmitted, default, error);
            if (options == GpuUploadRecordOptions.None && !HasOnlyRenderTextureTargets(builder))
            {
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadError.UnsupportedTexture);
            }
            if (!TryCreateSubmission(builder, bytes, out var ticket, out error,
                    out var admission))
            {
                if (admission == GpuUploadAdmission.SessionAbandoned)
                    ReleaseAndConsumeSlot(ref slot);
                return new GpuUploadRecordResult(0, 0,
                    admission, default, error);
            }
            ConsumeSlot(ref slot);
            builder.updateCountCallerManaged =
                (options & GpuUploadRecordOptions.CallerManagedUpdateCount) != 0;
            try
            {
                commandBuffer.IssuePluginEventAndData(uploadEvent,
                    eventBase + GpuUploadAbi.UploadEvent, builder.routeKey);
                if (!builder.updateCountCallerManaged)
                    RecordUpdateCounts(commandBuffer, builder);
                error = GpuUploadError.None;
            }
            catch
            {
                error = GpuUploadError.BackendFailed;
            }
            return new GpuUploadRecordResult(batch.builder, batch.generation,
                GpuUploadAdmission.Admitted, ticket, error);
#endif
        }

        internal static void ReleaseBatch(in GpuUploadBatch batch)
        {
            if (!TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building,
                    out var builder)) return;
            FreeBuilder(builder);
        }

        internal static bool IsRecordingValid(in GpuUploadRecordResult recording) =>
            TryGetBatchBuilder(recording.builder, recording.generation,
                BuilderState.Submitted, out _);

        internal static bool IsRecordingClosing(in GpuUploadRecordResult recording) =>
            TryGetBatchBuilder(recording.builder, recording.generation,
                BuilderState.Submitted, out var builder)
            && builder.closing;

        internal static bool TryPublishRecordingUpdateCounts(
            in GpuUploadRecordResult recording)
        {
            if (!TryGetBatchBuilder(recording.builder, recording.generation,
                    BuilderState.Submitted, out var builder)
                || !builder.updateCountCallerManaged || builder.updateCountPublished)
                return false;
            PublishUpdateCounts(builder);
            return true;
        }

        internal static void CloseRecording(in GpuUploadRecordResult recording)
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (!TryGetBatchBuilder(recording.builder, recording.generation,
                    BuilderState.Submitted, out var builder)
                || builder.closing)
                return;
            if (builder.submittedIndex < 0) TrackSubmitted(builder);
            builder.closing = true;
            if (!CanCallNative) return;
            CheckDeviceEpoch();
            if (!CanCallNative || !TryGetBatchBuilder(recording.builder,
                    recording.generation, BuilderState.Submitted, out builder))
                return;
            TryQueueCloseAndRetire(builder);
#endif
        }

        internal static bool TryRefreshTarget(GpuUploadTarget target, out GpuUploadError error)
        {
            if (target == null)
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
            if (target.state == GpuUploadTargetState.Stale
                || target.epoch != deviceInfo.GraphicsDeviceEpoch)
            {
                target.state = GpuUploadTargetState.Stale;
                error = GpuUploadError.TargetStale;
                return false;
            }
            if (target.closeRequested || target.state != GpuUploadTargetState.Active)
            {
                error = GpuUploadError.TargetClosing;
                return false;
            }
            if ((deviceInfo.Capabilities & GpuUploadCapabilities.TargetRefresh) == 0)
            {
                error = GpuUploadError.UnsupportedFeature;
                return false;
            }
            if (!TryBuildRegistration(target.texture, target.info.PrimaryAspect,
                    out var info, out uint flags, out var supports,
                    out var registration, out error))
                return false;
            error = GpuUploadAbi.RefreshTarget(ref registration, ref target.handle,
                out bool contractViolation);
            if (contractViolation)
            {
                target.closeRequested = true;
                DisableCorruptedSession(error);
                return false;
            }
            if (error != GpuUploadError.None)
            {
                ObserveBackendError(error);
                if (error == GpuUploadError.TargetStale || error == GpuUploadError.DeviceLost
                                                        || error == GpuUploadError.ContextLost)
                    target.state = GpuUploadTargetState.Stale;
                else if (error == GpuUploadError.TargetNotFound)
                    target.state = GpuUploadTargetState.Retired;
                return false;
            }
            target.info = info;
            target.abiFlags = flags;
            target.supports = supports;
            target.epoch = deviceInfo.GraphicsDeviceEpoch;
            return true;
        }

        internal static void CloseTarget(GpuUploadTarget target)
        {
            if (target == null || target.state == GpuUploadTargetState.Retired
                               || target.state == GpuUploadTargetState.Stale)
                return;
            target.closeRequested = true;
            if (target.state == GpuUploadTargetState.Closing) return;
            if (!CanCallNative) return;
            CheckDeviceEpoch();
            if (!CanCallNative) return;
            if (deviceInfo.GraphicsDeviceEpoch != 0
                && target.epoch != deviceInfo.GraphicsDeviceEpoch)
            {
                target.state = GpuUploadTargetState.Stale;
                return;
            }
            var error = GpuUploadAbi.UnregisterTarget(ref target.handle);
            ObserveBackendError(error);
            if (error == GpuUploadError.None || error == GpuUploadError.TargetClosing
                                                   || error == GpuUploadError.TargetBusy)
            {
                target.state = GpuUploadTargetState.Closing;
                if (CanCallNative) RefreshTargetState(target);
            }
            else if (error == GpuUploadError.TargetNotFound)
                target.state = GpuUploadTargetState.Retired;
            else if (error == GpuUploadError.TargetStale || error == GpuUploadError.DeviceLost
                                                          || error == GpuUploadError.ContextLost)
                target.state = GpuUploadTargetState.Stale;
        }

        internal static bool TryGetBoundaryEvent(out IntPtr callback, out int id)
        {
            Initialize();
            CheckDeviceEpoch();
            callback = boundaryEvent;
            id = eventBase + GpuUploadAbi.BoundaryEvent;
            return CanAdmitNewWork && callback != IntPtr.Zero
                             && (deviceInfo.Capabilities & GpuUploadCapabilities.SubmissionBoundary) != 0;
        }

        private static void Initialize()
        {
            if (initialized) return;
            int frame = Time.frameCount;
            if (lastEpochCheckFrame == frame) return;
            bool firstAttempt = lastEpochCheckFrame < 0;
            initialized = true;
            lastEpochCheckFrame = frame;
            if (!GpuUploadAbi.ContractMatches)
            {
                bindingUnavailable = true;
                availabilityError = GpuUploadError.AbiMismatch;
                zone.MeowError("[GpuUpload] Managed ABI-v6 contract mismatch");
                return;
            }
            try
            {
                var nativeInfo = new GpuUploadAbi.DeviceInfo();
                var error = GpuUploadAbi.GetDeviceInfo(ref nativeInfo);
                if (error != GpuUploadError.None)
                {
                    supported = false;
                    availabilityError = error;
                    ObserveBackendError(error);
                    if (error == GpuUploadError.NotInitialized)
                    {
                        initialized = false;
                        lastEpochCheckFrame = frame;
                    }
                    if (error != GpuUploadError.NotInitialized || firstAttempt)
                        zone.Meow($"[GpuUpload] ABI-v6 unavailable: {error}");
                    return;
                }
                if (!IsValidDeviceInfo(nativeInfo))
                {
                    ObserveBackendError(GpuUploadError.AbiMismatch);
                    zone.MeowError("[GpuUpload] Invalid ABI-v6 device information");
                    return;
                }
                deviceInfo = new GpuUploadDeviceInfo(nativeInfo);
                SetObservedSupport(TryResolveCallbacks(nativeInfo));
#if !(UNITY_WEBGL && !UNITY_EDITOR)
                drainNativeOrphans = supported &&
                    (deviceInfo.Capabilities & GpuUploadCapabilities.CompletionPoll) != 0;
#endif
                if (supported)
                    zone.Meow($"[GpuUpload] ABI v6 initialized on {deviceInfo.Renderer}");
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                               || exception is EntryPointNotFoundException
                                               || exception is BadImageFormatException)
            {
                RecordBindingFailure(exception);
                supported = false;
                bindingUnavailable = true;
                availabilityError = GpuUploadError.UnsupportedBackend;
                zone.Meow($"[GpuUpload] ABI-v6 binding unavailable: {exception.GetType().Name}");
            }
        }

        private static void CheckDeviceEpoch()
        {
            if (!initialized || bindingUnavailable || sessionCorrupted) return;
            int frame = Time.frameCount;
            if (lastEpochCheckFrame == frame) return;
            lastEpochCheckFrame = frame;
            try
            {
                var current = new GpuUploadAbi.DeviceInfo();
                var error = GpuUploadAbi.GetDeviceInfo(ref current);
                if (error != GpuUploadError.None)
                {
                    supported = false;
                    availabilityError = error;
                    ObserveBackendError(error);
                    return;
                }
                if (!IsValidDeviceInfo(current))
                {
                    ObserveBackendError(GpuUploadError.AbiMismatch);
                    return;
                }
                bool epochChanged = deviceInfo.GraphicsDeviceEpoch != 0
                                    && current.graphicsDeviceEpoch != deviceInfo.GraphicsDeviceEpoch;
                if (!epochChanged)
                {
                    deviceInfo = new GpuUploadDeviceInfo(current);
                    SetObservedSupport(CallbacksAreReady(current) || TryResolveCallbacks(current));
                    return;
                }
                InvalidateSequences();
                for (int i = 0; i < targets.Count; i++)
                    if (targets[i] != null) targets[i].state = GpuUploadTargetState.Stale;
                deviceInfo = new GpuUploadDeviceInfo(current);
                SetObservedSupport(TryResolveCallbacks(current));
                drainNativeOrphans = supported &&
                    (deviceInfo.Capabilities & GpuUploadCapabilities.CompletionPoll) != 0;
                PrepareSessionForDeviceEpoch();
                NotifyDeviceEpochChanged(current.graphicsDeviceEpoch);
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                               || exception is EntryPointNotFoundException
                                               || exception is BadImageFormatException)
            {
                RecordBindingFailure(exception);
                supported = false;
                bindingUnavailable = true;
                availabilityError = GpuUploadError.UnsupportedBackend;
                PreserveUnprovenSession();
            }
        }

        private static void NotifyDeviceEpochChanged(ulong epoch)
        {
            var handlers = DeviceEpochChanged;
            if (handlers == null) return;
            foreach (Action<ulong> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(epoch);
                }
                catch (Exception exception)
                {
                    zone.MeowError($"[GpuUpload] DeviceEpochChanged handler failed: {exception}");
                }
            }
        }

        private static void PreserveUnprovenSession()
        {
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null) targets[i].closeRequested = true;
        }

        private static void ObserveBackendError(GpuUploadError error)
        {
            if (error == GpuUploadError.AbiMismatch)
            {
                supported = false;
                bindingUnavailable = true;
                availabilityError = error;
                PreserveUnprovenSession();
                return;
            }
            if (error == GpuUploadError.StateRestoreFailed)
            {
                DisableCorruptedSession(error);
                return;
            }
            if (error == GpuUploadError.InternalError)
            {
                supported = false;
                availabilityError = error;
                lastEpochCheckFrame = -1;
                return;
            }
            if (error != GpuUploadError.NotInitialized
                && error != GpuUploadError.UnsupportedBackend
                && error != GpuUploadError.DeviceLost
                && error != GpuUploadError.ContextLost)
                return;
            supported = false;
            availabilityError = error;
            if (error == GpuUploadError.UnsupportedBackend
                && deviceInfo.GraphicsDeviceEpoch == 0)
                bindingUnavailable = true;
            lastEpochCheckFrame = -1;
        }

        private static void DisableCorruptedSession(
            GpuUploadError reason = GpuUploadError.InternalError)
        {
            if (sessionCorrupted) return;
            sessionCorrupted = true;
            supported = false;
            availabilityError = reason;
            bool abandoned = deviceInfo.GraphicsDeviceEpoch == 0;
            try
            {
                if (!abandoned && !bindingUnavailable)
                {
                    var error = GpuUploadAbi.AbandonSession(out ulong abandonedEpoch);
                    abandoned = error == GpuUploadError.None
                                && abandonedEpoch == deviceInfo.GraphicsDeviceEpoch;
                    if (error != GpuUploadError.None)
                        reason = error;
                    else if (!abandoned)
                        reason = GpuUploadError.AbiMismatch;
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                               || exception is EntryPointNotFoundException
                                               || exception is BadImageFormatException)
            {
                RecordBindingFailure(exception);
                reason = GpuUploadError.UnsupportedBackend;
            }
            if (abandoned)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (target == null) continue;
                    target.closeRequested = true;
                    target.state = GpuUploadTargetState.Stale;
                }
                deviceInfo = default;
                AdvanceSessionGeneration();
                Cleanup();
            }
            else
            {
                PreserveUnprovenSession();
            }
            initialized = true;
            supported = false;
            bindingUnavailable = true;
            sessionCorrupted = true;
            availabilityError = reason;
        }

        private static bool TryParseSubmissionStatus(in GpuUploadAbi.SubmissionStatus value,
            ulong expectedSerial, ulong expectedEpoch, int regionCount,
            out GpuUploadStatus status)
        {
            bool validError = GpuUploadAbi.TryParseError(value.resultCode, out var result);
            bool valid = value.structSize == GpuUploadAbi.SubmissionStatusSize
                         && value.state >= (uint)GpuUploadSubmissionState.Pending
                         && value.state <= (uint)GpuUploadSubmissionState.Retired
                         && value.gpuState <= (uint)GpuUploadGpuState.Failed
                         && value.contentState <= (uint)GpuUploadContentState.MayHaveChanged
                         && validError
                         && value.failedRegion >= -1
                         && (regionCount < 0 || value.failedRegion < regionCount)
                         && (value.flags & ~((uint)GpuUploadAbi.StatusFlags.SourceConsumed
                                            | (uint)GpuUploadAbi.StatusFlags.CloseRequested
                                            | (uint)GpuUploadAbi.StatusFlags.RetireObserved)) == 0
                         && ((value.flags & (uint)GpuUploadAbi.StatusFlags.RetireObserved) == 0
                             || (value.flags & ((uint)GpuUploadAbi.StatusFlags.CloseRequested
                                                | (uint)GpuUploadAbi.StatusFlags.SourceConsumed)) ==
                             ((uint)GpuUploadAbi.StatusFlags.CloseRequested
                              | (uint)GpuUploadAbi.StatusFlags.SourceConsumed))
                         && value.serial == expectedSerial && expectedSerial != 0
                         && value.graphicsDeviceEpoch == expectedEpoch && expectedEpoch != 0
                         && HasValidSubmissionSemantics(value, result);
            if (!valid)
            {
                status = default;
                ObserveBackendError(GpuUploadError.AbiMismatch);
                return false;
            }
            status = new GpuUploadStatus(value);
            return true;
        }

        private static bool HasValidSubmissionSemantics(
            in GpuUploadAbi.SubmissionStatus value, GpuUploadError result)
        {
            bool sourceConsumed =
                (value.flags & (uint)GpuUploadAbi.StatusFlags.SourceConsumed) != 0;
            bool closeRequested =
                (value.flags & (uint)GpuUploadAbi.StatusFlags.CloseRequested) != 0;
            bool retireObserved =
                (value.flags & (uint)GpuUploadAbi.StatusFlags.RetireObserved) != 0;
            var state = (GpuUploadSubmissionState)value.state;
            var gpu = (GpuUploadGpuState)value.gpuState;
            var content = (GpuUploadContentState)value.contentState;
            switch (state)
            {
                case GpuUploadSubmissionState.Pending:
                    return result == GpuUploadError.None
                           && content == GpuUploadContentState.Unchanged
                           && value.failedRegion == -1 && !sourceConsumed && !retireObserved
                           && (gpu == GpuUploadGpuState.Unsupported
                               || gpu == GpuUploadGpuState.Pending);
                case GpuUploadSubmissionState.Encoded:
                    return result == GpuUploadError.None && value.failedRegion == -1
                           && sourceConsumed
                           && content == GpuUploadContentState.Changed
                           && gpu != GpuUploadGpuState.Failed;
                case GpuUploadSubmissionState.Rejected:
                    return result != GpuUploadError.None
                           && content == GpuUploadContentState.Unchanged
                           && sourceConsumed && gpu == GpuUploadGpuState.Unsupported;
                case GpuUploadSubmissionState.BackendFailed:
                    return result != GpuUploadError.None && result != GpuUploadError.DeviceLost
                           && content != GpuUploadContentState.Changed && sourceConsumed;
                case GpuUploadSubmissionState.DeviceLost:
                    return result == GpuUploadError.DeviceLost
                           && content != GpuUploadContentState.Changed && sourceConsumed
                           && gpu != GpuUploadGpuState.Complete;
                case GpuUploadSubmissionState.Cancelled:
                    return result == GpuUploadError.SubmissionClosing
                           && content != GpuUploadContentState.Changed
                           && value.failedRegion == -1 && sourceConsumed
                           && closeRequested && retireObserved
                           && gpu != GpuUploadGpuState.Complete;
                case GpuUploadSubmissionState.Retired:
                    return result == GpuUploadError.None && value.failedRegion == -1
                           && sourceConsumed && closeRequested && retireObserved
                           && content == GpuUploadContentState.Changed
                           && (gpu == GpuUploadGpuState.Unsupported
                               || gpu == GpuUploadGpuState.Complete);
                default:
                    return false;
            }
        }

        private static unsafe bool TryParseWebGlResult(GpuUploadAbi.Batch* value,
            BatchBuilder builder, int expectedBytes, ulong expectedSerial, uint slotId,
            uint slotGeneration, uint writtenBytes, GpuUploadError callError,
            out GpuUploadStatus status, out GpuUploadAdmissionStage stage)
        {
            bool validError = GpuUploadAbi.TryParseError(value->resultCode, out var result);
            bool validState = value->state >= (uint)GpuUploadSubmissionState.Encoded
                              && value->state <= (uint)GpuUploadSubmissionState.DeviceLost;
            bool valid = value->magic == GpuUploadAbi.Magic
                         && value->abiMajor == GpuUploadAbi.Major
                         && value->abiMinor == GpuUploadAbi.Minor
                         && value->totalBytes == (uint)expectedBytes
                         && value->flags == builder.batchFlags
                         && value->serial == expectedSerial && expectedSerial != 0
                         && value->graphicsDeviceEpoch == deviceInfo.GraphicsDeviceEpoch
                         && value->sequenceSerial == (builder.sequence?.serial ?? 0)
                         && value->sequenceOrdinal == builder.sequenceOrdinal
                         && value->slotId == slotId
                         && value->slotGeneration == slotGeneration
                         && value->writtenBytes == writtenBytes
                         && value->targetTableOffset == GpuUploadAbi.BatchSize
                         && value->targetCount == (uint)builder.targetCount
                         && value->regionTableOffset == (uint)builder.regionTableOffset
                         && value->regionCount == (uint)builder.regionCount
                         && validError && validState && result == callError
                         && value->admissionStage <= (uint)GpuUploadAdmissionStage.Backend
                         && value->failedRegion >= -1
                         && value->failedRegion < builder.regionCount;
            var state = validState
                ? (GpuUploadSubmissionState)value->state
                : GpuUploadSubmissionState.Invalid;
            valid &= IsValidWebGlResultPair(state, result);
            if (valid && state == GpuUploadSubmissionState.Encoded)
                valid = value->failedRegion == -1 && value->backendDetail == 0
                        && value->admissionStage == (uint)GpuUploadAdmissionStage.None;
            if (valid && state == GpuUploadSubmissionState.Rejected)
                valid = value->backendDetail == 0
                        && value->admissionStage != (uint)GpuUploadAdmissionStage.None;
            if (!valid)
            {
                status = default;
                stage = GpuUploadAdmissionStage.None;
                ObserveBackendError(GpuUploadError.AbiMismatch);
                return false;
            }
            var content = state == GpuUploadSubmissionState.Encoded
                ? GpuUploadContentState.Changed
                : state == GpuUploadSubmissionState.Rejected || value->failedRegion == -1
                    ? GpuUploadContentState.Unchanged
                    : GpuUploadContentState.MayHaveChanged;
            status = new GpuUploadStatus(state, GpuUploadGpuState.Unsupported, content,
                result, value->failedRegion, value->backendDetail, true, true, true);
            stage = (GpuUploadAdmissionStage)value->admissionStage;
            return true;
        }

        private static bool IsValidWebGlResultPair(GpuUploadSubmissionState state,
            GpuUploadError error)
        {
            switch (state)
            {
                case GpuUploadSubmissionState.Encoded:
                    return error == GpuUploadError.None;
                case GpuUploadSubmissionState.DeviceLost:
                    return error == GpuUploadError.ContextLost;
                case GpuUploadSubmissionState.BackendFailed:
                    return error == GpuUploadError.BackendFailed
                           || error == GpuUploadError.StateRestoreFailed
                           || error == GpuUploadError.InternalError;
                case GpuUploadSubmissionState.Rejected:
                    return error == GpuUploadError.NotInitialized
                           || error == GpuUploadError.AbiMismatch
                           || error == GpuUploadError.UnsupportedFeature
                           || error == GpuUploadError.InvalidLayout
                           || error == GpuUploadError.TargetNotFound
                           || error == GpuUploadError.TargetStale
                           || error == GpuUploadError.TargetClosing
                           || error == GpuUploadError.SourceOutOfRange
                           || error == GpuUploadError.Backpressure
                           || error == GpuUploadError.OutOfMemory
                           || error == GpuUploadError.SequenceNotFound
                           || error == GpuUploadError.SequenceClosing
                           || error == GpuUploadError.SequenceOrder
                           || error == GpuUploadError.SlotNotFound
                           || error == GpuUploadError.SlotBusy
                           || error == GpuUploadError.InternalError;
                default:
                    return false;
            }
        }

        private static bool TryParseTargetStatus(in GpuUploadAbi.TargetStatus value,
            GpuUploadTarget target, out GpuUploadTargetState state)
        {
            bool validError = GpuUploadAbi.TryParseError(value.error, out var error);
            bool valid = value.structSize == GpuUploadAbi.TargetStatusSize
                         && value.state >= (uint)GpuUploadTargetState.Active
                         && value.state <= (uint)GpuUploadTargetState.Stale
                         && value.generation == target.handle.generation
                         && value.generation != 0
                         && value.graphicsDeviceEpoch == target.epoch && target.epoch != 0
                         && validError;
            if (valid)
            {
                var parsedState = (GpuUploadTargetState)value.state;
                valid = parsedState == GpuUploadTargetState.Stale
                    ? error == GpuUploadError.DeviceLost
                    : error == GpuUploadError.None;
            }
            if (!valid)
            {
                state = GpuUploadTargetState.Invalid;
                ObserveBackendError(GpuUploadError.AbiMismatch);
                return false;
            }
            state = (GpuUploadTargetState)value.state;
            return true;
        }

        private static bool IsValidDeviceInfo(in GpuUploadAbi.DeviceInfo value)
        {
            return !HasInvalidDeviceContract(value) && value.graphicsDeviceEpoch != 0
                   && value.renderer == (uint)SystemInfo.graphicsDeviceType;
        }

        private static bool HasInvalidDeviceContract(in GpuUploadAbi.DeviceInfo value) =>
            value.structSize != GpuUploadAbi.DeviceInfoSize
                                   || value.abiMajor != GpuUploadAbi.Major
                                   || value.abiMinor != GpuUploadAbi.Minor
                                   || value.formatCount != GpuUploadAbi.FormatCount
                                   || value.dimensionCount != GpuUploadAbi.DimensionCount
                                   || value.maxConcurrentSubmissions > int.MaxValue
                                   || value.contractFingerprint != GpuUploadAbi.ContractFingerprint
                                   || (value.flags & ~KnownCapabilityMask) != 0
                                   || value.slotBytes == 0 || value.slotBytes > int.MaxValue
                                   || value.maxRegionsPerSubmission == 0
                                   || value.maxRegionsPerSubmission > int.MaxValue;

        private static bool HasRequiredCapabilities(in GpuUploadDeviceInfo value)
        {
            var required = GpuUploadCapabilities.ExplicitMips | GpuUploadCapabilities.Immediate;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            required |= GpuUploadCapabilities.CommandBuffer
                        | GpuUploadCapabilities.SubmissionBoundary;
#endif
            return (value.Capabilities & required) == required;
        }

        private static void SetObservedSupport(bool callbacksReady)
        {
            bool capabilitiesReady = HasRequiredCapabilities(deviceInfo);
            supported = callbacksReady && capabilitiesReady;
            if (supported)
                availabilityError = GpuUploadError.None;
            else if (!bindingUnavailable)
                availabilityError = callbacksReady
                    ? GpuUploadError.UnsupportedFeature
                    : GpuUploadError.InternalError;
            else if (availabilityError == GpuUploadError.None
                     || availabilityError == GpuUploadError.NotInitialized)
                availabilityError = GpuUploadError.AbiMismatch;
        }

        private static bool TryResolveCallbacks(in GpuUploadAbi.DeviceInfo value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            if (value.eventBase < 0 || value.eventCount != GpuUploadAbi.EventCount
                                    || value.eventBase > int.MaxValue - GpuUploadAbi.EventCount)
            {
                ObserveBackendError(GpuUploadError.AbiMismatch);
                return false;
            }
            try
            {
                IntPtr newUploadEvent = GpuUploadAbi.ls_gpu_v6_get_upload_event();
                IntPtr newBoundaryEvent = GpuUploadAbi.ls_gpu_v6_get_boundary_event();
                IntPtr newRetireEvent = GpuUploadAbi.ls_gpu_v6_get_retire_event();
                IntPtr newPollEvent = GpuUploadAbi.ls_gpu_v6_get_poll_event();
                if (newUploadEvent == IntPtr.Zero || newBoundaryEvent == IntPtr.Zero
                    || newRetireEvent == IntPtr.Zero || newPollEvent == IntPtr.Zero)
                {
                    ObserveBackendError(GpuUploadError.AbiMismatch);
                    return false;
                }
                CommandBuffer newImmediate = null;
                CommandBuffer newRetire = null;
                try
                {
                    if (immediateCommandBuffer == null)
                        newImmediate = new CommandBuffer { name = "LightSide GPU Upload v6" };
                    if (retireCommandBuffer == null)
                        newRetire = new CommandBuffer { name = "LightSide GPU Upload v6 Retire" };
                }
                catch
                {
                    newImmediate?.Dispose();
                    newRetire?.Dispose();
                    return false;
                }
                uploadEvent = newUploadEvent;
                boundaryEvent = newBoundaryEvent;
                retireEvent = newRetireEvent;
                pollEvent = newPollEvent;
                eventBase = value.eventBase;
                if (newImmediate != null) immediateCommandBuffer = newImmediate;
                if (newRetire != null) retireCommandBuffer = newRetire;
                return immediateCommandBuffer != null && retireCommandBuffer != null;
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                               || exception is EntryPointNotFoundException
                                               || exception is BadImageFormatException)
            {
                RecordBindingFailure(exception);
                bindingUnavailable = true;
                availabilityError = GpuUploadError.UnsupportedBackend;
                PreserveUnprovenSession();
                return false;
            }
#endif
        }

        private static bool CallbacksAreReady(in GpuUploadAbi.DeviceInfo value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return value.eventBase >= 0 && value.eventCount == GpuUploadAbi.EventCount
                                        && value.eventBase <= int.MaxValue - GpuUploadAbi.EventCount
                                        && eventBase == value.eventBase
                                        && uploadEvent != IntPtr.Zero && boundaryEvent != IntPtr.Zero
                                        && retireEvent != IntPtr.Zero && pollEvent != IntPtr.Zero
                                        && immediateCommandBuffer != null && retireCommandBuffer != null;
#endif
        }

        private static unsafe bool TryBeginBuilder(BatchBuilder builder)
        {
            ulong generation = NextGeneration(ref handleGeneration);
            if (generation == 0) return false;
            builder.generation = generation;
            builder.state = BuilderState.Building;
            builder.targetCount = 0;
            builder.targetIndices.Clear();
            builder.regionCount = 0;
            builder.regionLimit = builder.regionEnds.Length;
            builder.regionTableOffset = GpuUploadAbi.BatchSize;
            UnsafeUtility.MemClear(builder.blob.GetUnsafePtr(), GpuUploadAbi.BatchSize);
            builder.routeKey = IntPtr.Zero;
            builder.serial = 0;
            builder.closing = false;
            builder.updateCountPublished = false;
            builder.updateCountCallerManaged = false;
            builder.batchFlags = 0;
            builder.publicationMarkerAdded = false;
            builder.retireQueued = false;
            builder.historyIndex = -1;
            return true;
        }

        private static bool TryGetBatchBuilder(int index, ulong generation,
            BuilderState expected, out BatchBuilder builder)
        {
            if (builders != null && generation != 0 && index >= 0 && index < builders.Length)
            {
                builder = builders[index];
                if (builder != null && builder.generation == generation
                    && builder.state == expected)
                    return true;
            }
            builder = null;
            return false;
        }

        private static GpuUploadAdmission StaleBatchAdmission(in GpuUploadBatch batch) =>
            batch.builder >= 0 && batch.generation != 0
                ? GpuUploadAdmission.SessionAbandoned
                : GpuUploadAdmission.NotAdmitted;

        private static bool TryValidateRegion(GpuUploadTarget target,
            in GpuUploadRegion region, out ulong spanBytes, out GpuUploadError error)
        {
            spanBytes = 0;
            if (!target.supports.TryGet(region.Aspect, out var supportInfo))
            {
                error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            if (!TryCalculateRegionLayout(target.info.Description, supportInfo, region,
                    out var layout, out error))
                return false;
            spanBytes = layout.SourceSpanBytes;
            error = GpuUploadError.None;
            return true;
        }

        private static unsafe bool TryReadRejection(BatchBuilder builder, GpuUploadError error,
            out GpuUploadAdmissionStage stage, out int failedRegion)
        {
            var header = (GpuUploadAbi.Batch*)builder.blob.GetUnsafeReadOnlyPtr();
            stage = (GpuUploadAdmissionStage)header->admissionStage;
            failedRegion = header->failedRegion;
            if (header->resultCode == (int)error
                && header->admissionStage >= (uint)GpuUploadAdmissionStage.Layout
                && header->admissionStage <= (uint)GpuUploadAdmissionStage.Backend
                && failedRegion >= -1 && failedRegion < builder.regionCount)
                return true;
            stage = GpuUploadAdmissionStage.None;
            failedRegion = -1;
            return false;
        }

        private static unsafe bool TrySerialize(BatchBuilder builder, uint slotId,
            uint slotGeneration, uint writtenBytes, out int totalBytes,
            out GpuUploadError error)
        {
            totalBytes = 0;
            if (builder.targetCount <= 0 || builder.regionCount <= 0)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            for (int i = 0; i < builder.targetCount; i++)
            {
                var target = builder.targets[i];
                if (target == null || target.state != GpuUploadTargetState.Active
                                   || target.closeRequested
                                   || target.epoch != deviceInfo.GraphicsDeviceEpoch
                                   || target.handle.token != builder.targetHandles[i].token
                                   || target.handle.generation != builder.targetHandles[i].generation)
                {
                    error = GpuUploadError.TargetStale;
                    return false;
                }
            }
            if (builder.publicationMarkerAdded && !IsSingleCommandPublicationMarkerRegion(builder))
            {
                error = GpuUploadError.InvalidLayout;
                return false;
            }
            totalBytes = checked(builder.regionTableOffset
                + builder.regionCount * GpuUploadAbi.RegionSize);
            var header = (GpuUploadAbi.Batch*)builder.blob.GetUnsafePtr();
            *header = new GpuUploadAbi.Batch
            {
                magic = GpuUploadAbi.Magic,
                abiMajor = GpuUploadAbi.Major,
                abiMinor = GpuUploadAbi.Minor,
                totalBytes = (uint)totalBytes,
                flags = builder.batchFlags,
                graphicsDeviceEpoch = deviceInfo.GraphicsDeviceEpoch,
                sequenceSerial = builder.sequence?.serial ?? 0,
                sequenceOrdinal = builder.sequenceOrdinal,
                slotId = slotId,
                slotGeneration = slotGeneration,
                writtenBytes = writtenBytes,
                targetTableOffset = GpuUploadAbi.BatchSize,
                targetCount = (uint)builder.targetCount,
                regionTableOffset = (uint)builder.regionTableOffset,
                regionCount = (uint)builder.regionCount,
                failedRegion = -1
            };
            error = GpuUploadError.None;
            return true;
        }

        private static unsafe bool TryCreateSubmission(BatchBuilder builder, int bytes,
            out GpuUploadTicket ticket, out GpuUploadError error,
            out GpuUploadAdmission admission)
        {
            ticket = default;
            admission = GpuUploadAdmission.NotAdmitted;
            error = GpuUploadAbi.CreateSubmission((IntPtr)builder.blob.GetUnsafeReadOnlyPtr(),
                (uint)bytes, out IntPtr routeKey, out ulong serial,
                out bool contractViolation);
            if (contractViolation)
            {
                builder.state = BuilderState.Abandoned;
                admission = GpuUploadAdmission.SessionAbandoned;
                DisableCorruptedSession(error);
                return false;
            }
            if (error != GpuUploadError.None)
            {
                ObserveBackendError(error);
                if (builder.state != BuilderState.Building)
                    admission = GpuUploadAdmission.SessionAbandoned;
                return false;
            }
            if (routeKey == IntPtr.Zero || serial == 0)
            {
                builder.state = BuilderState.Abandoned;
                admission = GpuUploadAdmission.SessionAbandoned;
                error = GpuUploadError.InternalError;
                DisableCorruptedSession(GpuUploadError.InternalError);
                return false;
            }
            admission = GpuUploadAdmission.SessionAbandoned;
            int historyIndex = ReserveHistory();
            if (historyIndex < 0)
            {
                builder.state = BuilderState.Abandoned;
                error = GpuUploadError.InternalError;
                DisableCorruptedSession(GpuUploadError.InternalError);
                return false;
            }
            ticket = new GpuUploadTicket(sessionGeneration,
                deviceInfo.GraphicsDeviceEpoch, serial, historyIndex,
                builder.freeIndex, builder.generation);
            StoreStatus(historyIndex, ticket, new GpuUploadStatus(
                GpuUploadSubmissionState.Pending, GpuUploadGpuState.Pending,
                GpuUploadContentState.Unchanged, GpuUploadError.None, -1, 0,
                false, false, false));
            builder.state = BuilderState.Submitted;
            builder.routeKey = routeKey;
            builder.serial = serial;
            builder.historyIndex = historyIndex;
            TrackSubmitted(builder);
            admission = GpuUploadAdmission.Admitted;
            return true;
        }

        private static void ProcessStatus(BatchBuilder builder, in GpuUploadStatus status)
        {
            var ticket = new GpuUploadTicket(sessionGeneration,
                deviceInfo.GraphicsDeviceEpoch, builder.serial, builder.historyIndex,
                builder.freeIndex, builder.generation);
            StoreStatus(builder.historyIndex, ticket, status);
            if (status.Error != GpuUploadError.None)
            {
                ObserveBackendError(status.Error);
                if (!CanCallNative || builder.state != BuilderState.Submitted) return;
            }
            if (!builder.updateCountPublished && !builder.updateCountCallerManaged
                && status.ContentState != GpuUploadContentState.Unchanged)
                PublishUpdateCounts(builder);
            if (builder.state == BuilderState.Submitted && !builder.closing
                && status.State != GpuUploadSubmissionState.Pending
                && status.GpuState != GpuUploadGpuState.Pending
                && status.SourceConsumed)
                UntrackSubmitted(builder);
            if (status.RetireObserved && status.GpuState != GpuUploadGpuState.Pending)
                FreeBuilder(builder);
        }

        private static void RecordUpdateCounts(CommandBuffer commandBuffer, BatchBuilder builder)
        {
            for (int i = 0; i < builder.targetCount; i++)
            {
                var target = builder.targets[i];
                if (target?.texture is RenderTexture texture)
                    commandBuffer.IncrementUpdateCount(new RenderTargetIdentifier(texture));
            }
            builder.updateCountPublished = true;
        }

        private static bool HasOnlyRenderTextureTargets(BatchBuilder builder)
        {
            for (int i = 0; i < builder.targetCount; i++)
                if (!(builder.targets[i]?.texture is RenderTexture)) return false;
            return true;
        }

        private static bool QueueRetire(IntPtr routeKey)
        {
            try
            {
                retireCommandBuffer.Clear();
                retireCommandBuffer.IssuePluginEventAndData(retireEvent,
                    eventBase + GpuUploadAbi.RetireEvent, routeKey);
                Graphics.ExecuteCommandBuffer(retireCommandBuffer);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryQueueCloseAndRetire(BatchBuilder builder)
        {
            if (!CanCallNative) return;
            var error = GpuUploadAbi.CloseSubmission(builder.routeKey);
            if (error == GpuUploadError.SubmissionNotFound)
            {
                DisableCorruptedSession(GpuUploadError.SubmissionNotFound);
                return;
            }
            ObserveBackendError(error);
            if (error == GpuUploadError.None || error == GpuUploadError.SubmissionClosing)
                builder.retireQueued = CanCallNative && QueueRetire(builder.routeKey);
        }

        private static void PublishUpdateCounts(BatchBuilder builder)
        {
            for (int i = 0; i < builder.targetCount; i++)
            {
                var target = builder.targets[i];
                if (target?.texture != null) target.texture.IncrementUpdateCount();
            }
            builder.updateCountPublished = true;
        }

        private static unsafe void FreeBuilder(BatchBuilder builder)
        {
            if (builder == null || builder.state == BuilderState.Free) return;
            UntrackSubmitted(builder);
            if (builder.targetCount > 0)
                UnsafeUtility.MemClear((byte*)builder.blob.GetUnsafePtr()
                    + GpuUploadAbi.BatchSize,
                    builder.targetCount * GpuUploadAbi.TargetSize);
            for (int i = 0; i < builder.targetCount; i++)
            {
                builder.targets[i] = null;
                builder.targetHandles[i] = default;
            }
            builder.targetCount = 0;
            builder.targetIndices.Clear();
            builder.regionCount = 0;
            builder.regionLimit = 0;
            builder.regionTableOffset = 0;
            builder.routeKey = IntPtr.Zero;
            builder.serial = 0;
            builder.closing = false;
            builder.updateCountPublished = false;
            builder.updateCountCallerManaged = false;
            builder.batchFlags = 0;
            builder.publicationMarkerAdded = false;
            builder.sequence = null;
            builder.sequenceOrdinal = 0;
            builder.retireQueued = false;
            DetachHistory(builder.historyIndex);
            builder.historyIndex = -1;
            builder.state = BuilderState.Free;
            freeBuilders[freeBuilderCount++] = builder.freeIndex;
        }

        private static void RefreshTargetState(GpuUploadTarget target)
        {
            if (target == null || !CanCallNative) return;
            var status = new GpuUploadAbi.TargetStatus();
            var error = GpuUploadAbi.GetTargetStatus(ref target.handle, ref status);
            ObserveBackendError(error);
            if (error == GpuUploadError.None)
            {
                if (TryParseTargetStatus(status, target, out var state)) target.state = state;
            }
            else if (error == GpuUploadError.TargetNotFound)
                target.state = GpuUploadTargetState.Retired;
            else if (error == GpuUploadError.DeviceLost || error == GpuUploadError.ContextLost)
                target.state = GpuUploadTargetState.Stale;
            else if (error == GpuUploadError.TargetStale)
                target.state = GpuUploadTargetState.Stale;
        }

        private static bool TryBuildRegistration(Texture texture, GpuUploadAspect primaryAspect,
            out GpuUploadTargetInfo info, out uint flags,
            out GpuUploadTargetSupports supports,
            out GpuUploadAbi.TargetRegistration registration, out GpuUploadError error)
        {
            info = default;
            flags = 0;
            supports = default;
            registration = default;
            if (texture == null)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            GpuUploadResourceKind resourceKind = primaryAspect switch
            {
                GpuUploadAspect.Color => GpuUploadResourceKind.Texture,
                GpuUploadAspect.Depth => GpuUploadResourceKind.DepthStencil,
                GpuUploadAspect.Stencil => GpuUploadResourceKind.DepthStencil,
                _ => (GpuUploadResourceKind)uint.MaxValue
            };
            if (!IsResourceAspect(resourceKind, primaryAspect))
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            if (!TryDescribeTexture(texture, resourceKind, primaryAspect,
                    out info, out flags, out error))
                return false;
            if (info.SampleCount != 1)
            {
                error = GpuUploadError.UnsupportedSampleCount;
                return false;
            }
            if ((flags & ((uint)GpuUploadAbi.TargetFlags.Memoryless
                          | (uint)GpuUploadAbi.TargetFlags.Streaming
                          | (uint)GpuUploadAbi.TargetFlags.DynamicSize)) != 0)
            {
                error = GpuUploadError.UnsupportedTexture;
                return false;
            }
            if (!TryBuildSupportSet(info, out supports, out error)
                || !supports.TryGet(primaryAspect, out var primarySupport)
                || !primarySupport.Supported)
            {
                if (error == GpuUploadError.None) error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            info = info.WithSupportedAspects(supports.SupportedAspects);
            if (resourceKind == GpuUploadResourceKind.DepthStencil)
                flags |= (uint)GpuUploadAbi.TargetFlags.DepthStencil;
            IntPtr pointer = resourceKind == GpuUploadResourceKind.DepthStencil
                ? ((RenderTexture)texture).GetNativeDepthBufferPtr()
                : texture.GetNativeTexturePtr();
            if (pointer == IntPtr.Zero)
            {
                error = GpuUploadError.UnsupportedTexture;
                return false;
            }
            registration = new GpuUploadAbi.TargetRegistration
            {
                structSize = GpuUploadAbi.TargetRegistrationSize,
                flags = flags,
                graphicsDeviceEpoch = deviceInfo.GraphicsDeviceEpoch,
                nativeResource = IntPtr.Size == 4
                    ? unchecked((uint)pointer.ToInt32())
                    : unchecked((ulong)pointer.ToInt64()),
                width = (uint)info.Width,
                height = (uint)info.Height,
                depth = (uint)info.Depth,
                layers = (uint)info.Layers,
                mipCount = (uint)info.MipCount,
                format = (uint)info.Format,
                dimension = (uint)info.Dimension,
                sampleCount = (uint)info.SampleCount,
                resourceKind = (uint)resourceKind
            };
            error = GpuUploadError.None;
            return true;
        }

        private static bool TryDescribeTexture(Texture texture, GpuUploadResourceKind resourceKind,
            GpuUploadAspect primaryAspect, out GpuUploadTargetInfo info,
            out uint flags, out GpuUploadError error)
        {
            info = default;
            flags = 0;
            GraphicsFormat graphicsFormat;
            if (resourceKind == GpuUploadResourceKind.DepthStencil)
            {
#if UNITY_2021_2_OR_NEWER
                if (!(texture is RenderTexture depthTexture) || !depthTexture.IsCreated())
                {
                    error = GpuUploadError.UnsupportedTexture;
                    return false;
                }
                graphicsFormat = depthTexture.depthStencilFormat;
#else
                error = GpuUploadError.UnsupportedTexture;
                return false;
#endif
            }
            else
                graphicsFormat = texture.graphicsFormat;
            if (!TryMapFormat(graphicsFormat, out var format)
                || !TryGetFormatLayout(format, primaryAspect, out _)
                || !TryMapDimension(texture.dimension, out var dimension))
            {
                error = GpuUploadError.UnsupportedTexture;
                return false;
            }
            int depth = 1;
            int layers = 1;
            int samples = 1;
            int mipCount = texture.mipmapCount;
            bool readable = false;
            switch (texture)
            {
                case Texture2D value:
                    readable = value.isReadable;
                    if (value.streamingMipmaps)
                        flags |= (uint)GpuUploadAbi.TargetFlags.Streaming;
                    break;
                case Texture2DArray value:
                    layers = value.depth;
                    readable = value.isReadable;
                    break;
                case Texture3D value:
                    depth = value.depth;
                    readable = value.isReadable;
                    break;
                case Cubemap value:
                    layers = 6;
                    readable = value.isReadable;
                    if (value.streamingMipmaps)
                        flags |= (uint)GpuUploadAbi.TargetFlags.Streaming;
                    break;
                case CubemapArray value:
                    if (value.cubemapCount <= 0 || value.cubemapCount > int.MaxValue / 6)
                    {
                        error = GpuUploadError.UnsupportedTexture;
                        return false;
                    }
                    layers = value.cubemapCount * 6;
                    readable = value.isReadable;
                    break;
                case CustomRenderTexture value when value.doubleBuffered:
                    error = GpuUploadError.UnsupportedTexture;
                    return false;
                case RenderTexture value:
                    if (!value.IsCreated())
                    {
                        error = GpuUploadError.UnsupportedTexture;
                        return false;
                    }
                    samples = value.antiAliasing;
                    if (value.memorylessMode != RenderTextureMemoryless.None)
                        flags |= (uint)GpuUploadAbi.TargetFlags.Memoryless;
                    if (value.useDynamicScale)
                        flags |= (uint)GpuUploadAbi.TargetFlags.DynamicSize;
                    if (dimension == GpuUploadDimension.Texture2DArray)
                        layers = value.volumeDepth;
                    else if (dimension == GpuUploadDimension.Texture3D)
                        depth = value.volumeDepth;
                    else if (dimension == GpuUploadDimension.Cube)
                        layers = 6;
                    else if (dimension == GpuUploadDimension.CubeArray)
                    {
                        layers = value.volumeDepth;
                        if (layers <= 0 || layers % 6 != 0)
                        {
                            error = GpuUploadError.UnsupportedTexture;
                            return false;
                        }
                    }
                    break;
                default:
                    error = GpuUploadError.UnsupportedTexture;
                    return false;
            }
            if (readable) flags |= (uint)GpuUploadAbi.TargetFlags.CpuReadable;
            if (texture.width <= 0 || texture.height <= 0 || depth <= 0 || layers <= 0
                || mipCount <= 0 || samples <= 0)
            {
                error = GpuUploadError.UnsupportedTexture;
                return false;
            }
            info = new GpuUploadTargetInfo(texture.width, texture.height, depth, layers,
                mipCount, format, dimension, samples, resourceKind, primaryAspect,
                GpuUploadAspectMask.None, readable);
            error = GpuUploadError.None;
            return true;
        }


        private static bool TryGetBatchFlags(GpuUploadOverlapPolicy overlapPolicy,
            GpuUploadBatchOptions options, out uint flags)
        {
            switch (overlapPolicy)
            {
                case GpuUploadOverlapPolicy.ValidateNonOverlapping:
                    flags = 0;
                    break;
                case GpuUploadOverlapPolicy.AssumeNonOverlapping:
                    flags = (uint)GpuUploadAbi.BatchFlags.AssumeNonOverlapping;
                    break;
                case GpuUploadOverlapPolicy.OrderedOverlaps:
                    flags = (uint)GpuUploadAbi.BatchFlags.OrderedOverlaps;
                    break;
                default:
                    flags = 0;
                    return false;
            }
            if ((options & ~GpuUploadBatchOptions.ObserveSharedGlErrors) != 0)
            {
                flags = 0;
                return false;
            }
            if ((options & GpuUploadBatchOptions.ObserveSharedGlErrors) != 0)
                flags |= (uint)GpuUploadAbi.BatchFlags.ObserveSharedGlErrors;
            return true;
        }

        private static int MipSize(int size, int mip) => mip >= 31 ? 1 : Math.Max(1, size >> mip);

        internal static bool TryAlignUp(ulong value, uint alignment, out ulong result)
        {
            if (alignment == 0)
            {
                result = 0;
                return false;
            }
            ulong remainder = value % alignment;
            ulong addition = remainder == 0 ? 0 : alignment - remainder;
            if (value > ulong.MaxValue - addition)
            {
                result = 0;
                return false;
            }
            result = value + addition;
            return true;
        }

        private static ulong NextImmediateSerial() => NextGeneration(ref immediateSerial);

        private static bool HasInvalidStats(in GpuUploadAbi.Stats value) =>
            value.structSize != GpuUploadAbi.StatsSize || value.reserved != 0
            || value.graphicsDeviceEpoch != deviceInfo.GraphicsDeviceEpoch
            || value.poolNodesFree > value.poolNodes
            || value.poolNodesInFlight > value.poolNodes - value.poolNodesFree
            || value.poolStagingFreeBytes > value.poolStagingCapacityBytes
            || value.poolStagingInFlightBytes >
            value.poolStagingCapacityBytes - value.poolStagingFreeBytes;

        private static ulong NextGeneration(ref ulong value) =>
            value == ulong.MaxValue ? 0 : ++value;

        private static void ResetSession()
        {
            Cleanup();
            InvalidateSequences();
#if UNITY_EDITOR
            if (EditorLifecycle.IsReloading) ReleaseAcquiredSlotsForTeardown();
#endif
            if (!initialized) AdvanceSessionGeneration();
            if (!initialized) return;
            sessionCorrupted = true;
            supported = false;
            bindingUnavailable = true;
        }

        private static void AdvanceSessionGeneration()
        {
            sessionGeneration = NextGeneration(ref sessionGeneration);
            if (sessionGeneration == 0)
            {
                supported = false;
                bindingUnavailable = true;
                availabilityError = GpuUploadError.InternalError;
            }
        }

        private static void Cleanup()
        {
            if (!initialized) return;
            bool sessionAbandoned = deviceInfo.GraphicsDeviceEpoch == 0;
            try
            {
                if (!sessionAbandoned && !bindingUnavailable)
                {
                    var error = GpuUploadAbi.AbandonSession(out ulong abandonedEpoch);
                    if (error != GpuUploadError.None)
                    {
                        supported = false;
                        sessionCorrupted = true;
                        bindingUnavailable = true;
                        availabilityError = error;
                        PreserveUnprovenSession();
                        return;
                    }
                    if (abandonedEpoch != deviceInfo.GraphicsDeviceEpoch)
                    {
                        supported = false;
                        sessionCorrupted = true;
                        bindingUnavailable = true;
                        availabilityError = GpuUploadError.AbiMismatch;
                        PreserveUnprovenSession();
                        return;
                    }
                    sessionAbandoned = true;
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                               || exception is EntryPointNotFoundException
                                               || exception is BadImageFormatException)
            {
                RecordBindingFailure(exception);
                supported = false;
                sessionCorrupted = true;
                bindingUnavailable = true;
                availabilityError = GpuUploadError.UnsupportedBackend;
                PreserveUnprovenSession();
                return;
            }
            if (!sessionAbandoned) return;
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target == null) continue;
                target.closeRequested = true;
                target.state = GpuUploadTargetState.Stale;
                target.texture = null;
            }
            targets.Clear();
            InvalidateSequences();
            CleanupSession();
            immediateCommandBuffer?.Dispose();
            retireCommandBuffer?.Dispose();
            immediateCommandBuffer = null;
            retireCommandBuffer = null;
            initialized = false;
            supported = false;
            bindingUnavailable = false;
            sessionCorrupted = false;
            automaticPumpFailureLogged = false;
            deviceInfo = default;
            availabilityError = GpuUploadError.NotInitialized;
            uploadEvent = IntPtr.Zero;
            boundaryEvent = IntPtr.Zero;
            retireEvent = IntPtr.Zero;
            pollEvent = IntPtr.Zero;
            eventBase = 0;
            lastEpochCheckFrame = -1;
            drainNativeOrphans = false;
        }
    }
}
