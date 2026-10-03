using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightSide
{
    /// <summary>
    /// Thin adapter wiring <see cref="UniTextWorld"/> components into a shared <see cref="WorldBatcher"/> —
    /// the domain-agnostic mesh-combining engine in LightSide.Core. It forwards the components' lifecycle and
    /// render-data events to the engine's receptors and ticks it once per processing sweep; all batching,
    /// sharding and the domain-reload / Prefab-Stage lifecycle live in <see cref="WorldBatcher"/>.
    /// </summary>
    internal static class UniTextWorldBatcher
    {
        private static readonly WorldBatcher engine = new(16384, "UText");

        private static readonly HashSet<UniTextWorld> recollectRequests = new();
        private static readonly List<UniTextWorld> recollectScratch = new();

        /// <summary>Per-shard vertex packing target for world text; forwards to the shared engine instance.</summary>
        internal static int shardTargetVertexCount
        {
            get => engine.ShardTargetVertexCount;
            set => engine.ShardTargetVertexCount = value;
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void Initialize() => Hook();
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize() => Hook();
#endif

        private static void Hook()
        {
            UniTextWorld.Activated -= OnActivated;
            UniTextWorld.Activated += OnActivated;
            UniTextWorld.Deactivated -= OnDeactivated;
            UniTextWorld.Deactivated += OnDeactivated;
            UniTextBase.ProcessingEnded -= OnAfterProcess;
            UniTextBase.ProcessingEnded += OnAfterProcess;
        }

        private static void OnActivated(UniTextWorld comp)
        {
            engine.Activate(comp);
            comp.RenderDataAvailable += OnRenderDataAvailable;
            comp.RenderDataCleared += OnRenderDataCleared;
            comp.RenderSuppressionChanged += OnRenderSuppressionChanged;
            comp.SortingChanged += OnContextChanged;
            comp.ParentChanged += OnContextChanged;
        }

        private static void OnDeactivated(UniTextWorld comp)
        {
            comp.RenderDataAvailable -= OnRenderDataAvailable;
            comp.RenderDataCleared -= OnRenderDataCleared;
            comp.RenderSuppressionChanged -= OnRenderSuppressionChanged;
            comp.SortingChanged -= OnContextChanged;
            comp.ParentChanged -= OnContextChanged;
            recollectRequests.Remove(comp);
            engine.Deactivate(comp);
        }

        private static void OnContextChanged(UniTextWorld comp) => engine.ContextChanged(comp);

        /// <summary>Queues a component whose geometry changed outside the text pipeline (a range decoration). Drained before the upload, so the change still reaches this frame.</summary>
        internal static void RequestRecollect(UniTextWorld comp)
        {
            if (comp != null) recollectRequests.Add(comp);
        }

        private static void OnRenderDataAvailable(UniTextWorld comp) => engine.Capture(comp);
        private static void OnRenderDataCleared(UniTextWorld comp) => engine.ClearRenderData(comp);
        private static void OnRenderSuppressionChanged(UniTextWorld comp) => engine.SuppressionChanged(comp);

        private static void OnAfterProcess()
        {
            DrainRecollectRequests();
            engine.Flush();
        }

        private static void DrainRecollectRequests()
        {
            if (recollectRequests.Count == 0) return;
            recollectScratch.Clear();
            recollectScratch.AddRange(recollectRequests);
            recollectRequests.Clear();
            for (var i = 0; i < recollectScratch.Count; i++)
            {
                var comp = recollectScratch[i];
                if (comp != null) comp.RefreshBatchSegments();
            }
            recollectScratch.Clear();
        }
    }
}
