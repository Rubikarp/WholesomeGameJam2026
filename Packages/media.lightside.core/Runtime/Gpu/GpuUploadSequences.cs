using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>Controls the terminal contract of an ordered batch sequence.</summary>
    [Flags]
    public enum GpuUploadSequenceOptions : uint
    {
        /// <summary>The caller seals the sequence explicitly after its final payload batch.</summary>
        None = 0,
        /// <summary>Requires the final batch to contain one publication marker.</summary>
        PublicationMarker = 1
    }

    /// <summary>Aggregate admission and execution lifecycle shared by an ordered batch sequence.</summary>
    public enum GpuUploadSequenceState : uint
    {
        /// <summary>No retained sequence state.</summary>
        Invalid = 0,
        /// <summary>Accepts additional payload batches or one terminal publication batch.</summary>
        Open = 1,
        /// <summary>Admission is closed while accepted batches advance through the graphics stream.</summary>
        Sealed = 2,
        /// <summary>A validation, ordering, or backend failure suppressed the remaining sequence.</summary>
        Failed = 3,
        /// <summary>The caller stopped the sequence instead of completing its normal terminal transition.</summary>
        Aborted = 4,
        /// <summary>Every admitted batch was encoded; consult GPU state for physical completion.</summary>
        Complete = 5
    }

    /// <summary>Whether the sequence's terminal GPU-visible publication command can be observed.</summary>
    public enum GpuUploadPublicationState : uint
    {
        /// <summary>The sequence has no publication command.</summary>
        None = 0,
        /// <summary>A publication command is expected but has not reached a terminal observation.</summary>
        Pending = 1,
        /// <summary>The command was definitely not encoded, so consumers must keep the prior state active.</summary>
        Suppressed = 2,
        /// <summary>The publication command was encoded after every preceding sequence batch.</summary>
        Published = 3,
        /// <summary>A device, scheduling, or terminal-command failure made publication impossible to prove.</summary>
        Unknown = 4
    }

    /// <summary>Aggregate state retained for an ordered batch sequence.</summary>
    public readonly struct GpuUploadSequenceStatus
    {
        /// <summary>Aggregate admission and encoding lifecycle.</summary>
        public readonly GpuUploadSequenceState State;
        /// <summary>Physical completion state across admitted batches.</summary>
        public readonly GpuUploadGpuState GpuState;
        /// <summary>Whether admitted batches changed or may have changed their targets.</summary>
        public readonly GpuUploadContentState ContentState;
        /// <summary>
        /// State of the optional terminal publication command, including uncertain attempted writes.
        /// </summary>
        public readonly GpuUploadPublicationState PublicationState;
        /// <summary>First latched sequence failure, or <see cref="GpuUploadError.None"/>.</summary>
        public readonly GpuUploadError Error;
        /// <summary>Zero-based failed batch ordinal, or -1 when no batch is attributable.</summary>
        public readonly int FailedBatch;
        /// <summary>Zero-based failed region within <see cref="FailedBatch"/>, or -1 when unknown.</summary>
        public readonly int FailedRegion;
        /// <summary>Backend-specific detail associated with <see cref="Error"/>.</summary>
        public readonly uint BackendDetail;
        /// <summary>Final batch count after sealing, failure, or cancellation; zero while open.</summary>
        public readonly uint ExpectedBatchCount;
        /// <summary>Number of batches accepted into the sequence.</summary>
        public readonly uint AdmittedBatchCount;
        /// <summary>Number of admitted batches whose source storage is no longer referenced.</summary>
        public readonly uint SourceConsumedBatchCount;
        /// <summary>Number of admitted batches whose complete command list was encoded.</summary>
        public readonly uint EncodedBatchCount;
        /// <summary>Number of admitted batches with a terminal physical-completion observation.</summary>
        public readonly uint GpuTerminalBatchCount;
        /// <summary>Number of admitted batch identities retired from submission tracking.</summary>
        public readonly uint RetiredBatchCount;
        /// <summary>Whether sequence disposal requested native identity retirement.</summary>
        public readonly bool CloseRequested;
        /// <summary>Whether sequence-owned target lifetime leases were released.</summary>
        public readonly bool TargetLeasesReleased;

        internal GpuUploadSequenceStatus(in GpuUploadAbi.SequenceStatus value)
        {
            State = (GpuUploadSequenceState)value.state;
            GpuState = (GpuUploadGpuState)value.gpuState;
            ContentState = (GpuUploadContentState)value.contentState;
            PublicationState = (GpuUploadPublicationState)value.publicationState;
            Error = (GpuUploadError)value.resultCode;
            FailedBatch = value.failedBatch;
            FailedRegion = value.failedRegion;
            BackendDetail = value.backendDetail;
            ExpectedBatchCount = value.expectedBatchCount;
            AdmittedBatchCount = value.admittedBatchCount;
            SourceConsumedBatchCount = value.sourceConsumedBatchCount;
            EncodedBatchCount = value.encodedBatchCount;
            GpuTerminalBatchCount = value.gpuTerminalBatchCount;
            RetiredBatchCount = value.retiredBatchCount;
            CloseRequested = (value.flags &
                (uint)GpuUploadAbi.SequenceStatusFlags.CloseRequested) != 0;
            TargetLeasesReleased = (value.flags &
                (uint)GpuUploadAbi.SequenceStatusFlags.TargetLeasesReleased) != 0;
        }
    }

    /// <summary>
    /// Owns one opt-in ordered sequence spanning independent batches and targets. Each distinct
    /// target remains leased until the sequence completes, fails, or is aborted.
    /// </summary>
    public sealed class GpuUploadSequence : IDisposable
    {
        internal readonly ulong serial;
        internal readonly ulong epoch;
        internal readonly ulong sessionGeneration;
        internal readonly GpuUploadSequenceOptions options;
        internal uint nextOrdinal;
        internal bool sealedForAdmission;
        internal bool closed;

        internal GpuUploadSequence(ulong serial, ulong epoch, ulong sessionGeneration,
            GpuUploadSequenceOptions options)
        {
            this.serial = serial;
            this.epoch = epoch;
            this.sessionGeneration = sessionGeneration;
            this.options = options;
        }

        /// <summary>Native identity assigned to this sequence.</summary>
        public ulong Serial => serial;
        /// <summary>Terminal behavior selected when the sequence was created.</summary>
        public GpuUploadSequenceOptions Options => options;
        /// <summary>Whether local state still permits another admission attempt.</summary>
        public bool IsOpen => GpuUpload.IsSequenceOpen(this);

        /// <summary>Seals a sequence that does not require a publication marker.</summary>
        public bool TrySeal(out GpuUploadError error) => GpuUpload.TrySealSequence(this, out error);

        /// <summary>
        /// Prevents further admission, cancels not-yet-executed members, and suppresses any
        /// pending publication.
        /// </summary>
        public bool TryAbort(out GpuUploadError error) => GpuUpload.TryAbortSequence(this, out error);

        /// <summary>Reads the aggregate retained status for this sequence.</summary>
        public bool TryGetStatus(out GpuUploadSequenceStatus status) =>
            GpuUpload.TryGetSequenceStatus(this, out status);

        /// <summary>
        /// Closes the sequence identity. An open sequence is aborted first; an already terminal
        /// sequence retains its final status through the bounded native history.
        /// </summary>
        public void Dispose() => GpuUpload.CloseSequence(this);
    }

    public partial struct GpuUploadBatch
    {
        /// <summary>
        /// Appends the final single-command region as the consumer's GPU-visible publication marker.
        /// No region can be appended afterward. GL backends must inspect the process-global error
        /// channel around this marker and can consume a pre-existing error from unrelated code.
        /// </summary>
        public bool TryAddPublicationMarker(GpuUploadTarget target, in GpuUploadRegion region,
            out GpuUploadError error) =>
            GpuUpload.TryAddPublicationMarker(this, target, region, out error);

        /// <summary>Submits this batch as the next member of an ordered sequence.</summary>
        public GpuUploadSubmitResult Submit(GpuUploadSequence sequence, ref GpuUploadSlot slot,
            int writtenBytes) =>
            GpuUpload.Submit(this, sequence, ref slot, writtenBytes);

        /// <summary>
        /// Records this batch as the next sequence member. Command buffers containing sequence
        /// members must execute in their assigned ordinal order; the sequence rejects disorder
        /// and does not reorder callbacks.
        /// </summary>
        public GpuUploadRecordResult RecordOnce(GpuUploadSequence sequence, ref GpuUploadSlot slot,
            int writtenBytes, CommandBuffer commandBuffer,
            GpuUploadRecordOptions options = GpuUploadRecordOptions.None) =>
            GpuUpload.RecordOnce(this, sequence, ref slot, writtenBytes, commandBuffer, options);
    }

    public static partial class GpuUpload
    {
        private static readonly List<GpuUploadSequence> sequences = new();

        /// <summary>Creates an open publication sequence spanning arbitrary batches and targets.</summary>
        public static bool TryCreateSequence(out GpuUploadSequence sequence,
            out GpuUploadError error) =>
            TryCreateSequence(GpuUploadSequenceOptions.PublicationMarker, out sequence, out error);

        /// <summary>Creates an open ordered sequence with explicitly selected terminal semantics.</summary>
        public static bool TryCreateSequence(GpuUploadSequenceOptions options,
            out GpuUploadSequence sequence, out GpuUploadError error)
        {
            sequence = null;
            Initialize();
            CheckDeviceEpoch();
            if (!CanAdmitNewWork)
            {
                error = AdmissionError;
                return false;
            }
            if ((options & ~GpuUploadSequenceOptions.PublicationMarker) != 0)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            var descriptor = new GpuUploadAbi.SequenceDesc
            {
                flags = (uint)options,
                graphicsDeviceEpoch = deviceInfo.GraphicsDeviceEpoch
            };
            error = GpuUploadAbi.CreateSequence(ref descriptor, out ulong serial,
                out bool contractViolation);
            if (contractViolation)
            {
                DisableCorruptedSession(error);
                return false;
            }
            if (error != GpuUploadError.None)
            {
                ObserveBackendError(error);
                return false;
            }
            sequence = new GpuUploadSequence(serial, deviceInfo.GraphicsDeviceEpoch,
                sessionGeneration, options);
            sequences.Add(sequence);
            return true;
        }

        private static bool IsSequenceLive(GpuUploadSequence sequence) =>
            sequence != null && sequence.serial != 0 && !sequence.closed
            && sequence.sessionGeneration == sessionGeneration
            && sequence.epoch == deviceInfo.GraphicsDeviceEpoch
            && CanCallNative;

        internal static bool IsSequenceOpen(GpuUploadSequence sequence) =>
            IsSequenceLive(sequence) && !sequence.sealedForAdmission;

        internal static bool TrySealSequence(GpuUploadSequence sequence,
            out GpuUploadError error)
        {
            Initialize();
            CheckDeviceEpoch();
            if (!IsSequenceOpen(sequence)
                || sequence.options != GpuUploadSequenceOptions.None
                || sequence.nextOrdinal == 0)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            error = GpuUploadAbi.SealSequence(sequence.serial, sequence.nextOrdinal);
            if (error == GpuUploadError.None)
            {
                sequence.sealedForAdmission = true;
                return true;
            }
            if (error == GpuUploadError.SequenceClosing)
                sequence.sealedForAdmission = true;
            ObserveBackendError(error);
            return false;
        }

        internal static bool TryAbortSequence(GpuUploadSequence sequence,
            out GpuUploadError error)
        {
            Initialize();
            CheckDeviceEpoch();
            if (!IsSequenceLive(sequence))
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            error = GpuUploadAbi.AbortSequence(sequence.serial);
            if (error == GpuUploadError.None)
            {
                sequence.sealedForAdmission = true;
                return true;
            }
            if (error == GpuUploadError.SequenceClosing)
                sequence.sealedForAdmission = true;
            ObserveBackendError(error);
            return false;
        }

        internal static bool TryGetSequenceStatus(GpuUploadSequence sequence,
            out GpuUploadSequenceStatus status)
        {
            status = default;
            if (sequence == null || sequence.serial == 0) return false;
            Initialize();
            CheckDeviceEpoch();
            if (!CanCallNative || sequence.sessionGeneration != sessionGeneration
                               || sequence.epoch != deviceInfo.GraphicsDeviceEpoch)
                return false;
            var nativeStatus = new GpuUploadAbi.SequenceStatus();
            var error = GpuUploadAbi.GetSequenceStatus(sequence.serial, ref nativeStatus);
            if (error == GpuUploadError.SequenceNotFound) return false;
            if (error != GpuUploadError.None)
            {
                ObserveBackendError(error);
                return false;
            }
            if (!TryParseSequenceStatus(nativeStatus, sequence, out status)) return false;
            if (status.State != GpuUploadSequenceState.Open)
                sequence.sealedForAdmission = true;
            return true;
        }

        internal static void CloseSequence(GpuUploadSequence sequence)
        {
            if (sequence == null || sequence.closed) return;
            Initialize();
            CheckDeviceEpoch();
            if (CanCallNative && sequence.sessionGeneration == sessionGeneration
                              && sequence.epoch == deviceInfo.GraphicsDeviceEpoch)
            {
                if (!sequence.sealedForAdmission)
                {
                    var abortError = GpuUploadAbi.AbortSequence(sequence.serial);
                    if (abortError != GpuUploadError.None
                        && abortError != GpuUploadError.SequenceClosing
                        && abortError != GpuUploadError.SequenceNotFound)
                        ObserveBackendError(abortError);
                }
                if (CanCallNative)
                {
                    var closeError = GpuUploadAbi.CloseSequence(sequence.serial);
                    if (closeError != GpuUploadError.None
                        && closeError != GpuUploadError.SequenceNotFound)
                        ObserveBackendError(closeError);
                }
            }
            sequence.sealedForAdmission = true;
            sequence.closed = true;
            sequences.Remove(sequence);
        }

        internal static bool TryAddPublicationMarker(in GpuUploadBatch batch,
            GpuUploadTarget target, in GpuUploadRegion region, out GpuUploadError error)
        {
            if (!CanAdmitNewWork)
            {
                error = AdmissionError;
                return false;
            }
            if (target == null || !IsSingleCommandPublicationRegion(target, region))
            {
                error = GpuUploadError.InvalidLayout;
                return false;
            }
            if (!TryAddRegion(batch, target, region, out error)) return false;
            if (!TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building,
                    out var builder))
            {
                error = GpuUploadError.InternalError;
                return false;
            }
            builder.publicationMarkerAdded = true;
            builder.batchFlags |= (uint)GpuUploadAbi.BatchFlags.ObserveSharedGlErrors;
            unsafe
            {
                var regions = (GpuUploadAbi.Region*)((byte*)builder.blob.GetUnsafePtr()
                                                     + builder.regionTableOffset);
                regions[builder.regionCount - 1].flags =
                    (uint)GpuUploadAbi.RegionFlags.PublicationMarker;
            }
            return true;
        }

        internal static GpuUploadSubmitResult Submit(in GpuUploadBatch batch,
            GpuUploadSequence sequence, ref GpuUploadSlot slot, int writtenBytes)
        {
            GpuUploadError error = GpuUploadError.InvalidArgument;
            if (!TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building,
                    out var builder)
                || !TryPrepareSequenceAdmission(sequence, builder, out error))
                return new GpuUploadSubmitResult(GpuUploadAdmission.NotAdmitted, default,
                    GpuUploadContentState.Unchanged, error);
            bool publicationTerminal = builder.publicationMarkerAdded;
            var result = Submit(batch, ref slot, writtenBytes);
            CompleteSequenceAdmission(sequence, builder, publicationTerminal,
                result.Admission, result.Error);
            return result;
        }

        internal static GpuUploadRecordResult RecordOnce(in GpuUploadBatch batch,
            GpuUploadSequence sequence, ref GpuUploadSlot slot, int writtenBytes,
            CommandBuffer commandBuffer, GpuUploadRecordOptions options)
        {
            GpuUploadError error = GpuUploadError.InvalidArgument;
            if (!TryGetBatchBuilder(batch.builder, batch.generation, BuilderState.Building,
                    out var builder)
                || !TryPrepareSequenceAdmission(sequence, builder, out error))
                return new GpuUploadRecordResult(0, 0,
                    GpuUploadAdmission.NotAdmitted, default, error);
            bool publicationTerminal = builder.publicationMarkerAdded;
            var result = RecordOnce(batch, ref slot, writtenBytes, commandBuffer, options);
            CompleteSequenceAdmission(sequence, builder, publicationTerminal,
                result.Admission, result.Error);
            return result;
        }

        private static bool TryPrepareSequenceAdmission(GpuUploadSequence sequence,
            BatchBuilder builder, out GpuUploadError error)
        {
            bool publicationTerminal = builder.publicationMarkerAdded;
            if (sequence == null || sequence.serial == 0 || sequence.closed
                || sequence.sealedForAdmission || sequence.nextOrdinal == uint.MaxValue
                || sequence.sessionGeneration != sessionGeneration
                || sequence.epoch != deviceInfo.GraphicsDeviceEpoch
                || publicationTerminal &&
                    (sequence.options & GpuUploadSequenceOptions.PublicationMarker) == 0
                || publicationTerminal && (builder.targetCount != 1 || builder.regionCount != 1)
                || builder.sequence != null)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            builder.sequence = sequence;
            builder.sequenceOrdinal = sequence.nextOrdinal;
            error = GpuUploadError.None;
            return true;
        }

        private static void CompleteSequenceAdmission(GpuUploadSequence sequence,
            BatchBuilder builder, bool publicationTerminal, GpuUploadAdmission admission,
            GpuUploadError error)
        {
            if (admission == GpuUploadAdmission.NotAdmitted)
            {
                builder.sequence = null;
                builder.sequenceOrdinal = 0;
                if (error == GpuUploadError.SequenceClosing)
                    sequence.sealedForAdmission = true;
                return;
            }
            sequence.nextOrdinal++;
            if (publicationTerminal) sequence.sealedForAdmission = true;
        }

        private static void InvalidateSequences()
        {
            for (int i = 0; i < sequences.Count; i++)
            {
                var sequence = sequences[i];
                if (sequence == null) continue;
                sequence.sealedForAdmission = true;
                sequence.closed = true;
            }
            sequences.Clear();
        }

        private static bool TryParseSequenceStatus(in GpuUploadAbi.SequenceStatus value,
            GpuUploadSequence sequence, out GpuUploadSequenceStatus status)
        {
            bool validError = GpuUploadAbi.TryParseError(value.resultCode, out var result);
            bool publicationExpected = (sequence.options &
                GpuUploadSequenceOptions.PublicationMarker) != 0;
            bool valid = value.structSize == GpuUploadAbi.SequenceStatusSize
                         && value.state >= (uint)GpuUploadSequenceState.Open
                         && value.state <= (uint)GpuUploadSequenceState.Complete
                         && value.gpuState <= (uint)GpuUploadGpuState.Failed
                         && value.contentState <= (uint)GpuUploadContentState.MayHaveChanged
                         && value.publicationState <=
                         (uint)GpuUploadPublicationState.Unknown
                         && validError && value.failedBatch >= -1 && value.failedRegion >= -1
                         && (value.failedBatch < 0
                             || (uint)value.failedBatch < value.admittedBatchCount)
                         && value.sourceConsumedBatchCount <= value.admittedBatchCount
                         && value.encodedBatchCount <= value.sourceConsumedBatchCount
                         && value.gpuTerminalBatchCount <= value.admittedBatchCount
                         && value.retiredBatchCount <= value.admittedBatchCount
                         && value.expectedBatchCount <= value.admittedBatchCount
                         && (value.flags & ~((uint)GpuUploadAbi.SequenceStatusFlags.CloseRequested
                                            | (uint)GpuUploadAbi.SequenceStatusFlags.TargetLeasesReleased)) == 0
                         && value.serial == sequence.serial
                         && value.graphicsDeviceEpoch == sequence.epoch
                         && (publicationExpected
                             ? value.publicationState !=
                               (uint)GpuUploadPublicationState.None
                             : value.publicationState ==
                               (uint)GpuUploadPublicationState.None)
                         && HasValidSequenceSemantics(value, result);
            if (!valid)
            {
                status = default;
                ObserveBackendError(GpuUploadError.AbiMismatch);
                return false;
            }
            status = new GpuUploadSequenceStatus(value);
            return true;
        }

        private static bool HasValidSequenceSemantics(in GpuUploadAbi.SequenceStatus value,
            GpuUploadError result)
        {
            var state = (GpuUploadSequenceState)value.state;
            var publication = (GpuUploadPublicationState)value.publicationState;
            switch (state)
            {
                case GpuUploadSequenceState.Open:
                    return result == GpuUploadError.None && value.expectedBatchCount == 0
                           && value.failedBatch == -1 && value.failedRegion == -1
                           && (publication == GpuUploadPublicationState.None
                               || publication == GpuUploadPublicationState.Pending);
                case GpuUploadSequenceState.Sealed:
                    return result == GpuUploadError.None && value.expectedBatchCount != 0
                           && value.expectedBatchCount == value.admittedBatchCount
                           && value.failedBatch == -1 && value.failedRegion == -1
                           && (publication == GpuUploadPublicationState.None
                               || publication == GpuUploadPublicationState.Pending);
                case GpuUploadSequenceState.Complete:
                    return result == GpuUploadError.None && value.expectedBatchCount != 0
                           && value.expectedBatchCount == value.admittedBatchCount
                           && value.encodedBatchCount == value.expectedBatchCount
                           && value.failedBatch == -1 && value.failedRegion == -1
                           && (publication == GpuUploadPublicationState.None
                               || publication == GpuUploadPublicationState.Published);
                case GpuUploadSequenceState.Failed:
                    return result != GpuUploadError.None
                           && value.expectedBatchCount == value.admittedBatchCount
                           && publication != GpuUploadPublicationState.Pending;
                case GpuUploadSequenceState.Aborted:
                    return result == GpuUploadError.None
                           && value.expectedBatchCount == value.admittedBatchCount
                           && value.failedBatch == -1 && value.failedRegion == -1
                           && (publication == GpuUploadPublicationState.None
                               || publication == GpuUploadPublicationState.Suppressed);
                default:
                    return false;
            }
        }

        private static unsafe bool IsSingleCommandPublicationMarkerRegion(BatchBuilder builder)
        {
            var regions = (GpuUploadAbi.Region*)((byte*)builder.blob.GetUnsafeReadOnlyPtr()
                                                  + builder.regionTableOffset);
            ref var region = ref regions[builder.regionCount - 1];
            if (region.targetIndex >= builder.targetCount || region.depth != 1
                                                       || region.layerCount != 1
                                                       || region.flags !=
                                                       (uint)GpuUploadAbi.RegionFlags.PublicationMarker)
                return false;
            var target = builder.targets[region.targetIndex];
            if (target == null || region.aspect > (uint)GpuUploadAspect.Stencil
                               || region.sourceRowPitch > int.MaxValue
                               || region.sourceImagePitch > int.MaxValue
                               || region.sourceLayerPitch > int.MaxValue)
                return false;
            var managedRegion = new GpuUploadRegion
            {
                MipLevel = (int)region.mipLevel,
                Aspect = (GpuUploadAspect)region.aspect,
                DestinationX = region.destinationX,
                DestinationY = region.destinationY,
                DestinationZ = region.destinationZ,
                Width = (int)region.width,
                Height = (int)region.height,
                Depth = (int)region.depth,
                BaseLayer = (int)region.baseLayer,
                LayerCount = (int)region.layerCount,
                SourceRowPitch = (int)region.sourceRowPitch,
                SourceImagePitch = (int)region.sourceImagePitch,
                SourceLayerPitch = (int)region.sourceLayerPitch
            };
            return IsSingleCommandPublicationRegion(target, managedRegion);
        }

        private static bool IsSingleCommandPublicationRegion(GpuUploadTarget target,
            in GpuUploadRegion region)
        {
            if (region.Depth != 1 || region.LayerCount != 1
                || !target.supports.TryGet(region.Aspect, out var supportInfo)
                || !TryCalculateRegionLayout(target.info.Description, supportInfo, region,
                    out var layout, out _))
                return false;
            ulong tightImagePitch = layout.RowBytes * (uint)layout.BlockRows;
            ulong tightLayerPitch = tightImagePitch * (uint)layout.BlockSlices;
            return layout.SourceRowPitch == layout.RowBytes
                   && layout.SourceImagePitch == tightImagePitch
                   && layout.SourceLayerPitch == tightLayerPitch;
        }
    }
}
