using UnityEngine;

namespace LightSide
{
    /// <summary>Queues a backend submission boundary without waiting for GPU completion.</summary>
    public static class GpuSubmitBoundary
    {
        /// <summary>Whether the active backend can submit queued graphics work.</summary>
        public static bool IsAvailable => GpuUpload.TryGetBoundaryEvent(out _, out _);

        /// <summary>Queues a boundary at the current main-thread graphics-stream position.</summary>
        public static bool TryInsert()
        {
            if (GpuUpload.TryGetBoundaryEvent(out var callback, out int id))
            {
                GL.IssuePluginEvent(callback, id);
                return true;
            }
            return false;
        }
    }
}
