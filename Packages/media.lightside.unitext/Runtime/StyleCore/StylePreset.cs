using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Describes one exact structural or nested state transition in a style preset.</summary>
    public readonly struct StylePresetChange
    {
        internal StylePresetChange(StateChangeKind kind, Style style = null,
            BaseModifier modifier = null, RangeSource rangeSource = null,
            UniTextDirty flags = UniTextDirty.Text, IStateMemberReplay stateSource = null,
            StateMember member = default, bool structural = false,
            int index = -1, int destinationIndex = -1)
        {
            Kind = kind;
            Style = style;
            Modifier = modifier;
            RangeSource = rangeSource;
            Flags = flags;
            StateSource = stateSource;
            Member = member;
            Structural = structural;
            Index = index;
            DestinationIndex = destinationIndex;
        }

        /// <summary>Gets the scalar or collection operation that committed.</summary>
        public StateChangeKind Kind { get; }
        /// <summary>Gets the affected authored style when one is identifiable.</summary>
        public Style Style { get; }
        /// <summary>Gets the affected modifier node when the transition originated in a modifier graph.</summary>
        public BaseModifier Modifier { get; }
        /// <summary>Gets the affected range source when the transition originated in a source graph.</summary>
        public RangeSource RangeSource { get; }
        /// <summary>Gets the modifier-owned consequence for a runtime modifier parameter.</summary>
        public UniTextDirty Flags { get; }
        /// <summary>Gets the replayable node that owns <see cref="Member"/>, when scalar replay is available.</summary>
        public IStateMemberReplay StateSource { get; }
        /// <summary>Gets the exact generated member token for a nested scalar transition.</summary>
        public StateMember Member { get; }
        /// <summary>Gets whether topology, registration, or source resolution changed.</summary>
        public bool Structural { get; }
        /// <summary>Gets the affected source index, or -1 when no collection index applies.</summary>
        public int Index { get; }
        /// <summary>Gets the destination index of a move, or -1 for other operations.</summary>
        public int DestinationIndex { get; }
    }

    /// <summary>Receives one exact committed style-preset transition.</summary>
    public delegate void StylePresetChangedHandler(StylePreset preset,
        in StylePresetChange change);

    /// <summary>
    /// Shareable, project-asset container of <see cref="Style"/> entries that components apply
    /// in bulk via <see cref="UniTextBase.StylePresets"/> or via
    /// <see cref="UniTextSettings.GlobalStylePreset"/>.
    /// </summary>
    /// <remarks>
    /// Mutating <see cref="Styles"/> raises <see cref="Changed"/>. Attached <see cref="UniTextBase"/>
    /// components replay leaf members into the matching runtime node and replace a runtime graph
    /// only when its structure or resolution changed.
    /// </remarks>
    [CreateAssetMenu(fileName = "StylePreset", menuName = UniTextMenu.CreateAsset.StylePreset)]
    public partial class StylePreset : ScriptableObject, IStyleChangeSink
    {
        /// <summary>Source/modifier pairs owned by this preset.</summary>
        [SerializeField, StateList(nameof(ApplyStylesChange),
            Validator = nameof(ValidateStylesMutation), Owned = true, AllowNullItems = false,
            AllowDuplicateReferences = false)]
        [Tooltip("Source/modifier pairs that define which ranges exist and what is applied to them.")]
        private StyledList<Style> styles = new();

        [NonSerialized] private ReferenceBinding<Style> boundStyles;
        [NonSerialized] private bool dependenciesBound;

        /// <summary>Raised with exact affected-node, member, operation, and replay context.</summary>
        public event StylePresetChangedHandler DeltaChanged;

        private void OnEnable()
        {
            dependenciesBound = true;
            if (styles == null) SetStylesState(new StyledList<Style>());
            else BindStyles();
        }

        private void OnDisable()
        {
            dependenciesBound = false;
            UnbindStyles();
        }

        private void ApplyStylesChange(in StateListMutation<Style> mutation)
        {
            if (dependenciesBound) ReconcileStyles();
            if (!mutation.TryGetCurrentItem(out var style) &&
                mutation.Kind == StateListMutationKind.Remove)
                style = mutation.PreviousItem;
            var change = new StylePresetChange(mutation.ChangeKind, style,
                structural: true,
                index: mutation.AffectedIndex,
                destinationIndex: mutation.DestinationIndex);
            PublishChange(in change);
        }

        private void BindStyles()
        {
            ValidateStyles();
            ReconcileStyles();
        }

        private void ReconcileStyles()
        {
            boundStyles ??= new ReferenceBinding<Style>(ConnectStyle, DisconnectStyle);
            boundStyles.Reconcile(styles);
        }

        private void UnbindStyles() => boundStyles?.Clear();

        private void ConnectStyle(Style style) => style.SetChangeSink(this);

        private void DisconnectStyle(Style style)
        {
            if (ReferenceEquals(style.ChangeSink, this)) style.SetChangeSink(null);
        }

        internal static Style[] CreateRuntimeStyles(StylePreset source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!MainThread.IsCurrent)
                throw new InvalidOperationException(
                    "Style runtime graphs must be created on the main thread.");
            source.ValidateStyles();
            var copy = Instantiate(source);
            try
            {
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.dependenciesBound = false;
                copy.UnbindStyles();
                var result = new Style[copy.Styles.Count];
                for (var i = 0; i < result.Length; i++) result[i] = copy.Styles[i];
                copy.Styles.Clear();
                for (var i = 0; i < result.Length; i++)
                    StateOwnership.Initialize((IStateOwnershipContainer)result[i]);
                return result;
            }
            finally
            {
                ObjectUtils.SafeDestroy(copy);
            }
        }

        void IStyleChangeSink.MarkStyleChanged(in StyleChange transition)
        {
            var change = new StylePresetChange(StateChangeKind.Value, transition.Style,
                transition.Modifier, transition.RangeSource, transition.Flags,
                transition.StateSource, transition.Member, transition.Structural);
            PublishChange(in change);
        }

        private void PublishChange(in StylePresetChange change)
        {
            DeltaChanged?.Invoke(this, in change);
        }

        private void ValidateStylesMutation(in StateListMutation<Style> mutation)
        {
            for (var i = 0; i < mutation.Count; i++)
            {
                BaseModifier.ValidateGraph(mutation[i].Modifier);
                RangeSource.ValidateGraph(mutation[i].Source);
            }
            Style.ValidateCollectionGraphs(in mutation);
        }

        private void ValidateStyles()
        {
            StateListRules.ValidateReferences(styles, false, false, nameof(styles));
            var mutation = StateListMutation<Style>.CreateReplacement(styles);
            ValidateStylesMutation(in mutation);
        }
    }
}
