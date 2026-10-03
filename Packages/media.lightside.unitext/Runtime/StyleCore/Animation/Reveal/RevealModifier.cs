using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Receives <see cref="RevealModifier.GlyphRevealing"/> notifications.</summary>
    public delegate void RevealGlyphHandler(in RevealGlyphInfo info);

    /// <summary>
    /// Reveal state of one rendered glyph, delivered mid mesh build. Mutate the current quad through
    /// <see cref="generator"/> (<c>faceBaseIdx</c>, <c>Vertices</c>, <c>Colors</c>) to animate its
    /// appearance any way you want.
    /// </summary>
    public readonly struct RevealGlyphInfo
    {
        /// <summary>Mesh generator mid-build, positioned on the glyph's quad.</summary>
        public readonly UniTextMeshGenerator generator;

        /// <summary>
        /// The text being built. Reach the layout through it — <see cref="UniTextBase.GetRangeBounds"/>
        /// over <see cref="unit"/> gives the unit's settled box, which is what an effect that moves a
        /// whole word or line as one body needs.
        /// </summary>
        public readonly UniTextBase text;

        /// <summary>Codepoint index of the glyph's cluster.</summary>
        public readonly int cluster;

        /// <summary>
        /// Codepoint span of the unit the glyph belongs to — every cluster of it shares this glyph's
        /// <see cref="ordinal"/> and <see cref="Progress"/>. One cluster wide while the frontier
        /// counts in clusters.
        /// </summary>
        public readonly TextRange unit;

        /// <summary>Reveal order of the glyph's cluster within its range, 0-based.</summary>
        public readonly int ordinal;

        /// <summary>Revealable clusters in the range.</summary>
        public readonly int count;

        /// <summary>Fractional reveal frontier in ordinal units, growing 0 → <see cref="count"/>.</summary>
        public readonly float front;

        /// <summary>Whether the glyph is leaving the visible part of its range rather than entering it.</summary>
        public readonly bool hiding;

        /// <summary>
        /// Normalized appearance state, 1 being the settled glyph in both directions: an appearing
        /// glyph runs 0 → 1, a <see cref="hiding"/> one 1 → 0. Serialized handlers receive their
        /// one-shot timeline; <see cref="RevealModifier.GlyphRevealing"/> subscribers receive the
        /// fractional frontier envelope derived from <see cref="front"/> minus <see cref="ordinal"/>.
        /// </summary>
        public float Progress { get; }

        /// <summary>
        /// Creates the state of a glyph riding the frontier envelope, taking
        /// <see cref="Progress"/> from <paramref name="front"/> minus <paramref name="ordinal"/>.
        /// </summary>
        public RevealGlyphInfo(UniTextBase text, UniTextMeshGenerator generator, int cluster,
            TextRange unit, int ordinal, int count, float front, bool hiding = false)
            : this(text, generator, cluster, unit, ordinal, count, front,
                Mathf.Clamp01(front - ordinal), hiding)
        {
        }

        /// <summary>
        /// The same glyph on an explicit timeline — what a handler wrapping other handlers gives a
        /// child to run <paramref name="progress"/> of its own. Keep 1 as the settled glyph, or the
        /// child never resolves to the identity transform.
        /// </summary>
        public RevealGlyphInfo WithProgress(float progress)
            => new(text, generator, cluster, unit, ordinal, count, front, progress, hiding);

        /// <summary>
        /// The same glyph against a restated frontier, its <see cref="Progress"/> derived from the new
        /// <paramref name="front"/> the way the envelope derives it — what a handler that re-times the
        /// frontier itself, rather than one glyph's timeline, hands on.
        /// </summary>
        public RevealGlyphInfo WithFront(float front)
            => new(text, generator, cluster, unit, ordinal, count, front, hiding);

        private RevealGlyphInfo(UniTextBase text, UniTextMeshGenerator generator, int cluster,
            TextRange unit, int ordinal, int count, float front, float progress, bool hiding)
        {
            this.text = text;
            this.generator = generator;
            this.cluster = cluster;
            this.unit = unit;
            this.ordinal = ordinal;
            this.count = count;
            this.front = front;
            this.hiding = hiding;
            Progress = progress;
        }
    }

    /// <summary>
    /// Shows only the leading part of each covered range and hides the rest — the engine piece
    /// behind typewriter-style text reveal.
    /// </summary>
    /// <remarks>
    /// Visibility is governed by the <see cref="Param.Front"/> parameter together with
    /// <see cref="Collapse"/>: set the <see cref="Front"/> field, author it in markup
    /// (<c>&lt;reveal=fade,50%&gt;</c>), or own it per range. The first tag parameter selects the
    /// appearance effect by <see cref="RevealHandlerEntry"/> name
    /// (<c>&lt;reveal=fade&gt;</c>). Clusters are revealed in logical order, whole grapheme
    /// clusters at a time — along one shared text-order frontier, or independently per range
    /// when <see cref="PerRange"/> is on. A cluster covered by overlapping ranges belongs to
    /// the innermost one — it alone governs the cluster's visibility and appearance effect.
    /// Line breaks are never hidden, so the line
    /// structure stays stable while text reveals. Appearance is binary by default; add named
    /// entries or subscribe <see cref="GlyphRevealing"/> to animate it. A receding frontier
    /// animates too: the cluster keeps its place in the mesh — and, where its range collapses,
    /// in layout — until its hide effect finishes.
    /// <para>
    /// <see cref="Collapse"/> is a parameter like the rest, so one modifier serves ranges that
    /// differ in it: <c>&lt;reveal=,4abs,line,true&gt;</c> clamps a body out of the layout while
    /// <c>&lt;reveal=fade,0&gt;</c> elsewhere merely holds text back from the mesh. A collapsing
    /// range is settled a pipeline stage earlier than a drawing one, so its frontier moves through
    /// a re-parse where the other moves on a mesh rebuild.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 4)]
    [TypeDescription("Shows a controllable fraction of the text, hiding the rest.")]
    [GenerateParameters]
    public partial class RevealModifier : BaseModifier
    {

        private const byte AnimationListed = 1;
        private const byte AnimationHiding = 2;

        private struct RevealRange
        {
            public int start;
            public int end;
            public RangeApplyMemo memo;
            public UnitValue front;
            public TextUnit unit;
            public bool collapse;
            public string reserveFor;
            public RevealHandler handler;
            public RevealHandler hideHandler;

            public int shownStart;
            public int shownEnd;
            public bool clipping;
        }

        /// <summary>
        /// Pipeline stage a range's verdict is written in, decided by its own <see cref="Collapse"/>
        /// and unit: text leaving the layout must be marked before whatever consumes it, while text
        /// that merely stops being drawn can wait for the mesh.
        /// </summary>
        private enum RevealStage : byte
        {
            /// <summary>Before every mesh build, which is what makes a per-frame front cheap.</summary>
            Mesh,

            /// <summary>Before itemization, so shaping never sees the hidden clusters.</summary>
            Analysis,

            /// <summary>On the break that numbered it, which the core then repeats without it.</summary>
            Layout,
        }

        private PooledList<RevealRange> ranges;
        private PooledBuffer<bool> graphemeBreaks;
        private PooledBuffer<int> unionOrdinals;
        private PooledBuffer<(int start, int end)> axisMerged;
        private PooledBuffer<bool> axisBreaks;
        private int axisTotal;
        private bool axisComputed;
        private Action analyzedCallback;
        private Action linesBrokenCallback;

        private RevealGlyphHandler glyphRevealing;
        private PooledBuffer<int> ordinals;
        private PooledBuffer<byte> rangeOf;
        private PooledBuffer<float> fronts;
        private PooledBuffer<int> totals;
        private PooledBuffer<(int start, int end, int total, float front, bool receding)> rangeHistory;
        private PooledBuffer<(int visible, int previousVisible, bool historyMatches)> rangeDiffs;
        private PooledBuffer<double> animationStarts;
        private PooledBuffer<float> animationDurations;
        private PooledBuffer<byte> animationFlags;
        private PooledList<int> activeAnimationCodepoints;
        private int comparisonRangeCount;
        private double animationNow;
        private double capturedNow;
        private double manualTime;
        private volatile bool hasActiveAnimations;
        private bool bookkeepingPrepared;
        private Action onGlyphCallback;
        private Action animationUpdateCallback;
        private TickHandle animationUpdateHandle;
        private int shownStart = int.MaxValue;
        private int shownEnd;
        private string reserveWarnedFor;
        private bool clipping;
        private int cachedUnitOrdinal = -1;
        private int cachedUnitRange = -1;
        private TextRange cachedUnitSpan;
        private RevealHandler unitWarnedFor;
        private TextUnit unitWarnedAt;

        /// <summary>
        /// Source of named reveal handlers for <c>&lt;reveal=name&gt;</c> ranges; the entry with an
        /// empty name serves ranges that select no name.
        /// </summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyProviderChange)),
         StateLink(nameof(OnProviderStateChanged))]
        [Tooltip("Source of named reveal handlers for <reveal=name> tags handled by this modifier.")]
        private IRevealHandlerProvider provider = new InlineRevealHandlerProvider();

        /// <summary>
        /// Default <see cref="RevealHandlerEntry"/> name used by ranges whose tag carries no
        /// parameter; empty selects the provider's unnamed entry. A per-range value overrides it.
        /// </summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))]
        private string handlerName;

        private static readonly Func<RevealHandlerEntry, string> entryNameOf =
            static entry => entry?.Name;

        [NonSerialized] private readonly CatalogSnapshot<RevealHandlerEntry> catalogSnapshot = new();
        [NonSerialized] private readonly HashSet<string> referencedNames =
            new(StringComparer.OrdinalIgnoreCase);
        [NonSerialized] private NamedCatalogChangedHandler<RevealHandlerEntry> catalogChangedCallback;
        [NonSerialized] private bool hasProviderHandlers;

        private bool HasSerializedHandlers => hasProviderHandlers;

        protected internal override void PrepareForParallel()
        {
            CaptureClock();
            RefreshRangeFronts();
            if (!IsInitialized) catalogSnapshot.MarkDirty();
            if (!catalogSnapshot.Prepare(provider, entryNameOf)) return;
            hasProviderHandlers = false;
            foreach (var entry in catalogSnapshot.Values)
            {
                if (entry == null || (entry.Handler == null && entry.HideHandler == null)) continue;
                hasProviderHandlers = true;
                break;
            }
            RefreshHandlerSubscriptions();
        }

        private void ResolveFrontier(ref RevealRange range, in RangeApplyContext context)
        {
            range.front = Param.Front.Resolve(this, in context);
            range.unit = Param.Unit.Resolve(this, in context);
        }

        /// <summary>Re-resolves each range's frontier inputs from its retained context, so front and unit changes reach the flag passes without a re-apply.</summary>
        private void RefreshRangeFronts()
        {
            if (!IsInitialized || ranges == null) return;
            for (var i = 0; i < ranges.Count; i++)
            {
                ref var range = ref ranges[i];
                var context = range.memo.ToContext();
                ResolveFrontier(ref range, in context);
            }
        }

        private void ApplyProviderChange(IRevealHandlerProvider previous,
            IRevealHandlerProvider current)
        {
            catalogSnapshot.MarkDirty();
            if (IsInitialized)
            {
                catalogChangedCallback ??= OnCatalogChanged;
                if (previous != null) previous.Changed -= catalogChangedCallback;
                if (current != null) current.Changed += catalogChangedCallback;
            }
            ResetAnimationState();
            MarkTextDirty();
        }

        private void OnProviderStateChanged(IStateChangeSource source, in StateChange change)
            => MarkNestedStateChanged(UniTextDirty.Mesh, source, in change);

        private void OnCatalogChanged(INamedCatalog<RevealHandlerEntry> catalog,
            in NamedCatalogChange<RevealHandlerEntry> change)
        {
            if (change.IsStructural || change.AffectsResolution)
            {
                catalogSnapshot.MarkDirty();
                if (!AffectsReferencedName(in change)) return;
                MarkTextDirty();
                return;
            }
            MarkMeshDirty();
        }

        private bool AffectsReferencedName(in NamedCatalogChange<RevealHandlerEntry> change)
        {
            if (change.Kind is StateChangeKind.Clear or StateChangeKind.ReplaceAll or
                StateChangeKind.Reset) return true;
            return referencedNames.Contains(change.Name ?? string.Empty) ||
                   referencedNames.Contains(change.PreviousName ?? string.Empty);
        }

        private void RefreshHandlerSubscriptions()
        {
            if (!IsInitialized) return;
            if (onGlyphCallback != null) uniText.MeshGenerator.onGlyph.Unsubscribe(onGlyphCallback);
            if (HasGlyphHandler) SubscribeOnGlyph();
            UnsubscribeAnimationUpdates();
            if (HasSerializedHandlers) SubscribeAnimationUpdates();
        }

        /// <summary>
        /// Reveal frontier: a percentage of the frontier axis, or an absolute position in
        /// <see cref="Unit"/>s (fractional values blend the frontier unit; positions beyond the length
        /// show everything). In markup a bare number is the percentage — an absolute position carries
        /// the <c>abs</c> suffix, so four lines is <c>4abs</c> and plain <c>4</c> is four percent of
        /// them. Addresses one shared text-order frontier, or each range independently when
        /// <see cref="PerRange"/> is on.
        /// </summary>
        [SerializeField, Parameter, Unit("%|abs"), StateProperty(nameof(Dirty))]
        private UnitValue front = UnitValue.Percent(100f);

        /// <summary>
        /// What one step of <see cref="Front"/> covers. Changing it rewrites an absolute
        /// <see cref="Front"/> to the number of new units the same text takes, so the revealed text
        /// stays put — two clusters become the one line holding them, and a front of two hundred
        /// clusters spanning five lines becomes five. A percentage needs no rewrite.
        /// A per-range value counts only where each range fills on its own: with
        /// <see cref="PerRange"/> off there is a single frontier axis, and one axis carries one
        /// numbering — the modifier's own.
        /// </summary>
        [SerializeField, Parameter, StateProperty(nameof(Dirty))]
        private TextUnit unit = TextUnit.Cluster;

        /// <summary>
        /// Unit the last pass numbered with; a mismatch converts <see cref="Front"/> before the next one.
        /// Seeded on enable, so a unit authored in the inspector is taken as given rather than converted
        /// from a unit the modifier never ran with.
        /// </summary>
        [NonSerialized] private TextUnit numberedUnit = TextUnit.Cluster;

        /// <summary>
        /// When <see langword="true"/>, hidden clusters are taken out of the layout and the text
        /// reflows (CSS <c>display: none</c>); when <see langword="false"/>, they keep their space and
        /// are only not drawn (CSS <c>visibility: hidden</c>). Ranges may differ: one that collapses
        /// is decided a stage earlier than one that only stops drawing, so its frontier moves through
        /// a re-parse where the other moves on a mesh rebuild.
        /// </summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))] private bool collapse;

        /// <summary>
        /// Label of the range the last shown line keeps room for, so what follows the frontier lands
        /// beside it instead of below: the line gives its tail back — at a break opportunity, whole
        /// words — until the labelled range fits. Its room is measured, never declared, so the
        /// language, the font, a size or spacing effect on it change nothing here.
        /// </summary>
        /// <remarks>
        /// Counts only on a range that counts <see cref="TextUnit.Line"/>s and collapses — the one
        /// arrangement holding a line budget to reserve from — and each such range reserves for its
        /// own label. A labelled range wider than a whole line cannot be made room for; the line gives
        /// back all it can and what follows takes its own line.
        /// </remarks>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))]
        private string reserveFor;

        /// <summary>
        /// Time source appear/hide phases advance on. Manual advances only through
        /// <see cref="AdvanceTime"/>; a clock change mid-flight restarts running phases.
        /// </summary>
        [SerializeField, Tooltip("Time source for appear/hide phases; Manual advances only through code.")]
        [StateProperty(nameof(Dirty))]
        private PlaybackClock clock = PlaybackClock.Unscaled;

        /// <summary>
        /// When <see langword="true"/>, every range fills independently and simultaneously. When
        /// <see langword="false"/> (the default), covered ranges reveal one after another in text
        /// order: <see cref="Front"/> addresses one shared frontier over the union of covered
        /// clusters (each counted once, nesting shares positions).
        /// </summary>
        [SerializeField, StateProperty(nameof(Dirty))] private bool perRange;

        private bool HasGlyphHandler => HasSerializedHandlers || glyphRevealing != null;

        /// <summary>
        /// Fires for every rendered glyph of a covered range during mesh build, carrying the glyph's
        /// reveal state — the receptor for custom appearance animation (fade, scale-in, drop-in, or
        /// anything else): mutate the current quad through <see cref="RevealGlyphInfo.generator"/>.
        /// While subscribed, the frontier cluster renders with a fractional
        /// <see cref="RevealGlyphInfo.Progress"/> instead of popping in whole.
        /// May run on a worker thread — no Unity API calls inside handlers.
        /// </summary>
        public event RevealGlyphHandler GlyphRevealing
        {
            add
            {
                var wasIdle = !HasGlyphHandler;
                glyphRevealing += value;
                if (wasIdle && HasGlyphHandler)
                {
                    if (IsInitialized) SubscribeOnGlyph();
                    Dirty();
                }
            }
            remove
            {
                var wasActive = HasGlyphHandler;
                glyphRevealing -= value;
                if (wasActive && !HasGlyphHandler)
                {
                    if (IsInitialized) uniText.MeshGenerator.onGlyph.Unsubscribe(onGlyphCallback);
                    Dirty();
                }
            }
        }

        private void Dirty()
        {
            if (CollapsesAnywhere) MarkTextDirty();
            else MarkRenderDirty(true);
        }

        private Action beforeGenerateMeshCallback;
        private Action shapedCallback;

        private void SubscribeOnGlyph()
        {
            onGlyphCallback ??= OnGlyph;
            uniText.MeshGenerator.onGlyph.Subscribe(onGlyphCallback);
        }

        /// <summary>
        /// Takes the frame tick of the shared loop: <see cref="CoreLoop.Updating"/> drives playback
        /// in play mode and, while subscribed, in edit mode — a registered listener is what makes
        /// the editor pump a frame per tick at all.
        /// </summary>
        private void SubscribeAnimationUpdates() =>
            CoreLoop.Updating.Toggle(ref animationUpdateHandle,
                animationUpdateCallback ??= RequestAnimationFrame, true);

        private void UnsubscribeAnimationUpdates() =>
            CoreLoop.Updating.Toggle(ref animationUpdateHandle, animationUpdateCallback, false);

        internal void RequestAnimationFrame()
        {
            if (hasActiveAnimations && clock != PlaybackClock.Manual) Dirty();
        }

        protected override void OnEnable()
        {
            numberedUnit = unit;
            ranges ??= new PooledList<RevealRange>(4);
            ranges.FakeClear();
            activeAnimationCodepoints ??= new PooledList<int>(8);
            graphemeBreaks.Rent(64);
            if (animationStarts.data == null) animationStarts.Rent(64);
            if (animationDurations.data == null) animationDurations.Rent(64);
            beforeGenerateMeshCallback ??= OnBeforeGenerateMesh;
            uniText.BeforeGenerateMesh.Subscribe(beforeGenerateMeshCallback);
            analyzedCallback ??= WriteCollapseFlags;
            uniText.TextProcessor.Analyzed.Subscribe(analyzedCallback);
            linesBrokenCallback ??= CollapseFromLayout;
            uniText.TextProcessor.LinesBroken.Subscribe(linesBrokenCallback);
            shapedCallback ??= SuppressCollapsedBreaks;
            uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
            catalogSnapshot.MarkDirty();
            if (provider != null)
            {
                catalogChangedCallback ??= OnCatalogChanged;
                provider.Changed += catalogChangedCallback;
            }
            if (HasGlyphHandler) SubscribeOnGlyph();
            if (HasSerializedHandlers) SubscribeAnimationUpdates();
        }

        protected override void OnDisable()
        {
            uniText.BeforeGenerateMesh.Unsubscribe(beforeGenerateMeshCallback);
            uniText.TextProcessor.Analyzed.Unsubscribe(analyzedCallback);
            uniText.TextProcessor.LinesBroken.Unsubscribe(linesBrokenCallback);
            uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
            if (provider != null && catalogChangedCallback != null)
                provider.Changed -= catalogChangedCallback;
            if (HasGlyphHandler) uniText.MeshGenerator.onGlyph.Unsubscribe(onGlyphCallback);
            if (HasSerializedHandlers) UnsubscribeAnimationUpdates();
        }

        protected override void OnDestroy()
        {
            ResetAnimationState();
            ranges?.Return();
            ranges = null;
            graphemeBreaks.Return();
            unionOrdinals.Return();
            axisMerged.Return();
            axisBreaks.Return();
            ordinals.Return();
            rangeOf.Return();
            fronts.Return();
            totals.Return();
            rangeHistory.Return();
            rangeDiffs.Return();
            animationStarts.Return();
            animationDurations.Return();
            animationFlags.Return();
            activeAnimationCodepoints?.Return();
            activeAnimationCodepoints = null;
        }

        protected override void BeforeApply()
        {
            ranges?.FakeClear();
            referencedNames.Clear();
            bookkeepingPrepared = false;
            axisComputed = false;
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            end = Math.Min(end, buffers.codepoints.count);
            if (start >= end) return;

            var entry = ResolveEntry(in context);
            var range = new RevealRange
            {
                start = start,
                end = end,
                memo = context.Retain(),
                collapse = Param.Collapse.Resolve(this, in context),
                reserveFor = Param.ReserveFor.Resolve(this, in context),
                handler = entry?.Handler,
                hideHandler = entry?.HideHandler,
                shownStart = int.MaxValue,
            };
            ResolveFrontier(ref range, in context);
            WarnUnsupportedUnit(entry?.Handler, range.unit);
            ranges.Add(range);
        }

        /// <summary>
        /// Collapse flags are written here — after every range of the parse is collected (the
        /// sequential axis needs the full set) and the analysis the unit numbering reads is final,
        /// and still before itemization consumes them. Line units are numbered from a layout that
        /// does not exist yet, so they hand the pass to <see cref="CollapseFromLayout"/>.
        /// </summary>
        private void WriteCollapseFlags()
        {
            if (HasGlyphHandler) BeginRangeHistoryPass();
            if (!HasStage(RevealStage.Analysis)) return;

            SyncNumberedUnit();
            WriteFlagsPass(RevealStage.Analysis);
        }

        /// <summary>
        /// Collapses by lines, which only a wrapped text has: the text shapes and wraps whole, its
        /// lines number the frontier, and the hidden ones are then taken out by a re-wrap of the same
        /// break. Nothing is withheld from shaping, so the lines the numbering reads are always the
        /// lines of the full text and the frontier cannot chase its own effect.
        /// </summary>
        /// <remarks>
        /// Runs on the break rather than the finished layout because a text's measured height comes
        /// from the break alone — a collapse decided after positioning never reaches the size the
        /// text reports to whatever lays it out.
        /// </remarks>
        private void CollapseFromLayout()
        {
            if (!uniText.TextProcessor.IsCollectingHiddenLayout) return;
            if (!HasStage(RevealStage.Layout)) return;

            SyncNumberedUnit();
            WriteFlagsPass(RevealStage.Layout);
            ReserveLastLines(uniText.TextProcessor.CurrentLineBreakWidth);
            uniText.TextProcessor.RebreakHidden(HiddenClusterBits.Reflow);
        }

        /// <summary>
        /// Reports a handler asked to play at a granularity it does not declare. The effect plays
        /// anyway — the declaration describes what an effect reads, and only its author can say what
        /// a mismatch should look like. Warned once per handler and granularity.
        /// </summary>
        private void WarnUnsupportedUnit(RevealHandler handler, TextUnit at)
        {
            if (handler == null || handler.SupportedUnits.Has(at)) return;
            if (ReferenceEquals(handler, unitWarnedFor) && at == unitWarnedAt) return;

            unitWarnedFor = handler;
            unitWarnedAt = at;
            UnityEngine.Debug.LogWarning(
                $"[RevealModifier] Reveal handler {handler.GetType().Name} does not declare " +
                $"{at} units; it plays unchanged.");
        }

        /// <summary>
        /// Gives the last shown line's tail back until <see cref="ReserveFor"/>'s range fits beside
        /// it, counting everything still visible between the frontier and that range's end — the
        /// separator before a label lands on the line as surely as the label does. The line ends at a
        /// break opportunity, so the text gives up whole words the way a wrap would have ended the
        /// line at that narrower width.
        /// </summary>
        private void ReserveLastLines(float maxWidth)
        {
            if (maxWidth >= TextProcessSettings.FloatMax) return;

            for (var i = 0; i < ranges.Count; i++)
                if (StageOf(i) == RevealStage.Layout) ReserveLastLine(i, maxWidth);

            RefreshExtent();
        }

        private void ReserveLastLine(int rangeIndex, float maxWidth)
        {
            ref var range = ref ranges[rangeIndex];
            if (!range.clipping || range.shownEnd <= range.shownStart) return;

            var label = range.reserveFor;
            if (string.IsNullOrEmpty(label)) return;
            if (!uniText.TryGetLabeled(label, out var reserved) || reserved.length <= 0)
            {
                WarnMissingReserve(label);
                return;
            }

            var buf = buffers;
            var cpCount = buf.codepoints.count;
            var widths = buf.cpWidths.data;
            if (buf.cpWidths.count < cpCount || reserved.End > cpCount) return;

            var shown = range.shownEnd;
            var lines = buf.lines.Span;
            var lineIndex = LineOfCluster(lines, shown - 1);
            if (lineIndex < 0) return;

            ref readonly var line = ref lines[lineIndex];
            var budget = maxWidth - line.startMargin;
            var bits = BitsFor(rangeIndex);

            var flags = buf.hiddenClusters.data;
            var flagCount = buf.hiddenClusters.count;

            var needed = 0f;
            for (var c = shown; c < reserved.End; c++)
                if (c >= flagCount || (flags[c] & bits) == 0) needed += widths[c];

            var room = budget;
            for (var c = line.range.start; c < shown; c++) room -= widths[c];
            if (room >= needed) return;

            var breaks = buf.breakOpportunities.data;
            var lineStart = line.range.start;
            var cut = shown;
            while (room < needed && cut > lineStart) room += widths[--cut];
            while (cut > lineStart && breaks[cut] == LineBreakType.None) room += widths[--cut];
            if (cut >= shown) return;

            for (var c = cut; c < shown; c++) flags[c] |= bits;
            range.shownEnd = cut;
        }

        /// <summary>
        /// Reports a <see cref="ReserveFor"/> that names no range in the text: the frontier keeps no
        /// room and whatever was to stand beside it wraps to a line of its own. Warned once per name.
        /// </summary>
        private void WarnMissingReserve(string label)
        {
            if (string.Equals(reserveWarnedFor, label, StringComparison.Ordinal)) return;

            reserveWarnedFor = label;
            UnityEngine.Debug.LogWarning(
                $"[RevealModifier] Reserve For '{label}' matches no labeled range; nothing is " +
                $"held back for it. A label goes between a tag's name and its value: " +
                $"<tag #{label}=value>.");
        }

        /// <summary>Index of the line holding <paramref name="cluster"/>, or -1 when no line does.</summary>
        private static int LineOfCluster(ReadOnlySpan<TextLine> lines, int cluster)
        {
            for (var i = 0; i < lines.Length; i++)
                if (cluster >= lines[i].range.start && cluster < lines[i].range.End)
                    return i;
            return -1;
        }

        /// <summary>Whether the frontier is numbered in lines, which are known only after layout.</summary>
        /// <summary>
        /// Stage that writes <paramref name="rangeIndex"/>'s verdict. A range that only stops drawing
        /// text waits for the mesh; one that collapses must be marked before whatever consumes it —
        /// itemization, or the break that numbered it when the frontier counts lines.
        /// </summary>
        private RevealStage StageOf(int rangeIndex)
        {
            if (!ranges[rangeIndex].collapse) return RevealStage.Mesh;
            return UnitOf(rangeIndex) == TextUnit.Line ? RevealStage.Layout : RevealStage.Analysis;
        }

        /// <summary>Bits <paramref name="rangeIndex"/> marks its hidden clusters with.</summary>
        private byte BitsFor(int rangeIndex) => StageOf(rangeIndex) switch
        {
            RevealStage.Analysis => HiddenClusterBits.Reveal | HiddenClusterBits.Collapse,
            RevealStage.Layout => HiddenClusterBits.Reveal | HiddenClusterBits.Reflow,
            _ => HiddenClusterBits.Reveal,
        };

        private bool HasStage(RevealStage stage)
        {
            if (ranges == null) return false;
            for (var i = 0; i < ranges.Count; i++)
                if (StageOf(i) == stage) return true;
            return false;
        }

        /// <summary>Whether any range takes its text out of the layout, at whatever stage.</summary>
        private bool CollapsesAnywhere
        {
            get
            {
                if (collapse) return true;
                if (ranges == null) return false;
                for (var i = 0; i < ranges.Count; i++)
                    if (ranges[i].collapse) return true;
                return false;
            }
        }

        /// <summary>
        /// Writes the verdicts of every range belonging to <paramref name="stage"/> and leaves the
        /// others exactly as the stage that owns them left them. Ownership is computed over all ranges
        /// — the innermost cover decides a cluster whichever stage wrote it — and the modifier's own
        /// extent is recomposed from every range afterwards, so it answers for the whole frontier no
        /// matter which stage ran last.
        /// </summary>
        private void WriteFlagsPass(RevealStage stage)
        {
            ComputeOwnership();
            for (var i = 0; i < ranges.Count; i++)
                if (StageOf(i) == stage)
                    WriteRangeFlags(ranges[i].start, ranges[i].end, BitsFor(i), i);
            TriggerAnimations(stage);
            HoldHidingClusters(stage);
            RefreshExtent();
        }

        /// <summary>Recomposes the modifier-wide extent and clipping from every range's own.</summary>
        private void RefreshExtent()
        {
            shownStart = int.MaxValue;
            shownEnd = 0;
            clipping = false;

            for (var i = 0; i < ranges.Count; i++)
            {
                ref readonly var range = ref ranges[i];
                if (range.clipping) clipping = true;
                if (range.shownEnd <= range.shownStart) continue;
                if (range.shownStart < shownStart) shownStart = range.shownStart;
                if (range.shownEnd > shownEnd) shownEnd = range.shownEnd;
            }
        }

        /// <summary>
        /// Codepoint span shown by the ranges lying inside <paramref name="span"/>, empty while they
        /// show nothing — what <see cref="VisibleRange"/> answers for one part of a modifier whose
        /// ranges differ. Pair it with a labelled span to ask after the range an author named.
        /// </summary>
        public TextRange VisibleRangeIn(TextRange span)
        {
            if (ranges == null || span.length <= 0) return default;

            var start = int.MaxValue;
            var end = 0;
            for (var i = 0; i < ranges.Count; i++)
            {
                ref readonly var range = ref ranges[i];
                if (range.start < span.start || range.end > span.End) continue;
                if (range.shownEnd <= range.shownStart) continue;
                if (range.shownStart < start) start = range.shownStart;
                if (range.shownEnd > end) end = range.shownEnd;
            }

            return end > start ? new TextRange(start, end - start) : default;
        }

        /// <summary>
        /// Codepoint span of the covered text the frontier currently shows, empty while it shows
        /// nothing. Reaches from the first shown cluster to the last, so a frontier over several
        /// ranges reports the extent it covers rather than each range's own share — ask
        /// <see cref="VisibleRangeIn"/> for one range's own. Line breaks, which no frontier hides, do
        /// not carry the span past what it reveals.
        /// </summary>
        public TextRange VisibleRange => shownEnd > shownStart
            ? new TextRange(shownStart, shownEnd - shownStart)
            : default;

        /// <summary>Whether the frontier is holding any covered cluster back.</summary>
        public bool IsClipping => clipping;

        /// <summary>
        /// Takes the hard breaks collapsed text stands behind, for good: the clusters are gone from
        /// the layout for the whole pass, so the merged paragraphs are what the text reports. Runs in
        /// the shaped phase — the channel a modifier influences wrapping through — after break
        /// analysis writes its opportunities and before line breaking reads them.
        /// </summary>
        private void SuppressCollapsedBreaks()
        {
            if (HasStage(RevealStage.Analysis))
                uniText.TextProcessor.SuppressHiddenBreaks(HiddenClusterBits.Collapse);

            ReserveLabelWidth();
        }

        /// <summary>
        /// Asks the text for the room a <see cref="ReserveFor"/> label will stand in, so the width the
        /// text reports covers both the widest line and the label beside it. Measured over the label
        /// as authored, which no allocated width moves: without it the text is handed exactly the
        /// width its content needs, and the reserve can only take the label's place out of that
        /// content — every character added to a clamped line paying for itself again.
        /// </summary>
        /// <remarks>
        /// Sums glyph advances rather than codepoint widths: the shaped phase runs before the pass
        /// projects advances onto codepoints, so the per-codepoint widths still describe the previous
        /// text at this point.
        /// </remarks>
        private void ReserveLabelWidth()
        {
            if (ranges == null || ranges.Count == 0) return;

            for (var i = 0; i < ranges.Count; i++)
            {
                var label = ranges[i].reserveFor;
                if (StageOf(i) != RevealStage.Layout || string.IsNullOrEmpty(label)) continue;
                if (!uniText.TryGetLabeled(label, out var reserved) || reserved.length <= 0) continue;

                uniText.TextProcessor.ReserveLineWidth(AdvanceOver(reserved));
            }
        }

        /// <summary>Shaped advance of every glyph clustering inside <paramref name="span"/>.</summary>
        private float AdvanceOver(TextRange span)
        {
            var glyphs = buffers.shapedGlyphs.data;
            var count = buffers.shapedGlyphs.count;

            var total = 0f;
            for (var g = 0; g < count; g++)
            {
                var cluster = glyphs[g].cluster;
                if (cluster >= span.start && cluster < span.End) total += glyphs[g].advanceX;
            }

            return total;
        }

        /// <summary>
        /// Converts <see cref="Front"/> to the unit now counted in, once this pass can measure both.
        /// A unit whose data the pass does not carry — lines before the text is wrapped — would read
        /// as clusters and convert to a number meaning something else, so the change waits for a pass
        /// that can answer instead.
        /// </summary>
        private void SyncNumberedUnit()
        {
            if (numberedUnit == unit) return;
            if (!CanNumber(numberedUnit) || !CanNumber(unit)) return;

            ConvertFront(numberedUnit);
            numberedUnit = unit;
        }

        /// <summary>Whether this pass carries what <paramref name="over"/> is numbered from.</summary>
        private bool CanNumber(TextUnit over) => over switch
        {
            TextUnit.Line => buffers.lines.count > 0,
            TextUnit.Word => buffers.wordBoundaries.count >= buffers.codepoints.count,
            _ => true,
        };

        /// <summary>The parse's word boundaries over a span, or empty when this pass has none for it.</summary>
        private ReadOnlySpan<bool> WordBoundariesOf(int start, int length)
        {
            var boundaries = buffers.wordBoundaries;
            return boundaries.count >= start + length
                ? boundaries.data.AsSpan(start, length)
                : default;
        }

        /// <summary>
        /// Unit a range numbers in: its own only where it fills independently, since the shared
        /// frontier axis is one numbering for every range on it.
        /// </summary>
        private TextUnit UnitOf(int rangeIndex) => perRange ? ranges[rangeIndex].unit : unit;

        private TextUnitWalk UnitsOver(TextUnit over, int start, int length)
            => new(over, buffers.codepoints.data.AsSpan(start, length),
                WordBoundariesOf(start, length), buffers.lines.Span, start);

        /// <summary>
        /// Rewrites an absolute <see cref="Front"/> into the unit the modifier now counts in, keeping the
        /// revealed text the same: the front lands on the last unit the old one had reached, rounded up to
        /// whole new units. Measured over the first covered range — the serialized front is one value for
        /// every range, so one axis has to speak for it.
        /// </summary>
        private void ConvertFront(TextUnit from)
        {
            if (front.unit == UnitKind.Percent || ranges == null || ranges.Count == 0) return;

            var remaining = Mathf.CeilToInt(front.value - 1e-4f);
            if (remaining <= 0) return;

            var span = new TextRange(ranges[0].start, ranges[0].end - ranges[0].start);

            var reached = 0;
            foreach (var before in uniText.Units(from, span))
            {
                reached = before.End;
                if (--remaining == 0) break;
            }
            if (reached == 0) return;

            var converted = 0;
            foreach (var after in uniText.Units(unit, span))
            {
                converted++;
                if (after.End >= reached) break;
            }

            Front = UnitValue.Absolute(converted);
        }

        /// <summary>
        /// Assigns every codepoint to the innermost (last-applied) covering range — the one that
        /// owns its visibility, ordinal, appearance effect and animation triggers. Uncovered
        /// codepoints keep the <see cref="byte.MaxValue"/> sentinel.
        /// </summary>
        private void ComputeOwnership()
        {
            var cpCount = buffers.codepoints.count;
            rangeOf.EnsureCapacity(cpCount);
            rangeOf.data.AsSpan(0, cpCount).Fill(byte.MaxValue);
            rangeOf.count = cpCount;
            for (var r = 0; r < ranges.Count; r++)
            {
                var start = Math.Max(0, ranges[r].start);
                var end = Math.Min(ranges[r].end, cpCount);
                if (end <= start) continue;
                rangeOf.data.AsSpan(start, end - start).Fill((byte)Math.Min(r, byte.MaxValue));
            }
        }

        private RevealHandlerEntry ResolveEntry(in RangeApplyContext context)
        {
            var name = Param.HandlerName.Resolve(this, in context) ?? string.Empty;

            referencedNames.Add(name);
            if (catalogSnapshot.TryGet(name, out var entry)) return entry;

            if (name.Length != 0)
                UnityEngine.Debug.LogWarning(
                    $"[RevealModifier] Reveal handler \"{name}\" is not defined in the provider.");
            return null;
        }

        /// <summary>
        /// Visual-only flags are rewritten before every mesh build (including <see cref="UniTextDirty.Mesh"/>
        /// rebuilds), which is what makes per-frame <see cref="Front"/> animation cheap. Collapsing writes its
        /// flags in <see cref="WriteCollapseFlags"/> or <see cref="CollapseFromLayout"/> instead — they must
        /// reach a pipeline stage this one is past, and collapse changes always arrive through a full re-parse.
        /// </summary>
        private void OnBeforeGenerateMesh()
        {
            if (HasGlyphHandler) BeginRangeHistoryPass();
            if (!HasStage(RevealStage.Mesh)) return;

            SyncNumberedUnit();
            ClearOwnFlags(RevealStage.Mesh);
            WriteFlagsPass(RevealStage.Mesh);
        }

        /// <summary>Clears the bits written by the ranges of <paramref name="stage"/>, and no others.</summary>
        private void ClearOwnFlags(RevealStage stage)
        {
            var count = buffers.hiddenClusters.count;
            if (count == 0) return;

            var flags = buffers.hiddenClusters.data;
            for (var r = 0; r < ranges.Count; r++)
            {
                if (StageOf(r) != stage) continue;

                var clear = unchecked((byte)~BitsFor(r));
                var max = Math.Min(ranges[r].end, count);
                for (var c = ranges[r].start; c < max; c++)
                    flags[c] &= clear;
            }
        }

        /// <summary>Sizes the per-codepoint ordinal maps once per pass; ordinals default to -1 (no event).</summary>
        private void EnsureBookkeeping()
        {
            if (bookkeepingPrepared) return;
            bookkeepingPrepared = true;

            var cpCount = buffers.codepoints.count;
            ordinals.EnsureCapacity(cpCount);
            ordinals.data.AsSpan(0, cpCount).Fill(-1);
            ordinals.count = cpCount;

            var previousAnimationCount = animationStarts.count;
            animationStarts.EnsureCapacity(cpCount);
            animationDurations.EnsureCapacity(cpCount);
            animationFlags.EnsureCapacity(cpCount);
            if (previousAnimationCount < cpCount)
            {
                animationStarts.data.AsSpan(previousAnimationCount,
                    cpCount - previousAnimationCount).Fill(-1d);
                animationDurations.data.AsSpan(previousAnimationCount,
                    cpCount - previousAnimationCount).Clear();
                animationFlags.data.AsSpan(previousAnimationCount,
                    cpCount - previousAnimationCount).Clear();
            }
            animationStarts.count = cpCount;
            animationDurations.count = cpCount;
            animationFlags.count = cpCount;
        }

        private void WriteRangeFlags(int start, int end, byte bits, int rangeIndex)
        {
            var flags = buffers.PrepareHiddenClusters();
            end = Math.Min(end, flags.Length);
            ranges[rangeIndex].shownStart = int.MaxValue;
            ranges[rangeIndex].shownEnd = 0;
            ranges[rangeIndex].clipping = false;
            if (start >= end)
            {
                rangeDiffs.EnsureCapacity(rangeIndex + 1);
                rangeDiffs.data[rangeIndex] = (0, 0, false);
                rangeDiffs.count = Math.Max(rangeDiffs.count, rangeIndex + 1);
                return;
            }

            var cps = buffers.codepoints.data;
            var len = end - start;
            var myByte = (byte)Math.Min(rangeIndex, byte.MaxValue);

            graphemeBreaks.EnsureCapacity(len + 1);
            var breaks = graphemeBreaks.data.AsSpan(0, len + 1);
            SharedPipelineComponents.GraphemeBreaker.GetBreakOpportunities(cps.AsSpan(start, len), breaks);

            var rangeUnit = UnitOf(rangeIndex);
            var totalUnits = UnitsOver(rangeUnit, start, len);
            var total = 0;
            for (var i = 0; i < len; i++)
            {
                if (!breaks[i]) continue;
                if (UnicodeData.IsMandatoryBreakChar(cps[start + i]))
                {
                    totalUnits.Skip(i);
                    continue;
                }

                if (rangeOf.data[start + i] == myByte && totalUnits.Starts(i)) total++;
            }

            if (!perRange) EnsureSequentialAxis();
            var front = ResolveFront(rangeIndex, total);
            var totalForRange = perRange ? total : axisTotal;

            var hasSerialized = HasSerializedHandlers;
            var eventHandler = glyphRevealing;
            var hasHandler = hasSerialized || eventHandler != null;
            var hasHistory = hasHandler && rangeIndex < comparisonRangeCount;
            var previous = hasHistory ? rangeHistory.data[rangeIndex] : default;
            var historyMatches = hasHistory && previous.start == start && previous.end == end &&
                                 previous.total == totalForRange;
            var previousFront = historyMatches ? previous.front : front;
            int visible;
            if (hasHandler)
            {
                EnsureBookkeeping();
                fronts.EnsureCapacity(rangeIndex + 1);
                fronts.data[rangeIndex] = front;
                fronts.count = Math.Max(fronts.count, rangeIndex + 1);
                totals.EnsureCapacity(rangeIndex + 1);
                totals.data[rangeIndex] = totalForRange;
                totals.count = Math.Max(totals.count, rangeIndex + 1);
                visible = Math.Min(totalForRange, Mathf.CeilToInt(front - 1e-4f));
            }
            else
            {
                visible = (int)(front + 1e-4f);
            }

            if (hasHandler)
            {
                var previousVisible =
                    Math.Min(totalForRange, Mathf.CeilToInt(previousFront - 1e-4f));
                var receding = historyMatches && (front < previousFront ||
                                                  (front == previousFront && previous.receding));
                rangeHistory.EnsureCapacity(rangeIndex + 1);
                rangeHistory.data[rangeIndex] = (start, end, totalForRange, front, receding);
                rangeHistory.count = Math.Max(rangeHistory.count, rangeIndex + 1);
                rangeDiffs.EnsureCapacity(rangeIndex + 1);
                rangeDiffs.data[rangeIndex] = (visible, previousVisible, historyMatches);
                rangeDiffs.count = Math.Max(rangeDiffs.count, rangeIndex + 1);
            }

            var units = UnitsOver(rangeUnit, start, len);
            var seen = 0;
            var ordinal = -1;
            var clusterVisible = true;
            var clusterOwned = true;
            for (var i = 0; i < len; i++)
            {
                var cpIndex = start + i;
                if (breaks[i])
                {
                    if (UnicodeData.IsMandatoryBreakChar(cps[cpIndex]))
                    {
                        units.Skip(i);
                        clusterOwned = true;
                        clusterVisible = true;
                        ordinal = -1;
                    }
                    else if (rangeOf.data[cpIndex] != myByte)
                    {
                        clusterOwned = false;
                        ordinal = -1;
                    }
                    else
                    {
                        clusterOwned = true;
                        if (units.Starts(i)) seen++;
                        ordinal = perRange ? seen - 1 : unionOrdinals.data[cpIndex];
                        clusterVisible = ordinal < visible;
                    }
                }

                if (!clusterOwned) continue;

                if (!clusterVisible)
                {
                    flags[cpIndex] |= bits;
                    ranges[rangeIndex].clipping = true;
                }
                else
                {
                    flags[cpIndex] &= unchecked((byte)~bits);
                    if (ordinal >= 0)
                    {
                        if (cpIndex < ranges[rangeIndex].shownStart)
                            ranges[rangeIndex].shownStart = cpIndex;
                        if (cpIndex >= ranges[rangeIndex].shownEnd)
                            ranges[rangeIndex].shownEnd = cpIndex + 1;
                    }
                }

                if (hasHandler && ordinal >= 0)
                    ordinals.data[cpIndex] = ordinal;
            }
        }

        /// <summary>
        /// Fires appear and hide animation triggers for the ranges the pass just wrote — each cluster
        /// is diffed once, against the frontier of the range that owns it (the innermost), so an outer
        /// range crossing an already-revealed cluster replays nothing, and a range another stage owns
        /// replays nothing either. A range whose extent or cluster count changed drops its in-flight
        /// animations instead: their timeline no longer describes whatever now sits at those
        /// codepoints.
        /// </summary>
        private void TriggerAnimations(RevealStage stage)
        {
            if (!HasSerializedHandlers || ranges == null) return;
            for (var r = 0; r < ranges.Count; r++)
            {
                if (r >= rangeDiffs.count || StageOf(r) != stage) continue;
                var (visible, previousVisible, historyMatches) = rangeDiffs.data[r];
                if (!historyMatches)
                {
                    DropAnimations(r);
                    continue;
                }
                if (visible == previousVisible) continue;
                var owner = (byte)Math.Min(r, byte.MaxValue);
                var start = Math.Max(0, ranges[r].start);
                var end = Math.Min(ranges[r].end, ordinals.count);
                var appearDuration = ranges[r].handler?.Duration ?? 0f;
                var hideDuration = (ranges[r].hideHandler ?? ranges[r].handler)?.Duration ?? 0f;
                for (var cp = start; cp < end; cp++)
                {
                    if (rangeOf.data[cp] != owner) continue;
                    var ordinal = ordinals.data[cp];
                    if (ordinal < 0) continue;
                    var isVisible = ordinal < visible;
                    var wasVisible = ordinal < previousVisible;
                    if (!wasVisible && isVisible) StartAnimation(cp, appearDuration, false);
                    else if (wasVisible && !isVisible) StartAnimation(cp, hideDuration, true);
                }
            }
        }

        /// <summary>
        /// Keeps every cluster with a running hide effect in the mesh — and, where its own range
        /// collapses, in layout — until that effect finishes. The flag pass has already marked the
        /// cluster invisible by the time its hide animation starts, so this runs after
        /// <see cref="TriggerAnimations"/> and clears only the bits its owning range wrote.
        /// </summary>
        private void HoldHidingClusters(RevealStage stage)
        {
            if (!hasActiveAnimations || activeAnimationCodepoints == null) return;
            var flags = buffers.hiddenClusters.data;
            var count = Math.Min(buffers.hiddenClusters.count, rangeOf.count);
            for (var i = 0; i < activeAnimationCodepoints.Count; i++)
            {
                var codepoint = activeAnimationCodepoints[i];
                if ((uint)codepoint >= (uint)count || !IsHiding(codepoint)) continue;

                int owner = rangeOf.data[codepoint];
                if (owner >= ranges.Count || StageOf(owner) != stage) continue;
                flags[codepoint] &= unchecked((byte)~BitsFor(owner));
            }
        }

        private void OnGlyph()
        {
            var eventHandler = glyphRevealing;
            var hasSerialized = HasSerializedHandlers;
            if (!hasSerialized && eventHandler == null) return;

            var gen = uniText.MeshGenerator;
            var cluster = gen.currentCluster;
            if ((uint)cluster >= (uint)ordinals.count) return;

            var ordinal = ordinals.data[cluster];
            if (ordinal < 0) return;

            int rangeIndex = rangeOf.data[cluster];
            var hiding = IsHiding(cluster);
            var span = UnitSpanOf(cluster, ordinal, rangeIndex);
            var rangeHandler = hasSerialized && rangeIndex < ranges.Count
                ? hiding
                    ? ranges[rangeIndex].hideHandler ?? ranges[rangeIndex].handler
                    : ranges[rangeIndex].handler
                : null;
            if (rangeHandler != null)
            {
                var progress = PhaseProgress(cluster, hiding);
                if (progress < 1f)
                {
                    var handlerInfo = new RevealGlyphInfo(uniText, gen, cluster, span, ordinal,
                        totals.data[rangeIndex], fronts.data[rangeIndex], hiding)
                        .WithProgress(progress);
                    rangeHandler.Apply(in handlerInfo);
                }
            }
            if (eventHandler == null) return;
            var receding = rangeIndex < rangeHistory.count && rangeHistory.data[rangeIndex].receding;
            var eventInfo = new RevealGlyphInfo(uniText, gen, cluster, span, ordinal,
                totals.data[rangeIndex], fronts.data[rangeIndex], receding);
            eventHandler.Invoke(in eventInfo);
        }

        /// <summary>
        /// Codepoint span of the unit holding <paramref name="cluster"/> — the run its range gave one
        /// ordinal. Cached for the unit being emitted, which every glyph of it asks for in turn.
        /// </summary>
        private TextRange UnitSpanOf(int cluster, int ordinal, int rangeIndex)
        {
            if (ordinal == cachedUnitOrdinal && rangeIndex == cachedUnitRange) return cachedUnitSpan;

            var owner = (byte)Math.Min(rangeIndex, byte.MaxValue);
            var limit = ordinals.count;
            var start = cluster;
            while (start > 0 && ordinals.data[start - 1] == ordinal &&
                   rangeOf.data[start - 1] == owner) start--;
            var end = cluster + 1;
            while (end < limit && ordinals.data[end] == ordinal &&
                   rangeOf.data[end] == owner) end++;

            cachedUnitOrdinal = ordinal;
            cachedUnitRange = rangeIndex;
            cachedUnitSpan = new TextRange(start, end - start);
            return cachedUnitSpan;
        }

        private float ResolveFront(int rangeIndex, int total)
        {
            var front = ranges[rangeIndex].front;
            var limit = perRange ? total : axisTotal;
            if (front.unit == UnitKind.Percent)
                return Mathf.Clamp01(front.value / 100f) * limit;
            return Mathf.Clamp(front.value, 0f, limit);
        }

        /// <summary>
        /// Builds the shared frontier axis over the UNION of this parse's ranges: every covered
        /// cluster gets one text-order position (<see cref="unionOrdinals"/>), regardless of how
        /// many ranges cover it. Numbered in the modifier's own <see cref="Unit"/> — one axis
        /// admits one numbering, so a range's own unit does not reach it. Cached until the next
        /// apply cycle, so per-frame <see cref="Front"/> animation pays no recount.
        /// </summary>
        private void EnsureSequentialAxis()
        {
            if (axisComputed) return;
            axisComputed = true;

            var cpCount = buffers.codepoints.count;
            unionOrdinals.EnsureCapacity(cpCount);
            unionOrdinals.data.AsSpan(0, cpCount).Fill(-1);
            unionOrdinals.count = cpCount;
            axisMerged.EnsureCapacity(ranges.Count);

            var merged = 0;
            for (var i = 0; i < ranges.Count; i++)
            {
                var start = Math.Max(0, ranges[i].start);
                var end = Math.Min(ranges[i].end, cpCount);
                if (end <= start) continue;
                axisMerged.data[merged++] = (start, end);
            }
            for (var i = 1; i < merged; i++)
            {
                var value = axisMerged.data[i];
                var j = i - 1;
                while (j >= 0 && axisMerged.data[j].start > value.start)
                {
                    axisMerged.data[j + 1] = axisMerged.data[j];
                    j--;
                }
                axisMerged.data[j + 1] = value;
            }
            var count = 0;
            for (var i = 0; i < merged; i++)
            {
                var value = axisMerged.data[i];
                if (count > 0 && value.start <= axisMerged.data[count - 1].end)
                {
                    if (value.end > axisMerged.data[count - 1].end)
                        axisMerged.data[count - 1].end = value.end;
                    continue;
                }
                axisMerged.data[count++] = value;
            }
            axisMerged.count = count;

            axisTotal = 0;
            var cps = buffers.codepoints.data;
            for (var m = 0; m < count; m++)
            {
                var (start, end) = axisMerged.data[m];
                var len = end - start;
                axisBreaks.EnsureCapacity(len + 1);
                var breaks = axisBreaks.data.AsSpan(0, len + 1);
                var walk = RevealableClusterWalk.Over(cps.AsSpan(start, len), breaks,
                    excludeSpaces: false, UnitsOver(unit, start, len));
                while (walk.MoveNext())
                    if (walk.IsLead && walk.Eligible)
                        unionOrdinals.data[start + walk.Index] = axisTotal + walk.Ordinal;
                axisTotal += walk.UnitCount;
            }
        }

#if UNITY_EDITOR
        internal bool HasSerializedHandler => HasSerializedHandlers;
#endif

        /// <summary>
        /// Opens a history pass: latches the outgoing per-range records for comparison and takes the
        /// frame's clock reading. Records are overwritten in place by whichever stage owns each range,
        /// so a range this frame's stages do not reach keeps the record it was last given rather than
        /// losing it; a record whose extent or cluster count no longer matches is refused by the diff.
        /// </summary>
        private void BeginRangeHistoryPass()
        {
            comparisonRangeCount = rangeHistory.count;
            animationNow = capturedNow;
            cachedUnitOrdinal = -1;
            CompleteAnimations();
        }

        /// <summary>
        /// Samples the configured clock on the main thread; apply and mesh passes that consume
        /// <see cref="animationNow"/> may run on worker threads where the engine clock is illegal.
        /// </summary>
        private void CaptureClock()
        {
            var now = clock switch
            {
                PlaybackClock.Scaled => Time.timeAsDouble,
                PlaybackClock.Unscaled => Time.unscaledTimeAsDouble,
                _ => manualTime,
            };
#if UNITY_EDITOR
            if (!Application.isPlaying && clock != PlaybackClock.Manual)
                now = UnityEditor.EditorApplication.timeSinceStartup;
#endif
            capturedNow = now;
        }

        /// <summary>
        /// Advances appear/hide phases by explicit seconds — the tick source for the
        /// <see cref="PlaybackClock.Manual"/> clock; other clocks ignore it.
        /// </summary>
        public void AdvanceTime(float deltaTime)
        {
            manualTime += deltaTime;
            if (clock == PlaybackClock.Manual && hasActiveAnimations) Dirty();
        }

        /// <summary>
        /// Starts one phase for a cluster, offset so the phase continues the glyph's current
        /// appearance instead of restarting it: an interrupted appear hands its exact state to the
        /// hide that replaces it, and back. A phase with no duration settles immediately.
        /// </summary>
        private void StartAnimation(int codepoint, float duration, bool hiding)
        {
            if ((uint)codepoint >= (uint)animationStarts.count) return;
            if (duration <= 0f)
            {
                animationStarts.data[codepoint] = -1d;
                return;
            }
            animationStarts.data[codepoint] =
                animationNow - ResumeOffset(codepoint, hiding, duration);
            animationDurations.data[codepoint] = duration;
            var flags = animationFlags.data[codepoint];
            if ((flags & AnimationListed) == 0)
            {
                activeAnimationCodepoints.Add(codepoint);
                flags |= AnimationListed;
            }
            animationFlags.data[codepoint] = hiding
                ? (byte)(flags | AnimationHiding)
                : (byte)(flags & ~AnimationHiding);
            hasActiveAnimations = true;
        }

        private float ResumeOffset(int codepoint, bool hiding, float duration)
        {
            if (animationStarts.data[codepoint] < 0d) return 0f;
            var settled = PhaseProgress(codepoint, IsHiding(codepoint));
            return (hiding ? 1f - settled : settled) * duration;
        }

        private void DropAnimations(int rangeIndex)
        {
            if (!hasActiveAnimations) return;
            var owner = (byte)Math.Min(rangeIndex, byte.MaxValue);
            var start = Math.Max(0, ranges[rangeIndex].start);
            var end = Math.Min(ranges[rangeIndex].end, animationStarts.count);
            for (var codepoint = start; codepoint < end; codepoint++)
                if (rangeOf.data[codepoint] == owner) animationStarts.data[codepoint] = -1d;
        }

        private bool IsHiding(int codepoint)
            => (uint)codepoint < (uint)animationStarts.count &&
               animationStarts.data[codepoint] >= 0d &&
               (animationFlags.data[codepoint] & AnimationHiding) != 0;

        /// <summary>Animation state of a cluster in appearance terms, 1 being the settled glyph.</summary>
        private float PhaseProgress(int codepoint, bool hiding)
        {
            var progress = AnimationProgress(codepoint);
            return hiding ? 1f - progress : progress;
        }

        private float AnimationProgress(int codepoint)
        {
            if ((uint)codepoint >= (uint)animationStarts.count) return 1f;
            var startedAt = animationStarts.data[codepoint];
            if (startedAt < 0d) return 1f;
            var duration = animationDurations.data[codepoint];
            if (duration <= 0f) return 1f;
            return Mathf.Clamp01((float)((animationNow - startedAt) / duration));
        }

        private void CompleteAnimations()
        {
            if (activeAnimationCodepoints == null) return;
            for (var i = activeAnimationCodepoints.Count - 1; i >= 0; i--)
            {
                var codepoint = activeAnimationCodepoints[i];
                if ((uint)codepoint < (uint)animationStarts.count &&
                    animationStarts.data[codepoint] >= 0d &&
                    animationNow - animationStarts.data[codepoint] <
                    animationDurations.data[codepoint]) continue;
                if ((uint)codepoint < (uint)animationStarts.count)
                {
                    animationStarts.data[codepoint] = -1d;
                    animationFlags.data[codepoint] = 0;
                }
                activeAnimationCodepoints.buffer.SwapRemoveAt(i);
            }
            hasActiveAnimations = activeAnimationCodepoints.Count > 0;
        }

        private void ResetAnimationState()
        {
            rangeHistory.count = 0;
            comparisonRangeCount = 0;
            if (animationStarts.data != null && animationStarts.count > 0)
                animationStarts.data.AsSpan(0, animationStarts.count).Fill(-1d);
            if (animationFlags.data != null && animationFlags.count > 0)
                animationFlags.data.AsSpan(0, animationFlags.count).Clear();
            activeAnimationCodepoints?.FakeClear();
            hasActiveAnimations = false;
        }
    }
}
