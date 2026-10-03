using System;
using System.Runtime.InteropServices;

namespace LightSide
{
    internal static partial class GpuUploadAbi
    {
#if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS) && !UNITY_EDITOR
        internal const string LibName = "__Internal";
#else
        internal const string LibName = "lightside_gpu";
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_get_device_info(ref DeviceInfo info);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_query_support(ref SupportQuery query,
            uint queryBytes, ref SupportInfo info, uint outBytes);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_register_target(ref TargetRegistration registration,
            out TargetHandle handle);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_refresh_target(ref TargetRegistration registration,
            ref TargetHandle handle);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_unregister_target(ref TargetHandle handle);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_get_target_status(ref TargetHandle handle,
            ref TargetStatus status);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_acquire_slot(uint minBytes, out uint slotId,
            out uint slotGeneration, out uint capacityBytes, out IntPtr mapped);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_release_slot(uint slotId, uint slotGeneration);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_create_submission(IntPtr batch, int totalBytes);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_create_sequence(ref SequenceDesc descriptor,
            out ulong serial);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_seal_sequence(uint serialLow, uint serialHigh,
            uint expectedBatchCount);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_abort_sequence(uint serialLow, uint serialHigh);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_get_sequence_status(uint serialLow,
            uint serialHigh, ref SequenceStatus status);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_close_sequence(uint serialLow, uint serialHigh);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_get_stats(ref Stats stats);

        [DllImport("__Internal")]
        private static extern int ls_gpu_v6_abandon_session(out ulong graphicsDeviceEpoch);
#else
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_get_device_info(uint requestedMajor, ref DeviceInfo info,
            uint outBytes);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_query_support(ref SupportQuery query,
            uint queryBytes, ref SupportInfo info, uint outBytes);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ls_gpu_v6_get_upload_event();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ls_gpu_v6_get_boundary_event();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ls_gpu_v6_get_retire_event();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ls_gpu_v6_get_poll_event();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_register_target(ref TargetRegistration registration,
            out TargetHandle handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_refresh_target(TargetHandle handle,
            ref TargetRegistration registration, out TargetHandle refreshedHandle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_unregister_target(TargetHandle handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_get_target_status(TargetHandle handle,
            ref TargetStatus status, uint outBytes);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_acquire_slot(uint minBytes, out uint slotId,
            out uint slotGeneration, out uint capacityBytes, out IntPtr mapped);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_release_slot(uint slotId, uint slotGeneration);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_create_submission(IntPtr batch, uint bytes,
            out IntPtr eventData, out ulong serial);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_create_sequence(ref SequenceDesc descriptor,
            out ulong serial);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_seal_sequence(ulong serial,
            uint expectedBatchCount);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_abort_sequence(ulong serial);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_get_sequence_status(ulong serial,
            ref SequenceStatus status, uint outBytes);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_close_sequence(ulong serial);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_get_submission_status(ulong serial,
            ref SubmissionStatus status, uint outBytes);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_close_submission(IntPtr eventData);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_get_stats(ref Stats stats, uint outBytes);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ls_gpu_v6_abandon_session(out ulong graphicsDeviceEpoch);
#endif

        private static bool OffsetMatches<T>(string field, int expected) where T : struct
        {
            try
            {
                return Marshal.OffsetOf<T>(field).ToInt32() == expected;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryParseError(int value, out GpuUploadError error)
        {
            if ((uint)value <= (uint)GpuUploadError.SlotBusy)
            {
                error = (GpuUploadError)value;
                return true;
            }
            error = GpuUploadError.AbiMismatch;
            return false;
        }

        private static GpuUploadError ParseError(int value) =>
            TryParseError(value, out var error) ? error : GpuUploadError.AbiMismatch;

        private static bool IsZero(in TargetHandle handle) =>
            handle.token == 0 && handle.generation == 0 && handle.reserved == 0;

        private static bool IsValid(in TargetHandle handle) =>
            handle.token != 0 && handle.generation != 0 && handle.reserved == 0;

        internal static GpuUploadError GetDeviceInfo(ref DeviceInfo info)
        {
            info.structSize = DeviceInfoSize;
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_get_device_info(ref info));
#else
            return ParseError(ls_gpu_v6_get_device_info(Major, ref info, DeviceInfoSize));
#endif
        }

        internal static GpuUploadError QuerySupport(ref SupportQuery query,
            ref SupportInfo info)
        {
            query.structSize = SupportQuerySize;
            info.structSize = SupportInfoSize;
#if UNITY_WEBGL && !UNITY_EDITOR
            int rawError = ls_gpu_v6_query_support(ref query, SupportQuerySize,
                ref info, SupportInfoSize);
#else
            int rawError = ls_gpu_v6_query_support(ref query, SupportQuerySize,
                ref info, SupportInfoSize);
#endif
            return ParseError(rawError);
        }

        internal static GpuUploadError RegisterTarget(ref TargetRegistration registration,
            out TargetHandle handle, out bool contractViolation)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int rawError = ls_gpu_v6_register_target(ref registration, out handle);
#else
            int rawError = ls_gpu_v6_register_target(ref registration, out handle);
#endif
            bool validError = TryParseError(rawError, out var error);
            contractViolation = !validError
                                || (error == GpuUploadError.None && !IsValid(handle))
                                || (error != GpuUploadError.None && !IsZero(handle));
            if (contractViolation) return GpuUploadError.AbiMismatch;
            return error;
        }

        internal static GpuUploadError RefreshTarget(ref TargetRegistration registration,
            ref TargetHandle handle, out bool contractViolation)
        {
            var previous = handle;
#if UNITY_WEBGL && !UNITY_EDITOR
            int rawError = ls_gpu_v6_refresh_target(ref registration, ref handle);
            bool validError = TryParseError(rawError, out var error);
            bool validOutput = error == GpuUploadError.None
                ? IsValid(handle) && handle.token == previous.token
                                  && previous.generation != uint.MaxValue
                                  && handle.generation == previous.generation + 1
                : handle.token == previous.token && handle.generation == previous.generation
                                                && handle.reserved == previous.reserved;
#else
            int rawError = ls_gpu_v6_refresh_target(handle, ref registration,
                out var refreshedHandle);
            bool validError = TryParseError(rawError, out var error);
            bool validOutput = error == GpuUploadError.None
                ? IsValid(refreshedHandle) && refreshedHandle.token == previous.token
                                           && previous.generation != uint.MaxValue
                                           && refreshedHandle.generation == previous.generation + 1
                : IsZero(refreshedHandle);
            if (error == GpuUploadError.None) handle = refreshedHandle;
#endif
            if (!validError || !validOutput)
            {
                handle = previous;
                contractViolation = true;
                return GpuUploadError.AbiMismatch;
            }
            contractViolation = false;
            return error;
        }

        internal static GpuUploadError UnregisterTarget(ref TargetHandle handle)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_unregister_target(ref handle));
#else
            return ParseError(ls_gpu_v6_unregister_target(handle));
#endif
        }

        internal static GpuUploadError GetTargetStatus(ref TargetHandle handle, ref TargetStatus status)
        {
            status.structSize = TargetStatusSize;
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_get_target_status(ref handle, ref status));
#else
            return ParseError(ls_gpu_v6_get_target_status(handle, ref status, TargetStatusSize));
#endif
        }

        internal static GpuUploadError AcquireSlot(uint minBytes, out uint slotId,
            out uint slotGeneration, out uint capacityBytes, out IntPtr mapped,
            out bool contractViolation)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int rawError = ls_gpu_v6_acquire_slot(minBytes, out slotId, out slotGeneration,
                out capacityBytes, out mapped);
#else
            int rawError = ls_gpu_v6_acquire_slot(minBytes, out slotId, out slotGeneration,
                out capacityBytes, out mapped);
#endif
            bool validError = TryParseError(rawError, out var error);
            contractViolation = !validError
                                || (error == GpuUploadError.None
                                    && (mapped == IntPtr.Zero || slotGeneration == 0
                                        || capacityBytes < minBytes
                                        || capacityBytes > int.MaxValue))
                                || (error != GpuUploadError.None
                                    && (mapped != IntPtr.Zero || slotId != 0
                                        || slotGeneration != 0 || capacityBytes != 0));
            if (contractViolation) return GpuUploadError.AbiMismatch;
            return error;
        }

        internal static GpuUploadError ReleaseSlot(uint slotId, uint slotGeneration)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_release_slot(slotId, slotGeneration));
#else
            return ParseError(ls_gpu_v6_release_slot(slotId, slotGeneration));
#endif
        }

        internal static GpuUploadError CreateSubmission(IntPtr batch, uint bytes,
            out IntPtr eventData, out ulong serial, out bool contractViolation)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            eventData = IntPtr.Zero;
            serial = 0;
            contractViolation = false;
            return GpuUploadError.UnsupportedFeature;
#else
            int rawError = ls_gpu_v6_create_submission(batch, bytes, out eventData, out serial);
            bool validError = TryParseError(rawError, out var error);
            contractViolation = !validError
                                || (error == GpuUploadError.None
                                    && (eventData == IntPtr.Zero || serial == 0))
                                || (error != GpuUploadError.None
                                    && (eventData != IntPtr.Zero || serial != 0));
            if (contractViolation) return GpuUploadError.AbiMismatch;
            return error;
#endif
        }

        internal static GpuUploadError CreateSequence(ref SequenceDesc descriptor,
            out ulong serial, out bool contractViolation)
        {
            descriptor.structSize = SequenceDescSize;
#if UNITY_WEBGL && !UNITY_EDITOR
            int rawError = ls_gpu_v6_create_sequence(ref descriptor, out serial);
#else
            int rawError = ls_gpu_v6_create_sequence(ref descriptor, out serial);
#endif
            bool validError = TryParseError(rawError, out var error);
            contractViolation = !validError || error == GpuUploadError.None && serial == 0
                || error != GpuUploadError.None && serial != 0;
            if (contractViolation)
            {
                serial = 0;
                return GpuUploadError.AbiMismatch;
            }
            return error;
        }

        internal static GpuUploadError SealSequence(ulong serial, uint expectedBatchCount)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_seal_sequence((uint)serial,
                (uint)(serial >> 32), expectedBatchCount));
#else
            return ParseError(ls_gpu_v6_seal_sequence(serial, expectedBatchCount));
#endif
        }

        internal static GpuUploadError AbortSequence(ulong serial)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_abort_sequence((uint)serial,
                (uint)(serial >> 32)));
#else
            return ParseError(ls_gpu_v6_abort_sequence(serial));
#endif
        }

        internal static GpuUploadError GetSequenceStatus(ulong serial,
            ref SequenceStatus status)
        {
            status.structSize = SequenceStatusSize;
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_get_sequence_status((uint)serial,
                (uint)(serial >> 32), ref status));
#else
            return ParseError(ls_gpu_v6_get_sequence_status(serial, ref status,
                SequenceStatusSize));
#endif
        }

        internal static GpuUploadError CloseSequence(ulong serial)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_close_sequence((uint)serial,
                (uint)(serial >> 32)));
#else
            return ParseError(ls_gpu_v6_close_sequence(serial));
#endif
        }

        internal static GpuUploadError GetSubmissionStatus(ulong serial, ref SubmissionStatus status)
        {
            status.structSize = SubmissionStatusSize;
#if UNITY_WEBGL && !UNITY_EDITOR
            return GpuUploadError.SubmissionNotFound;
#else
            return ParseError(ls_gpu_v6_get_submission_status(serial, ref status,
                SubmissionStatusSize));
#endif
        }

        internal static GpuUploadError CloseSubmission(IntPtr eventData)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return GpuUploadError.UnsupportedFeature;
#else
            return ParseError(ls_gpu_v6_close_submission(eventData));
#endif
        }

        internal static GpuUploadError GetStats(ref Stats stats)
        {
            stats.structSize = StatsSize;
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_get_stats(ref stats));
#else
            return ParseError(ls_gpu_v6_get_stats(ref stats, StatsSize));
#endif
        }

        internal static GpuUploadError ExecuteWebGlBatch(IntPtr batch, int bytes)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ParseError(ls_gpu_v6_create_submission(batch, bytes));
#else
            return GpuUploadError.UnsupportedFeature;
#endif
        }

        internal static GpuUploadError AbandonSession(out ulong graphicsDeviceEpoch)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int rawError = ls_gpu_v6_abandon_session(out graphicsDeviceEpoch);
#else
            int rawError = ls_gpu_v6_abandon_session(out graphicsDeviceEpoch);
#endif
            bool validError = TryParseError(rawError, out var error);
            if (!validError || error == GpuUploadError.None && graphicsDeviceEpoch == 0
                            || error != GpuUploadError.None && graphicsDeviceEpoch != 0)
            {
                graphicsDeviceEpoch = 0;
                return GpuUploadError.AbiMismatch;
            }
            return error;
        }
    }
}
