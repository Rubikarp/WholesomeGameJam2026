using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shared base for modifiers that flow inline media — sprites, prefab instances — through text.
    /// Owns the catalog snapshot, the per-occurrence resolution of size, offset, advance and line space,
    /// the shaping-slot rewrite that reserves the media's advance, and the line-space reservation.
    /// How an occurrence becomes visible is the subclass's: a glyph in the text mesh
    /// (<see cref="SpriteModifier"/>) or a child object (<see cref="InlineObjectModifier{TEntry, TWrapper, TOverride}"/>).
    /// </summary>
    /// <remarks>Provider values are replaced by a matching keyed override and then by tag arguments.</remarks>
    /// <typeparam name="TEntry">The catalog entry type (must extend <see cref="InlineMedia"/>).</typeparam>
    [Serializable]
    public abstract partial class InlineMediaModifier<TEntry> : BaseModifier
        where TEntry : InlineMedia
    {
        /// <summary>An occurrence's entry plus its presentation values with the override chain already applied.</summary>
        protected struct ResolvedMedia
        {
            public TEntry entry;
            public Vector2 size;
            public Vector2 bearingOffset;
            public float advance;
            public float lineHeightAbove;
            public float lineHeightBelow;
            public Vector2 pivot;
            public float rotation;
        }

        /// <summary>
        /// An occurrence's position in the shaping output, produced and owned exclusively by the
        /// shaping pass. A slot is valid only while <c>buffers.shapedGlyphs</c> still holds the
        /// shaping run it was recorded from, so every read must first confirm
        /// <see cref="glyph"/> is below <c>buffers.shapedGlyphs.count</c>.
        /// </summary>
        private struct ShapedSlot
        {
            public int run;
            public int glyph;
        }

        private static readonly Vector2 inheritAxes = new(float.NaN, float.NaN);

        private FastIntDictionary<ResolvedMedia> clusterMedia;
        private FastIntDictionary<ShapedSlot> shapedSlots;
        private readonly HashSet<string> referencedLookupKeys =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The catalog this modifier consults. Concrete subclass exposes a typed provider field with the appropriate marker interface.</summary>
        protected abstract INamedCatalog<TEntry> Catalog { get; }

        /// <summary>
        /// Returns <see langword="true"/> when the entry's renderable payload is present (sprite
        /// assigned, prefab assigned). Entries failing this check are skipped at render time but
        /// still influence layout.
        /// </summary>
        /// <remarks>
        /// Invoked from the parallel mesh-generation phase, so implementations MUST be worker-thread
        /// safe — use <c>is not null</c> reference checks rather than Unity's overloaded
        /// <c>!= null</c>, which calls <c>EnsureRunningOnMainThread</c> and throws off the main
        /// thread. Destroyed Unity assets are filtered correctly later on the main thread.
        /// </remarks>
        protected abstract bool HasRenderable(TEntry entry);

        /// <summary>
        /// Hook for parsing the subclass's additional comma-separated tag arguments after the entry
        /// name (e.g. the <c>i</c> in <c>&lt;sprite=name,i&gt;</c>). Called once per applied range
        /// with the reader positioned right after the name token; consume exactly the subclass's own
        /// positional tokens — the base reads the shared presentation overrides (size, bearing
        /// offset, advance, line space above/below) immediately after. A missing token falls back to
        /// the keyed override and then the provider entry. When no override resolves, clear any stale
        /// per-cluster state so previous-rebuild overrides do not persist.
        /// </summary>
        protected virtual void OnExtraTokens(int cluster, ref ParameterReader reader) { }

        protected virtual void OnExtraTokens(int cluster, InlineMediaOverride mediaOverride,
            ref ParameterReader reader)
            => OnExtraTokens(cluster, ref reader);

        protected virtual InlineMediaOverride ResolveOverride(string key) => null;

        /// <summary>Runs before the shaping pass rewrites the occurrences' slots — the moment per-occurrence placement state from the previous shaping is stale. Worker thread.</summary>
        protected virtual void OnShapingStarted() { }

        /// <summary>
        /// Per-occurrence hook after the shaping slot took the media's advance and bearing offset.
        /// A subclass that renders through the mesh adds its own offsets to <paramref name="glyph"/>
        /// and records the occurrence's placement here. Also runs when a granular re-apply refreshes
        /// the slot in place. Worker thread.
        /// </summary>
        protected virtual void OnMediaShaped(int cluster, in ResolvedMedia media, ref ShapedGlyph glyph,
            float fontSize) { }

        /// <summary>Resolved occurrences of the current parse by source cluster; the per-rebuild placement pass reads it.</summary>
        protected FastIntDictionary<ResolvedMedia> ClusterMedia => clusterMedia;

        /// <summary>Entries of the captured catalog. Main thread, between <see cref="PrepareForParallel"/> calls.</summary>
        protected Dictionary<string, TEntry>.ValueCollection SnapshotEntries => catalogSnapshot.Values;

        private Action shapedCallback;
        private OrderedEventHandler<LineHeightContext> lineHeightCallback;
        private NamedCatalogChangedHandler<TEntry> catalogChangedCallback;

        private static readonly Func<TEntry, string> entryNameOf = static e => e?.Name;

        [NonSerialized] private readonly CatalogSnapshot<TEntry> catalogSnapshot = new();

        /// <summary>
        /// Captures the catalog into a name→entry map on the main thread before parallel layout:
        /// <see cref="OnApply"/> runs on the rebuild worker and must not reach the live catalog
        /// (settings access, asset <c>op_Equality</c> in <see cref="BoundNamedCatalog{TEntry,TAsset}"/>).
        /// Rebuilt only when the catalog reference or content changed; steady state allocates nothing.
        /// </summary>
        protected internal override void PrepareForParallel()
        {
            base.PrepareForParallel();
            if (!IsInitialized) catalogSnapshot.MarkDirty();
            catalogSnapshot.Prepare(Catalog, entryNameOf);
        }

        protected override void OnEnable()
        {
            clusterMedia ??= new FastIntDictionary<ResolvedMedia>(16);
            clusterMedia.Clear();
            shapedSlots ??= new FastIntDictionary<ShapedSlot>(16);
            shapedSlots.Clear();

            shapedCallback ??= OnShaped;
            lineHeightCallback ??= OnCalculateLineHeight;
            catalogChangedCallback ??= OnCatalogChanged;

            uniText.TextProcessor.Shaped.Subscribe(shapedCallback, -1000);
            uniText.TextProcessor.OnCalculateLineHeight.Subscribe(lineHeightCallback);

            var catalog = Catalog;
            if (catalog != null) catalog.Changed += catalogChangedCallback;
        }

        protected override void BeforeApply()
        {
            clusterMedia.Clear();
            referencedLookupKeys.Clear();
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
            uniText.TextProcessor.OnCalculateLineHeight.Unsubscribe(lineHeightCallback);

            var catalog = Catalog;
            if (catalog != null) catalog.Changed -= catalogChangedCallback;
        }

        protected override void OnDestroy()
        {
            clusterMedia?.Clear();
            clusterMedia = null;
            shapedSlots?.Clear();
            shapedSlots = null;
        }

        /// <summary>
        /// Re-subscribes a swapped provider when the modifier is currently active. Concrete
        /// subclasses call this from the <c>Provider</c> property setter.
        /// </summary>
        protected void RebindCatalog(INamedCatalog<TEntry> oldCatalog,
            INamedCatalog<TEntry> newCatalog)
        {
            catalogSnapshot.MarkDirty();
            if (!IsInitialized) return;
            catalogChangedCallback ??= OnCatalogChanged;
            if (oldCatalog != null) oldCatalog.Changed -= catalogChangedCallback;
            if (newCatalog != null) newCatalog.Changed += catalogChangedCallback;
        }

        protected void OnProviderStateChanged(IStateChangeSource source,
            in StateChange change)
        {
            if (source is TEntry)
            {
                ApplyEntryChange(source as IStateMemberReplay, change.Member);
                return;
            }
            MarkNestedStateChanged(UniTextDirty.Text, source, in change);
        }

        private void OnCatalogChanged(INamedCatalog<TEntry> _,
            in NamedCatalogChange<TEntry> change)
        {
            if (change.IsStructural || change.AffectsResolution)
            {
                catalogSnapshot.MarkDirty();
                if (!AffectsReferencedKey(in change)) return;
                ChangeSink?.MarkModifierChanged(this, UniTextDirty.Text,
                    structural: true);
                return;
            }
            if (!UsesEntry(change.Entry)) return;
            ApplyEntryChange(change.Source as IStateMemberReplay,
                change.SourceChange.Member);
        }

        private bool AffectsReferencedKey(
            in NamedCatalogChange<TEntry> change)
        {
            if (referencedLookupKeys.Count == 0) return false;
            if (change.Kind is StateChangeKind.Clear or StateChangeKind.ReplaceAll or
                StateChangeKind.Reset)
                return true;
            return (!string.IsNullOrEmpty(change.Name) &&
                    referencedLookupKeys.Contains(change.Name)) ||
                   (!string.IsNullOrEmpty(change.PreviousName) &&
                    referencedLookupKeys.Contains(change.PreviousName));
        }

        protected bool UsesLookupKey(string key)
            => !string.IsNullOrEmpty(key) && referencedLookupKeys.Contains(key);

        protected bool HasReferencedLookupKeys => referencedLookupKeys.Count != 0;

        private bool UsesEntry(TEntry entry)
        {
            if (entry == null || clusterMedia == null) return false;
            foreach (var pair in clusterMedia)
                if (ReferenceEquals(pair.Value.entry, entry)) return true;
            return false;
        }

        private void ApplyEntryChange(IStateMemberReplay source, StateMember member)
        {
            if (member == InlineMedia.Members.Advance ||
                member == InlineMedia.Members.LineHeightAbove ||
                member == InlineMedia.Members.LineHeightBelow)
            {
                ChangeSink?.MarkModifierChanged(this, UniTextDirty.Layout, source, member);
                return;
            }
            if (member == InlineMedia.Members.BearingOffset)
            {
                ChangeSink?.MarkModifierChanged(this, UniTextDirty.Positions, source, member);
                return;
            }
            if (member == InlineMedia.Members.Size || member == InlineMedia.Members.Pivot ||
                member == InlineMedia.Members.Rotation ||
                member == InlineSprite.Members.Sprite || member == InlineSprite.Members.Color ||
                member == InlineSprite.Members.PreserveAspect ||
                member == InlineObject.Members.Prefab)
            {
                ChangeSink?.MarkModifierChanged(this, UniTextDirty.Mesh, source, member);
                return;
            }
            if (member == InlineMedia.Members.Name)
            {
                ChangeSink?.MarkModifierChanged(this, UniTextDirty.Text, source, member);
                return;
            }
            throw new ArgumentOutOfRangeException(nameof(member));
        }

        internal override bool ReplayNestedStateMember(
            BaseModifier authoredModifier, IStateMemberReplay source,
            StateMember member)
            => authoredModifier is InlineMediaModifier<TEntry> authored &&
               TryReplayNestedState(authored, source, member);

        internal virtual bool TryReplayNestedState(
            InlineMediaModifier<TEntry> authored,
            IStateMemberReplay source, StateMember member)
        {
            if (source is not TEntry sourceEntry ||
                authored.Catalog is not InlineNamedCatalog<TEntry> sourceCatalog ||
                Catalog is not InlineNamedCatalog<TEntry> targetCatalog)
                return false;
            var sourceEntries = sourceCatalog.Entries;
            var targetEntries = targetCatalog.Entries;
            var index = -1;
            for (var i = 0; i < sourceEntries.Count; i++)
            {
                if (!ReferenceEquals(sourceEntries[i], sourceEntry)) continue;
                if (index >= 0) return false;
                index = i;
            }
            return (uint)index < (uint)targetEntries.Count &&
                   targetEntries[index] is IStateMemberReplay target &&
                   target.ReplayStateMember(source, member);
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var reader = context.Parameters.GetReader();
            if (!reader.Next(out var name) || name.IsEmpty) return;
            var lookupKey = SpanIntern.Get(name);
            referencedLookupKeys.Add(lookupKey);
            var mediaOverride = ResolveOverride(lookupKey);
            if (!catalogSnapshot.TryGet(lookupKey, out var entry) || entry == null) return;

            var start = context.Segment.Range.start;
            OnExtraTokens(start, mediaOverride, ref reader);

            reader.NextVector2(out var sizeValue,
                mediaOverride == null ? inheritAxes : mediaOverride.Size);
            reader.NextVector2(out var bearingValue,
                mediaOverride == null ? inheritAxes : mediaOverride.BearingOffset);
            reader.NextFloat(out var advanceValue,
                mediaOverride == null ? float.NaN : mediaOverride.Advance);
            reader.NextFloat(out var lineHeightAboveValue,
                mediaOverride == null ? float.NaN : mediaOverride.LineHeightAbove);
            reader.NextFloat(out var lineHeightBelowValue,
                mediaOverride == null ? float.NaN : mediaOverride.LineHeightBelow);
            reader.NextVector2(out var pivotValue,
                mediaOverride == null ? inheritAxes : mediaOverride.Pivot);
            reader.NextFloat(out var rotationValue,
                mediaOverride == null ? float.NaN : mediaOverride.Rotation);

            var resolved = new ResolvedMedia
            {
                entry = entry,
                size = ResolveAxes(sizeValue, entry.Size),
                bearingOffset = ResolveAxes(bearingValue, entry.BearingOffset),
                advance = float.IsNaN(advanceValue) ? entry.Advance : advanceValue,
                lineHeightAbove = float.IsNaN(lineHeightAboveValue) ? entry.LineHeightAbove : lineHeightAboveValue,
                lineHeightBelow = float.IsNaN(lineHeightBelowValue) ? entry.LineHeightBelow : lineHeightBelowValue,
                pivot = ResolveAxes(pivotValue, entry.Pivot),
                rotation = float.IsNaN(rotationValue) ? entry.Rotation : rotationValue,
            };
            clusterMedia[start] = resolved;
            if (shapedSlots.TryGetValue(start, out var slot) && slot.glyph < buffers.shapedGlyphs.count)
                RefreshShapedMetrics(start, in slot, in resolved);
        }

        private static Vector2 ResolveAxes(Vector2 value, Vector2 fallback) => new(
            float.IsNaN(value.x) ? fallback.x : value.x,
            float.IsNaN(value.y) ? fallback.y : value.y);

        private void OnShaped()
        {
            shapedSlots.Clear();
            OnShapingStarted();
            if (clusterMedia == null || clusterMedia.Count == 0) return;

            var buf = buffers;
            var fontSize = buf.shapingFontSize > 0 ? buf.shapingFontSize : uniText.FontSize;
            var glyphs = buf.shapedGlyphs.data;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                float width = 0;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var globalCluster = glyphs[g].cluster;
                    if (clusterMedia.TryGetValue(globalCluster, out var media))
                    {
                        shapedSlots[globalCluster] = new ShapedSlot { run = r, glyph = g };
                        ref var glyph = ref glyphs[g];
                        glyph.glyphId = ShapedGlyph.NoGlyph;
                        glyph.advanceX = media.advance * fontSize;
                        glyph.offsetX = media.bearingOffset.x * fontSize;
                        glyph.offsetY = media.bearingOffset.y * fontSize;
                        OnMediaShaped(globalCluster, in media, ref glyph, fontSize);
                    }
                    width += glyphs[g].advanceX;
                }

                run.width = width;
            }
        }

        private void OnCalculateLineHeight(ref LineHeightContext context)
        {
            if (clusterMedia == null || clusterMedia.Count == 0) return;

            var above = 0f;
            var below = 0f;
            foreach (var kv in clusterMedia)
            {
                if (kv.Key < context.startCluster || kv.Key >= context.endCluster) continue;
                if (kv.Value.lineHeightAbove > above) above = kv.Value.lineHeightAbove;
                if (kv.Value.lineHeightBelow > below) below = kv.Value.lineHeightBelow;
            }

            uniText.TextProcessor.ReserveLineSpace(context.lineIndex,
                above * context.fontSize, below * context.fontSize);
        }

        private void RefreshShapedMetrics(int cluster, in ShapedSlot slot, in ResolvedMedia current)
        {
            var fontSize = buffers.shapingFontSize > 0
                ? buffers.shapingFontSize
                : uniText.FontSize;
            ref var glyph = ref buffers.shapedGlyphs.data[slot.glyph];
            var advance = current.advance * fontSize;
            var advanceDelta = advance - glyph.advanceX;
            glyph.advanceX = advance;
            glyph.offsetX = current.bearingOffset.x * fontSize;
            glyph.offsetY = current.bearingOffset.y * fontSize;
            OnMediaShaped(cluster, in current, ref glyph, fontSize);
            buffers.shapedRuns.data[slot.run].width += advanceDelta;
            buffers.cpWidths.data[cluster] += advanceDelta;
        }
    }

    /// <summary>Inline-media modifier with typed per-entry overrides.</summary>
    [Serializable]
    public abstract partial class InlineMediaModifier<TEntry, TOverride> :
        InlineMediaModifier<TEntry>
        where TEntry : InlineMedia
        where TOverride : InlineMediaOverride
    {
        /// <summary>Presentation overrides for individual provider entries.</summary>
        [SerializeField, StateList(nameof(ApplyOverridesChange), Owned = true,
            AllowNullItems = false, AllowDuplicateReferences = false)]
        [Tooltip("Presentation overrides for individual provider entries.")]
        private StyledList<TOverride> overrides = new();

        [NonSerialized] private readonly Dictionary<string, TOverride> overrideSnapshot =
            new(StringComparer.OrdinalIgnoreCase);
        [NonSerialized] private ReferenceBinding<TOverride> boundOverrides;
        [NonSerialized] private Dictionary<TOverride, string> boundOverrideKeys;
        [NonSerialized] private bool overridesDirty = true;

        internal override void SetChangeSink(IModifierChangeSink sink)
        {
            base.SetChangeSink(sink);
            if (sink == null) boundOverrides?.Clear();
            else BindOverrides();
        }

        protected internal override void PrepareForParallel()
        {
            base.PrepareForParallel();
            if (!Application.isPlaying) overridesDirty = true;
            if (!overridesDirty) return;
            overridesDirty = false;
            overrideSnapshot.Clear();
            for (var i = 0; i < overrides.Count; i++)
            {
                var mediaOverride = overrides[i];
                if (!string.IsNullOrEmpty(mediaOverride.Key))
                    overrideSnapshot[mediaOverride.Key] = mediaOverride;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            overridesDirty = true;
            BindOverrides();
        }

        protected override void OnDestroy()
        {
            boundOverrides?.Clear();
            base.OnDestroy();
        }

        protected sealed override InlineMediaOverride ResolveOverride(string key)
            => overrideSnapshot.TryGetValue(key, out var mediaOverride)
                ? mediaOverride
                : null;

        protected sealed override void OnExtraTokens(int cluster,
            InlineMediaOverride mediaOverride, ref ParameterReader reader)
            => OnExtraTokens(cluster, (TOverride)mediaOverride, ref reader);

        protected virtual void OnExtraTokens(int cluster, TOverride mediaOverride,
            ref ParameterReader reader)
            => OnExtraTokens(cluster, ref reader);

        private void ApplyOverridesChange(in StateListMutation<TOverride> mutation)
        {
            BindOverrides();
            overridesDirty = true;
            if (!AffectsReferencedOverride(in mutation)) return;
            ChangeSink?.MarkModifierChanged(this, UniTextDirty.Text,
                structural: true);
        }

        private bool AffectsReferencedOverride(
            in StateListMutation<TOverride> mutation)
        {
            if (mutation.Kind is StateListMutationKind.Clear or
                StateListMutationKind.ReplaceAll)
                return HasReferencedLookupKeys;
            mutation.TryGetCurrentItem(out var current);
            return UsesLookupKey(current?.Key) ||
                   UsesLookupKey(mutation.PreviousItem?.Key);
        }

        private void BindOverrides()
        {
            boundOverrides ??= new ReferenceBinding<TOverride>(
                ConnectOverride, DisconnectOverride);
            boundOverrides.Reconcile(overrides);
        }

        private void ConnectOverride(TOverride source)
        {
            boundOverrideKeys ??= new Dictionary<TOverride, string>(
                ReferenceIdentityComparer<TOverride>.Instance);
            boundOverrideKeys[source] = source.Key;
            source.Changed += OnOverrideChanged;
        }

        private void DisconnectOverride(TOverride source)
        {
            source.Changed -= OnOverrideChanged;
            boundOverrideKeys?.Remove(source);
        }

        private void OnOverrideChanged(IStateChangeSource source, in StateChange change)
        {
            var member = change.Member;
            if (source is not TOverride mediaOverride)
                throw new ArgumentOutOfRangeException(nameof(source));
            if (member == InlineMediaOverride.Members.Key)
            {
                overridesDirty = true;
                boundOverrideKeys ??= new Dictionary<TOverride, string>(
                    ReferenceIdentityComparer<TOverride>.Instance);
                boundOverrideKeys.TryGetValue(mediaOverride, out var previousKey);
                boundOverrideKeys[mediaOverride] = mediaOverride.Key;
                if (!UsesLookupKey(previousKey) && !UsesOverride(mediaOverride)) return;
                ChangeSink?.MarkModifierChanged(this, UniTextDirty.Text,
                    source as IStateMemberReplay, member);
                return;
            }
            if (!UsesOverride(mediaOverride)) return;
            if (member == InlineMediaOverride.Members.Advance ||
                member == InlineMediaOverride.Members.LineHeightAbove ||
                member == InlineMediaOverride.Members.LineHeightBelow)
            {
                ChangeSink?.MarkModifierChanged(this, UniTextDirty.Layout,
                    source as IStateMemberReplay, member);
                return;
            }
            if (member == InlineMediaOverride.Members.BearingOffset)
            {
                ChangeSink?.MarkModifierChanged(this, UniTextDirty.Positions,
                    source as IStateMemberReplay, member);
                return;
            }
            if (member == InlineMediaOverride.Members.Size ||
                member == InlineMediaOverride.Members.Pivot ||
                member == InlineMediaOverride.Members.Rotation ||
                member == InlineSpriteOverride.Members.Color ||
                member == InlineSpriteOverride.Members.PreserveAspect)
            {
                ChangeSink?.MarkModifierChanged(this, UniTextDirty.Mesh,
                    source as IStateMemberReplay, member);
                return;
            }
            throw new ArgumentOutOfRangeException(nameof(change));
        }

        private bool UsesOverride(TOverride candidate)
        {
            if (!UsesLookupKey(candidate.Key)) return false;
            for (var i = overrides.Count - 1; i >= 0; i--)
            {
                var current = overrides[i];
                if (!string.Equals(current.Key, candidate.Key,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                return ReferenceEquals(current, candidate);
            }
            return false;
        }

        internal override bool TryReplayNestedState(
            InlineMediaModifier<TEntry> authored,
            IStateMemberReplay source, StateMember member)
        {
            if (base.TryReplayNestedState(authored, source, member)) return true;
            if (authored is not InlineMediaModifier<TEntry, TOverride>
                authoredOverrides)
                return false;
            var authoredEntries = authoredOverrides.overrides;
            var entries = overrides;
            for (var i = 0; i < authoredEntries.Count; i++)
            {
                if (!ReferenceEquals(authoredEntries[i], source)) continue;
                return (uint)i < (uint)entries.Count &&
                       entries[i] is IStateMemberReplay target &&
                       target.ReplayStateMember(source, member);
            }
            return false;
        }
    }

    /// <summary>
    /// Inline-media modifier whose occurrences are child objects (prefab instances) positioned over
    /// the text: owns the wrapper pools and their lifecycle across rebuilds.
    /// </summary>
    /// <typeparam name="TEntry">The catalog entry type (must extend <see cref="InlineMedia"/>).</typeparam>
    /// <typeparam name="TWrapper">The <see cref="MediaWrapper"/> subclass that materialises each on-screen occurrence.</typeparam>
    /// <typeparam name="TOverride">The keyed presentation-override type.</typeparam>
    [Serializable]
    public abstract partial class InlineObjectModifier<TEntry, TWrapper, TOverride> :
        InlineMediaModifier<TEntry, TOverride>
        where TEntry : InlineMedia
        where TWrapper : MediaWrapper, new()
        where TOverride : InlineMediaOverride
    {
        /// <summary>
        /// Per-entry wrapper pools owned by this modifier instance. The pool and its active-count
        /// must live here, not on the shared <typeparamref name="TEntry"/>: a catalog asset hands
        /// the same entry to every component by reference, so storing per-occurrence state on the
        /// entry makes concurrently visible components (which rebuild on parallel worker threads)
        /// collide on one another's instances. Pools are created lazily on first render, reset each
        /// rebuild, and pruned once empty so entries dropped from the text or catalog tear their
        /// GameObjects down rather than leaking.
        /// </summary>
        private readonly Dictionary<TEntry, InstancePool> pools = new();

        /// <summary>
        /// Copies the per-frame visual state from <paramref name="entry"/> into <paramref name="wrapper"/>
        /// (prefab reference, etc.). <paramref name="cluster"/> is the source-text cluster index of this
        /// occurrence — subclasses use it to apply per-occurrence overrides parsed by
        /// <c>OnExtraTokens</c> from extra tag arguments.
        /// </summary>
        protected abstract void ConfigureWrapper(TWrapper wrapper, TEntry entry, int cluster);

        private Action<UniTextCommitChanges> committedCallback;
        private Action deferredCleanupCallback;
        private Action rebuildStartCallback;
        private Action rebuildEndCallback;

        protected override void OnEnable()
        {
            base.OnEnable();

            committedCallback ??= OnCommitted;
            deferredCleanupCallback ??= CleanupOnDisable;
            rebuildStartCallback ??= OnRebuildStart;
            rebuildEndCallback ??= OnRebuildEnd;

            uniText.LayoutCommitted -= deferredCleanupCallback;
            uniText.Committed += committedCallback;
            uniText.MeshGenerator.onRebuildStart.Subscribe(rebuildStartCallback);
            uniText.MeshGenerator.onRebuildEnd.Subscribe(rebuildEndCallback);
        }

        /// <summary>
        /// Unsubscribes and schedules a one-shot pool trim on the host's next
        /// <see cref="UniTextBase.LayoutCommitted"/>: when this cycle no longer applies the modifier
        /// (style removed, tag deleted), the surviving wrappers are destroyed right after the rebuild
        /// that dropped them; a re-parse that re-enables cancels the deferred trim in
        /// <see cref="OnEnable"/> and the wrappers are reused instead.
        /// </summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            uniText.Committed -= committedCallback;
            uniText.MeshGenerator.onRebuildStart.Unsubscribe(rebuildStartCallback);
            uniText.MeshGenerator.onRebuildEnd.Unsubscribe(rebuildEndCallback);

            foreach (var kv in pools) kv.Value.activeCount = 0;

            uniText.LayoutCommitted -= deferredCleanupCallback;
            uniText.LayoutCommitted += deferredCleanupCallback;
        }

        /// <summary>
        /// Destroys the wrapper GameObjects synchronously. This structural teardown (text emptied,
        /// component disabled or destroyed) is not followed by another rebuild, so the deferred
        /// cleanup that <see cref="OnDisable"/> schedules would never fire. Always runs on the
        /// main thread.
        /// </summary>
        protected override void OnDestroy()
        {
            if (uniText != null && deferredCleanupCallback != null)
                uniText.LayoutCommitted -= deferredCleanupCallback;

            foreach (var kv in pools) kv.Value.DestroyAll();
            pools.Clear();
            base.OnDestroy();
        }

        private void OnRebuildStart()
        {
            if (!uniText.MeshGenerator.captureGlyphGeometry) return;
            foreach (var kv in pools) kv.Value.activeCount = 0;
        }

        private void OnRebuildEnd()
        {
            if (!uniText.MeshGenerator.captureGlyphGeometry) return;
            var clusterMedia = ClusterMedia;
            if (clusterMedia == null || clusterMedia.Count == 0) return;

            var glyphs = uniText.ResultGlyphs;
            var fontSize = uniText.MeshGenerator.FontSize;
            var hiddenFlags = buffers.hiddenClusters.data;
            var hiddenCount = buffers.hiddenClusters.count;

            for (var i = 0; i < glyphs.Length; i++)
            {
                var cluster = glyphs[i].cluster;
                if ((uint)cluster < (uint)hiddenCount && hiddenFlags[cluster] != 0) continue;
                if (!clusterMedia.TryGetValue(cluster, out var media)) continue;
                if (!HasRenderable(media.entry)) continue;

                var glyph = glyphs[i];
                CreateInstance(in media, cluster,
                    glyph.x + media.bearingOffset.x * fontSize,
                    -glyph.y + media.bearingOffset.y * fontSize,
                    media.size.x * fontSize,
                    media.size.y * fontSize);
            }
        }

        private void CreateInstance(in ResolvedMedia media, int cluster, float x, float y, float w, float h)
        {
            if (uniText == null) return;

            if (!pools.TryGetValue(media.entry, out var pool))
            {
                pool = new InstancePool();
                pools[media.entry] = pool;
            }

            var instances = pool.instances;
            var index = pool.activeCount++;

            TWrapper wrapper;
            if (index < instances.Count)
            {
                wrapper = instances[index];
            }
            else
            {
                wrapper = new TWrapper { owner = uniText };
                instances.Add(wrapper);
            }

            ConfigureWrapper(wrapper, media.entry, cluster);
            wrapper.isDirty = true;
            wrapper.pivot = media.pivot;
            wrapper.rotation = media.rotation;
            var pad = uniText.Padding;
            wrapper.anchoredPosition = new Vector2(x + w * media.pivot.x + pad.x, y + h * media.pivot.y - pad.w);
            wrapper.sizeDelta = new Vector2(w, h);
        }

        private void CleanupOnDisable()
        {
            if (uniText == null) return;
            uniText.LayoutCommitted -= deferredCleanupCallback;
            UpdateAndPrunePools();
        }

        private void OnCommitted(UniTextCommitChanges changes)
        {
            if ((changes & UniTextCommitChanges.GlyphGeometry) != 0)
                UpdateAndPrunePools();
        }

        private void UpdateAndPrunePools()
        {
            if (pools.Count == 0) return;

            List<TEntry> toRemove = null;
            foreach (var kv in pools)
            {
                var pool = kv.Value;
                pool.Update();
                if (pool.activeCount == 0 && pool.instances.Count == 0)
                {
                    toRemove ??= new List<TEntry>();
                    toRemove.Add(kv.Key);
                }
            }
            if (toRemove != null)
                foreach (var entry in toRemove) pools.Remove(entry);
        }

        /// <summary>
        /// One component's wrapper pool for a single entry. <see cref="activeCount"/> counts the
        /// occurrences claimed in the current rebuild (reset to 0 at rebuild start); because each
        /// claim either reuses or appends a wrapper, after a rebuild <see cref="instances"/> always
        /// holds at least <see cref="activeCount"/> wrappers — so <see cref="Update"/> only trims
        /// the surplus, never has to grow.
        /// </summary>
        private sealed class InstancePool
        {
            public int activeCount;
            public readonly List<TWrapper> instances = new();

            public void Update()
            {
                for (var i = instances.Count - 1; i >= activeCount; i--)
                {
                    instances[i].Destroy();
                    instances.RemoveAt(i);
                }

                for (var i = 0; i < instances.Count; i++)
                    instances[i].Setup();
            }

            public void DestroyAll()
            {
                for (var i = 0; i < instances.Count; i++)
                    instances[i].Destroy();
                instances.Clear();
                activeCount = 0;
            }
        }
    }
}
