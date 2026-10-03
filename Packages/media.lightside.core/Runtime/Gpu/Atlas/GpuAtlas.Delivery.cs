using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LightSide
{
    public abstract partial class GpuAtlas<TEntry> where TEntry : struct, IGpuAtlasEntry
    {
        /// <summary>
        /// One deterministic backpressure round — the same-frame contract's engine: inserts a submit
        /// boundary so queued work reaches the render thread, drains it with a 1×1 readback wait (the
        /// GPU executes everything submitted so far, including our in-flight upload batches), then
        /// observes the retired tickets. The caller retries its operation after each round and fails
        /// closed when a FULL GPU drain freed nothing — with our own work in flight that is a wedged
        /// device, not a wait-longer situation. No timers, no spin-waits; a heavy frame is long, never
        /// stale. On WebGL the backend is synchronous and Pump alone is the drain.
        /// </summary>
        private bool TryDrainUploadProgress(out GpuUploadError error)
        {
            flushYields++;
            if (!GpuUpload.TryRequestProgress(out error)) return false;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (atlasRT != null && SystemInfo.supportsAsyncGPUReadback
                && SupportsRetirementReadback(atlasRT))
            {
                try
                {
                    var drain = AsyncGPUReadback.Request(atlasRT, 0, 0, 1, 0, 1, 0, 1);
                    drain.WaitForCompletion();
                }
                catch (Exception exception) when (exception is NotSupportedException
                                                  || exception is InvalidOperationException
                                                  || exception is ArgumentException
                                                  || exception is UnityException)
                {
                }
            }
            GpuUpload.Pump();
#endif
            if (!PollGpuUploadTickets())
            {
                error = LastUploadErrorOr(GpuUploadError.BackendFailed);
                return false;
            }
            error = GpuUploadError.None;
            return true;
        }

        protected bool AcquireUploadSlot(int bytes, out GpuUploadSlot slot,
            out GpuUploadError error)
        {
            while (true)
            {
                if (GpuUpload.TryAcquireSlot(bytes, out slot, out error)) return true;
                if (error != GpuUploadError.Backpressure) return false;
                int ticketsBefore = gpuUploadTicketCount;
                if (!TryDrainUploadProgress(out error)) return false;
                if (GpuUpload.TryAcquireSlot(bytes, out slot, out error)) return true;
                if (error != GpuUploadError.Backpressure) return false;
                if (gpuUploadTicketCount >= ticketsBefore)
                {
                    error = GpuUploadError.Backpressure;
                    return false;
                }
            }
        }

        protected bool BeginUploadBatch(out GpuUploadBatch batch, out GpuUploadError error)
        {
            while (true)
            {
                if (GpuUpload.TryBeginBatch(GpuUploadOverlapPolicy.AssumeNonOverlapping,
                        UploadBatchOptions, out batch, out error))
                    return true;
                if (error != GpuUploadError.Backpressure) return false;
                int ticketsBefore = gpuUploadTicketCount;
                if (!TryDrainUploadProgress(out error)) return false;
                if (GpuUpload.TryBeginBatch(GpuUploadOverlapPolicy.AssumeNonOverlapping,
                        UploadBatchOptions, out batch, out error))
                    return true;
                if (error != GpuUploadError.Backpressure) return false;
                if (gpuUploadTicketCount >= ticketsBefore)
                {
                    error = GpuUploadError.Backpressure;
                    return false;
                }
            }
        }

        protected bool EnsureUploadTicketSlot(out GpuUploadError error)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            error = GpuUploadError.None;
            return true;
#else
            error = GpuUploadError.None;
            while (gpuUploadTicketCount >= gpuUploadTickets.Length)
            {
                int ticketsBefore = gpuUploadTicketCount;
                if (!TryDrainUploadProgress(out error)) return false;
                if (gpuUploadTicketCount >= ticketsBefore)
                {
                    error = GpuUploadError.Backpressure;
                    return false;
                }
            }
            return true;
#endif
        }

        private GpuUploadError LastUploadErrorOr(GpuUploadError fallback) =>
            hasLastGpuUploadError ? lastGpuUploadError : fallback;

        private void RecordGpuUploadError(GpuUploadError error)
        {
            if (error == GpuUploadError.None) return;
            hasLastGpuUploadError = true;
            lastGpuUploadError = error;
        }

        protected void RecordFlushGpuUploadError(GpuUploadError error,
            ref FlushTransaction transaction)
        {
            RecordGpuUploadError(error);
            if (error != GpuUploadError.None) transaction.failure = error;
            transaction.graphicsStorageLost |= IsGraphicsStorageLost(error);
        }

        private static bool IsGraphicsStorageLost(GpuUploadError error) =>
            error == GpuUploadError.DeviceLost || error == GpuUploadError.ContextLost;

        /// <summary>
        /// Probes once per graphics-device epoch that this atlas's format uploads on the active
        /// backend; slot acquisition itself is synchronous, so a passed probe IS delivery readiness.
        /// Transient failures re-probe at the next requesting flush, recovery failures park until
        /// the epoch changes, terminal failures leave delivery unavailable
        /// (<see cref="NoteDeliveryPending"/> throws).
        /// </summary>
        private bool EnsureGpuUploadDelivery()
        {
            ulong currentEpoch = GpuUpload.Info.GraphicsDeviceEpoch;
            if (gpuUploadSupportEpoch != 0 && currentEpoch != gpuUploadSupportEpoch)
                ResetGpuUploadDeliveryConfiguration();
            if (gpuUploadSupportEpoch != 0 && gpuUploadDeliveryError == GpuUploadError.None)
                return true;
            if (gpuUploadDeliveryError != GpuUploadError.None
                && !IsTransientGpuUploadConfigurationError(gpuUploadDeliveryError))
                return false;
            var graphicsFormat = GraphicsFormatUtility.GetGraphicsFormat(textureFormat, !isLinear);
            if (!GpuUpload.TryMapFormat(graphicsFormat, out var format))
            {
                gpuUploadSupportEpoch = GpuUpload.Info.GraphicsDeviceEpoch;
                gpuUploadDeliveryError = GpuUploadError.UnsupportedFormat;
                RecordGpuUploadDeliveryFailure(gpuUploadDeliveryError);
                return false;
            }
            int mipCount = pixelTiles ? TileMipCount : 1;
            var description = GpuUploadResourceDescription.ForTexture2DArray(format,
                PageSize, PageSize, 1, mipCount);
            if (!GpuUpload.TryQuerySupport(description, GpuUploadAspect.Color,
                    out var support, out var error)
                || !support.Supported)
            {
                if (error == GpuUploadError.None) error = GpuUploadError.UnsupportedFormat;
                gpuUploadSupportEpoch = GpuUpload.Info.GraphicsDeviceEpoch;
                gpuUploadDeliveryError = error;
                RecordGpuUploadDeliveryFailure(error);
                return false;
            }
            gpuUploadSupportEpoch = GpuUpload.Info.GraphicsDeviceEpoch;
            gpuUploadDeliveryError = GpuUploadError.None;
            gpuUploadDeliveryFailureLogged = false;
            return true;
        }

        private void ResetGpuUploadDeliveryConfiguration()
        {
            gpuUploadSupportEpoch = 0;
            gpuUploadDeliveryError = GpuUploadError.None;
            gpuUploadTarget?.Close();
            gpuUploadTarget = null;
            gpuUploadTargetRegistrationError = GpuUploadError.None;
            gpuUploadTargetRegistrationEpoch = 0;
            gpuUploadDeliveryFailureLogged = false;
            uploadTargetRegistrationFailureLogged = false;
        }

        private void RecordGpuUploadDeliveryFailure(GpuUploadError error)
        {
            RecordGpuUploadError(error);
            if (gpuUploadDeliveryFailureLogged) return;
            gpuUploadDeliveryFailureLogged = true;
            logZone.MeowWarnFormat(
                "[{0}] GpuUpload delivery failed ({1}); {2}", Label, GpuUpload.Describe(error),
                IsTransientGpuUploadConfigurationError(error)
                    ? "the next requesting flush retries the operation"
                    : IsGraphicsRecoveryError(error)
                        ? "delivery resumes after the graphics-device epoch changes"
                        : "delivery is unavailable");
        }

        private static bool IsTransientGpuUploadConfigurationError(GpuUploadError error) =>
            error == GpuUploadError.NotInitialized
            || error == GpuUploadError.TargetBusy
            || error == GpuUploadError.Backpressure;

        private static bool IsGraphicsRecoveryError(GpuUploadError error) =>
            error == GpuUploadError.DeviceLost || error == GpuUploadError.ContextLost;

        private static bool IsTerminalGpuUploadError(GpuUploadError error) =>
            error != GpuUploadError.None
            && !IsTransientGpuUploadConfigurationError(error)
            && !IsGraphicsRecoveryError(error);

        /// <summary>Registers the current storage generation, retrying only transient failures or a new graphics epoch.</summary>
        private bool EnsureGpuUploadTarget()
        {
            if (atlasRT == null) return false;
            var currentEpoch = GpuUpload.Info.GraphicsDeviceEpoch;
            if (gpuUploadTargetRegistrationError != GpuUploadError.None
                && !IsTransientGpuUploadConfigurationError(gpuUploadTargetRegistrationError)
                && !IsGraphicsRecoveryError(gpuUploadTargetRegistrationError))
                throw new InvalidOperationException(
                    $"[{Label}] GPU upload target registration failed ({GpuUpload.Describe(gpuUploadTargetRegistrationError)}).");
            if (IsGraphicsRecoveryError(gpuUploadTargetRegistrationError)
                && gpuUploadTargetRegistrationEpoch == currentEpoch)
                return false;
            if (gpuUploadTarget != null)
            {
                if (gpuUploadTarget.State == GpuUploadTargetState.Active) return true;
                gpuUploadTarget.Close();
                gpuUploadTarget = null;
            }
            if (GpuUpload.TryRegister(atlasRT, out gpuUploadTarget, out var error))
            {
                uploadTargetRegistrationFailureLogged = false;
                gpuUploadTargetRegistrationError = GpuUploadError.None;
                gpuUploadTargetRegistrationEpoch = currentEpoch;
                return true;
            }
            RecordGpuUploadError(error);
            gpuUploadTargetRegistrationError = error;
            gpuUploadTargetRegistrationEpoch = currentEpoch;
            if (!IsTransientGpuUploadConfigurationError(error) && !IsGraphicsRecoveryError(error))
                throw new InvalidOperationException(
                    $"[{Label}] GPU upload target registration failed ({GpuUpload.Describe(error)}).");
            if (!uploadTargetRegistrationFailureLogged)
            {
                uploadTargetRegistrationFailureLogged = true;
                logZone.MeowWarnFormat(
                    "[{0}] GpuUpload target registration is waiting for {1} progress ({2})",
                    Label, IsGraphicsRecoveryError(error) ? "graphics recovery" : "backend", error);
            }
            return false;
        }

        /// <summary>Delivery readiness for benchmarks and tooling: Ready when the format-support probe passed for the current graphics epoch, Preparing while it is retryable, Unsupported when the device or plugin cannot deliver at all.</summary>
        public enum DeliveryPreparation : byte
        {
            Ready,
            Preparing,
            Unsupported
        }

        public DeliveryPreparation PrepareDelivery(out string reason)
        {
            reason = null;
            var format = GraphicsFormatUtility.GetGraphicsFormat(textureFormat, !isLinear);
            if (!GpuUpload.Supports(format, TextureDimension.Tex2DArray))
            {
                reason = $"GpuUpload does not support {format} Texture2DArray";
                return DeliveryPreparation.Unsupported;
            }
            bool ready = EnsureGpuUploadDelivery();
            if (gpuUploadTargetRegistrationError != GpuUploadError.None)
            {
                reason = $"GpuUpload target registration failed: {GpuUpload.Describe(gpuUploadTargetRegistrationError)}";
                return IsTerminalGpuUploadError(gpuUploadTargetRegistrationError)
                    ? DeliveryPreparation.Unsupported
                    : DeliveryPreparation.Preparing;
            }
            if (ready) return DeliveryPreparation.Ready;
            if (IsTerminalGpuUploadError(gpuUploadDeliveryError))
            {
                reason = $"GpuUpload delivery failed: {GpuUpload.Describe(gpuUploadDeliveryError)}";
                return DeliveryPreparation.Unsupported;
            }
            reason = gpuUploadDeliveryError != GpuUploadError.None
                ? $"GpuUpload delivery retry is pending: {gpuUploadDeliveryError}"
                : "GpuUpload delivery is pending";
            return DeliveryPreparation.Preparing;
        }

        /// <summary>Frame-start hook: invalidates a lost storage generation before auditing its in-flight upload tickets, polls retired storage, and opens the frame's allocation gate for the reclamation evaluation (which runs at <see cref="CommitPresentationAfterPublication"/> on the first untouched frame).</summary>
        public void BeginFrame()
        {
            if (atlasRT != null && !atlasRT.IsCreated()
                || gpuUploadTarget != null && gpuUploadTarget.State == GpuUploadTargetState.Stale)
            {
                logZone.MeowWarnFormat("[{0}] BeginFrame invalidate: rtNull={1} rtCreated={2} targetState={3}",
                    Label, atlasRT == null, atlasRT != null && atlasRT.IsCreated(),
                    gpuUploadTarget != null ? gpuUploadTarget.State.ToString() : "null");
                InvalidateAllContent(true);
                return;
            }
            if (!PollGpuUploadTickets())
            {
                var failure = LastUploadErrorOr(GpuUploadError.BackendFailed);
                logZone.MeowWarnFormat("[{0}] BeginFrame invalidate: ticket audit failed, lastErr={1}",
                    Label, failure);
                var ticketFailure = new InvalidOperationException(
                    $"[{Label}] GPU upload ticket failed ({GpuUpload.Describe(failure)}).");
                var terminal = IsTerminalGpuUploadError(failure);
                try
                {
                    InvalidateAllContent(IsGraphicsStorageLost(failure));
                }
                catch (Exception recoveryFailure)
                {
                    throw new AggregateException(
                        $"[{Label}] GPU ticket failure and recovery both failed.",
                        ticketFailure, recoveryFailure);
                }
                if (terminal) throw ticketFailure;
                return;
            }
            bool deferredPublicationHeld = !ReferenceEquals(deferredRetirementTexture, null)
                                           && ReferenceEquals(deferredRetirementTexture,
                                               publishedAtlasTexture);
            if (deferredPublicationHeld
                && (deferredRetirementTexture == null
                    || deferredRetirementTexture is RenderTexture deferredRT && !deferredRT.IsCreated()
                    || deferredRetirementTarget != null
                    && deferredRetirementTarget.State != GpuUploadTargetState.Active))
            {
                logZone.MeowWarnFormat("[{0}] BeginFrame invalidate: deferred publication lost (texNull={1}, targetState={2})",
                    Label, deferredRetirementTexture == null,
                    deferredRetirementTarget != null ? deferredRetirementTarget.State.ToString() : "null");
                InvalidateAllContent(true);
                return;
            }
            if (retiredTexturePolling) PollRetiredTextures();
            allocatedThisFrame = false;
            evictedThisFrame = 0;
        }

        /// <summary>
        /// The ONE recovery primitive: wipe the index, destroy storage, fire
        /// <see cref="AnyAtlasContentLost"/>, and let consumers re-collect from their sources (sources ARE the
        /// retained source). Covers device/context loss, terminal delivery failure, and failed mutations.
        /// When <paramref name="preservePublished"/> holds and nothing was written, the published texture
        /// generation is retained until the replacement presentation commits — stale consumers keep rendering
        /// last-good content because their tiles stay allocated (deferred frees) and still map into it.
        /// </summary>
        private void InvalidateAllContent(bool graphicsDeviceLost = false,
            bool preservePublished = false)
        {
            recoveryVersion++;
            logZone.MeowWarnFormat("[{0}] Atlas contents became invalid — wiping {1} entries for full re-rasterization",
                Label, entries.Count);

            preservePublished = preservePublished
                                && !graphicsDeviceLost
                                && deferredRetirementTexture != null
                                && ReferenceEquals(deferredRetirementTexture,
                                    publishedAtlasTexture)
                                && (!ReferenceEquals(atlasRT, publishedAtlasTexture)
                                    || gpuUploadTicketCount == 0)
                                && (publishedAtlasTexture is not RenderTexture publishedRT
                                    || publishedRT.IsCreated())
                                && (deferredRetirementTarget == null
                                    || deferredRetirementTarget.State
                                    == GpuUploadTargetState.Active);
            if (!ReferenceEquals(deferredRetirementTexture, null)
                && ReferenceEquals(atlasRT, deferredRetirementTexture))
            {
                atlasRT = null;
                gpuUploadTarget = null;
            }

            try
            {
                DestroyAtlasTexture(graphicsDeviceLost);
            }
            finally
            {
                ClearAtlasState();
                publicationRequiresPresentationCommit = preservePublished;
                try
                {
                    if (!preservePublished)
                    {
                        publishedAtlasTexture = null;
                        NotifyAtlasTextureChanged(null);
                    }
                }
                finally
                {
                    try
                    {
                        if (!preservePublished)
                            CompleteDeferredTextureRetirement(graphicsDeviceLost);
                        if (graphicsDeviceLost)
                            ReleaseRetiredTextures();
                    }
                    finally
                    {
                        NotifyAtlasContentLost();
                    }
                }
            }
        }

        /// <summary>Retains the last published texture while discarding a partially prepared atlas index and storage generation.</summary>
        public void RecoverAfterFailedMutation()
        {
            PreservePublishedStorageForRecovery();
            InvalidateAllContent(false, true);
        }

        /// <summary>Recovers from a failed mutation and preserves both failures if recovery also fails.</summary>
        public void RecoverAfterFailedMutation(Exception mutationFailure)
        {
            if (mutationFailure == null) throw new ArgumentNullException(nameof(mutationFailure));
            try
            {
                RecoverAfterFailedMutation();
            }
            catch (Exception recoveryFailure)
            {
                throw new AggregateException(
                    $"[{Label}] Atlas mutation and recovery both failed.",
                    mutationFailure, recoveryFailure);
            }
        }

        private void ClearAtlasState()
        {
            ClearGpuUploadTickets();
            foreach (var kvp in entries)
            {
                var e = kvp.Value;
                OnEntryDropped(in e);
            }
            ResetIndexState();
            deferredTileRetirements.Clear();
            publicationRequiresPresentationCommit = false;
            sliceCount = 0;
            OnAtlasStateCleared();
            ReturnPendingTileBuffers();
            batchProtected.Clear();
            ceilingOverflowLogged = false;
        }

        private bool TrackGpuUploadTicket(in GpuUploadTicket ticket)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            if (ticket.IsValid && gpuUploadTicketCount < gpuUploadTickets.Length)
            {
                gpuUploadTickets[gpuUploadTicketCount++] = ticket;
                return true;
            }
            RecordGpuUploadError(GpuUploadError.InternalError);
            return false;
#endif
        }

        private bool PollGpuUploadTickets()
        {
            for (int i = gpuUploadTicketCount - 1; i >= 0; i--)
            {
                if (!gpuUploadTickets[i].TryGetStatus(out var status))
                {
                    logZone.MeowWarnFormat("[{0}] ticket audit: SubmissionNotFound (ticket {1}/{2})",
                        Label, i, gpuUploadTicketCount);
                    FailGpuUploadTicket(GpuUploadError.SubmissionNotFound);
                    return false;
                }
                if (status.Error != GpuUploadError.None
                    || status.ContentState == GpuUploadContentState.MayHaveChanged
                    || status.GpuState == GpuUploadGpuState.Failed
                    || status.State == GpuUploadSubmissionState.Rejected
                    || status.State == GpuUploadSubmissionState.BackendFailed
                    || status.State == GpuUploadSubmissionState.DeviceLost
                    || status.State == GpuUploadSubmissionState.Cancelled)
                {
                    logZone.MeowWarnFormat("[{0}] ticket audit FAIL: err={1} state={2} gpu={3} content={4}",
                        Label, status.Error, status.State, status.GpuState, status.ContentState);
                    FailGpuUploadTicket(status.Error != GpuUploadError.None
                            ? status.Error
                            : status.State == GpuUploadSubmissionState.DeviceLost
                                ? GpuUploadError.DeviceLost
                                : GpuUploadError.BackendFailed);
                    return false;
                }
                if (status.State == GpuUploadSubmissionState.Pending
                    || status.GpuState == GpuUploadGpuState.Pending)
                    continue;
                if (status.ContentState != GpuUploadContentState.Changed
                    || status.State != GpuUploadSubmissionState.Encoded
                    && status.State != GpuUploadSubmissionState.Retired)
                {
                    logZone.MeowWarnFormat("[{0}] ticket audit UNEXPECTED: state={1} gpu={2} content={3}",
                        Label, status.State, status.GpuState, status.ContentState);
                    FailGpuUploadTicket(GpuUploadError.BackendFailed);
                    return false;
                }
                gpuUploadTicketCount--;
                gpuUploadTickets[i] = gpuUploadTickets[gpuUploadTicketCount];
                gpuUploadTickets[gpuUploadTicketCount] = default;
            }
            return true;
        }

        private void FailGpuUploadTicket(GpuUploadError error)
        {
            RecordGpuUploadError(error);
            ClearGpuUploadTickets();
        }

        private void ClearGpuUploadTickets()
        {
            if (gpuUploadTicketCount > 0)
                Array.Clear(gpuUploadTickets, 0, gpuUploadTicketCount);
            gpuUploadTicketCount = 0;
        }

    }
}
