using System;
using System.Collections.Generic;

namespace LightSide
{
    public abstract partial class UniTextBase
    {
        private sealed class StyleRuntimeSet : IStyleChangeSink
        {
            private sealed class Binding
            {
                internal readonly StylePreset SourcePreset;
                internal Style[] SourceStyles;
                internal Style[] RuntimeStyles;

                internal Binding(StylePreset sourcePreset, Style[] sourceStyles,
                    Style[] runtimeStyles)
                {
                    SourcePreset = sourcePreset;
                    SourceStyles = sourceStyles;
                    RuntimeStyles = runtimeStyles;
                }
            }

            private readonly UniTextBase owner;
            private readonly List<Binding> bindings = new();
            private readonly List<Style> liveStyles = new();
            private readonly List<Style> nextStyles = new();
            private readonly List<StylePreset> desiredPresets = new();
            private ReferenceBinding<Style> localSources;
            private ReferenceBinding<StylePreset> subscriptions;
            private bool created;
            private bool parserAttached;
            private bool reconciling;
            private bool suppressStructuralChanges;

            internal StyleRuntimeSet(UniTextBase owner) => this.owner = owner;

            internal IReadOnlyList<Style> Styles
            {
                get
                {
                    EnsureCreated();
                    return liveStyles;
                }
            }

            internal void EnsureCreated()
            {
                if (created) return;
                BindLocalStyles();
                StateListRules.ValidateReferences(owner.stylePresets, false, false,
                    nameof(UniTextBase.stylePresets));
                CollectDesiredPresets();
                try
                {
                    for (var i = 0; i < desiredPresets.Count; i++)
                        bindings.Add(CreateBinding(desiredPresets[i]));
                    ReconcileLiveStyles();
                    created = true;
                }
                catch
                {
                    ReleaseBindingsFrom(0);
                    liveStyles.Clear();
                    throw;
                }
            }

            internal void AttachParser()
            {
                EnsureCreated();
                if (parserAttached) return;
                if (owner.attributeParser == null)
                    throw new InvalidOperationException(
                        "Style runtime cannot attach without an attribute parser.");
                ReconcileParser(liveStyles);
                parserAttached = true;
            }

            internal void ReconcileLocal()
            {
                BindLocalStylesCore();
                if (!created)
                {
                    owner.SetDirty(UniTextDirty.Text);
                    return;
                }
                if (ReconcileLiveStyles()) PublishStructuralChange();
            }

            internal void ReconcilePresets(bool setDirty = true)
            {
                CollectDesiredPresets();
                if (owner.subscribed) RefreshSubscriptions();
                if (!created)
                {
                    if (setDirty) owner.SetDirty(UniTextDirty.Text);
                    return;
                }
                if (ReconcileBindings()) PublishStructuralChange(setDirty);
            }

            internal void DestroyRuntime()
            {
                if (!created) return;
                try
                {
                    if (parserAttached) ReconcileParser(Array.Empty<Style>());
                }
                finally
                {
                    parserAttached = false;
                    ReleaseBindingsFrom(0);
                    liveStyles.Clear();
                    nextStyles.Clear();
                    created = false;
                }
            }

            internal void Dispose()
            {
                Unlisten();
                DestroyRuntime();
                localSources?.Clear();
            }

            internal void Listen()
            {
                CollectDesiredPresets();
                RefreshSubscriptions();
                if (!created) return;
                ReleaseBindingsFrom(0);
                for (var i = 0; i < desiredPresets.Count; i++)
                    bindings.Add(CreateBinding(desiredPresets[i]));
                ReconcileLiveStyles();
            }

            internal void Unlisten()
            {
                subscriptions?.Clear();
                desiredPresets.Clear();
            }

            void IStyleChangeSink.MarkStyleChanged(in StyleChange change)
            {
                if (reconciling)
                    throw new InvalidOperationException(
                        "A style graph cannot mutate while its registrations are being reconciled.");
                if (!created)
                {
                    owner.SetDirty(change.Flags);
                    return;
                }
                ApplyRuntimeChange(in change);
            }

            private void BindLocalStyles()
            {
                owner.ValidateLocalStyles();
                BindLocalStylesCore();
            }

            private void BindLocalStylesCore()
            {
                localSources ??= new ReferenceBinding<Style>(
                    ConnectLocalStyle, DisconnectLocalStyle);
                localSources.Reconcile(owner.styles);
            }

            private void ConnectLocalStyle(Style style) => style.SetChangeSink(this);

            private void DisconnectLocalStyle(Style style)
            {
                if (ReferenceEquals(style.ChangeSink, this)) style.SetChangeSink(null);
            }

            private void CollectDesiredPresets()
            {
                desiredPresets.Clear();
                for (var i = 0; i < owner.stylePresets.Count; i++)
                    desiredPresets.Add(owner.stylePresets[i]);
                if (owner.useGlobalStylePreset &&
                    UniTextSettings.GlobalStylePreset is { } global)
                    desiredPresets.Add(global);
            }

            private bool ReconcileBindings()
            {
                var changed = false;
                for (var targetIndex = 0; targetIndex < desiredPresets.Count; targetIndex++)
                {
                    var source = desiredPresets[targetIndex];
                    if (targetIndex < bindings.Count &&
                        ReferenceEquals(bindings[targetIndex].SourcePreset, source)) continue;

                    var existingIndex = FindBinding(source, targetIndex + 1);
                    if (existingIndex >= 0)
                    {
                        var binding = bindings[existingIndex];
                        bindings.RemoveAt(existingIndex);
                        bindings.Insert(targetIndex, binding);
                    }
                    else
                        bindings.Insert(targetIndex, CreateBinding(source));
                    changed = true;
                }

                if (bindings.Count > desiredPresets.Count)
                {
                    ReleaseBindingsFrom(desiredPresets.Count);
                    changed = true;
                }
                return changed && ReconcileLiveStyles();
            }

            private int FindBinding(StylePreset source, int start)
            {
                for (var i = start; i < bindings.Count; i++)
                    if (ReferenceEquals(bindings[i].SourcePreset, source)) return i;
                return -1;
            }

            private void OnPresetChanged(StylePreset preset, in StylePresetChange change)
            {
                if (reconciling || suppressStructuralChanges)
                    throw new InvalidOperationException(
                        "A style preset cannot change reentrantly while its runtime graph is updating.");
                if (!created)
                {
                    owner.SetDirty(change.Flags);
                    return;
                }

                var matched = false;
                var replaced = false;
                suppressStructuralChanges = change.Structural;
                try
                {
                    for (var i = 0; i < bindings.Count; i++)
                    {
                        var binding = bindings[i];
                        if (!ReferenceEquals(binding.SourcePreset, preset)) continue;
                        matched = true;
                        if (TryReplay(binding, in change)) continue;
                        ReplaceBindingStyles(binding, preset, in change);
                        replaced = true;
                    }
                }
                finally
                {
                    suppressStructuralChanges = false;
                }

                if (!matched)
                    throw new InvalidOperationException(
                        "A subscribed style preset has no runtime binding.");
                if (!change.Structural && !replaced) return;
                var changed = ReconcileLiveStyles();
                if (!parserAttached || changed) PublishStructuralChange();
            }

            private static bool TryReplay(Binding binding, in StylePresetChange change)
            {
                if (change.RangeSource != null || change.StateSource == null ||
                    !change.Member.IsValid) return false;
                if (change.Structural &&
                    (!ReferenceEquals(change.StateSource, change.Style) ||
                     change.Member != Style.Members.Disabled)) return false;
                var sourceIndex = ReferenceIdentity.IndexOf(binding.SourceStyles, change.Style);
                if ((uint)sourceIndex >= (uint)binding.RuntimeStyles.Length) return false;
                return Replay(binding.RuntimeStyles[sourceIndex], change.Modifier,
                    change.StateSource, change.Member);
            }

            private void ReplaceBindingStyles(Binding binding, StylePreset preset,
                in StylePresetChange change)
            {
                if (binding.SourceStyles.Length != binding.RuntimeStyles.Length)
                    throw new InvalidOperationException(
                        "A style preset binding lost its authored-to-runtime correspondence.");

                var previous = binding.RuntimeStyles;
                var sources = CopyStyles(preset.Styles);
                var current = new Style[sources.Length];
                Style[] replacements = null;
                var replaceAll = change.Style == null &&
                                 change.Kind is StateChangeKind.Value or StateChangeKind.Reset;
                for (var i = 0; i < sources.Length; i++)
                {
                    var source = sources[i];
                    var previousIndex = ReferenceIdentity.IndexOf(binding.SourceStyles, source);
                    var replace = replaceAll ||
                                  (change.Kind is StateChangeKind.Value or StateChangeKind.Reset) &&
                                  ReferenceEquals(source, change.Style);
                    if (!replace && previousIndex >= 0)
                    {
                        current[i] = previous[previousIndex];
                        continue;
                    }
                    replacements ??= StylePreset.CreateRuntimeStyles(preset);
                    current[i] = replacements[i];
                }

                for (var i = 0; i < previous.Length; i++)
                    if (ReferenceIdentity.IndexOf(current, previous[i]) < 0 &&
                        ReferenceEquals(previous[i].ChangeSink, this))
                        previous[i].SetChangeSink(null);
                for (var i = 0; i < current.Length; i++)
                    if (ReferenceIdentity.IndexOf(previous, current[i]) < 0)
                        current[i].SetChangeSink(this);

                binding.SourceStyles = sources;
                binding.RuntimeStyles = current;
            }

            private static Style[] CopyStyles(IReadOnlyList<Style> source)
            {
                var result = new Style[source.Count];
                for (var i = 0; i < result.Length; i++) result[i] = source[i];
                return result;
            }

            private static bool Replay(Style targetStyle, BaseModifier authoredModifier,
                IStateMemberReplay source, StateMember member)
            {
                if (authoredModifier == null)
                    return targetStyle is IStateMemberReplay targetStyleState &&
                           targetStyleState.ReplayStateMember(source, member);
                return BaseModifier.TryReplayGraphState(targetStyle.Modifier,
                    authoredModifier, source, member);
            }

            private void ApplyRuntimeChange(in StyleChange change)
            {
                if (change.Structural)
                {
                    if (suppressStructuralChanges) return;
                    if (ReferenceEquals(change.StateSource, change.Style))
                    {
                        if (!parserAttached || ReconcileLiveStyles()) PublishStructuralChange();
                        return;
                    }
                    if (!change.Style.Enabled || !change.Style.IsValid) return;
                    PublishStructuralChange(change.RangeSource == null || !parserAttached);
                    return;
                }
                if (!change.Style.Enabled || !change.Style.IsValid) return;
                if (change.Modifier != null)
                    owner.MarkModifierDirty(change.Modifier, change.Flags);
                else
                    owner.SetDirty(change.Flags);
            }

            private bool ReconcileLiveStyles()
            {
                nextStyles.Clear();
                AddStyles(nextStyles, owner.styles);
                for (var i = 0; i < bindings.Count; i++)
                    AddStyles(nextStyles, bindings[i].RuntimeStyles);
                try
                {
                    var changed = parserAttached
                        ? ReconcileParser(nextStyles)
                        : !SameStyleOrder(liveStyles, nextStyles);
                    liveStyles.Clear();
                    AddStyles(liveStyles, nextStyles);
                    return changed;
                }
                finally
                {
                    nextStyles.Clear();
                }
            }

            private bool ReconcileParser(IReadOnlyList<Style> current)
            {
                if (reconciling)
                    throw new InvalidOperationException(
                        "Style registrations cannot be reconciled reentrantly.");
                reconciling = true;
                try
                {
                    return owner.attributeParser.ReconcileStyles(current);
                }
                finally
                {
                    reconciling = false;
                }
            }

            private static void AddStyles(List<Style> target, IReadOnlyList<Style> source)
            {
                for (var i = 0; i < source.Count; i++) target.Add(source[i]);
            }

            private static bool SameStyleOrder(IReadOnlyList<Style> left,
                IReadOnlyList<Style> right)
            {
                if (left.Count != right.Count) return false;
                for (var i = 0; i < left.Count; i++)
                    if (!ReferenceEquals(left[i], right[i])) return false;
                return true;
            }

            private Binding CreateBinding(StylePreset source)
            {
                var binding = new Binding(source, CopyStyles(source.Styles),
                    StylePreset.CreateRuntimeStyles(source));
                for (var i = 0; i < binding.RuntimeStyles.Length; i++)
                    binding.RuntimeStyles[i].SetChangeSink(this);
                return binding;
            }

            private void ReleaseBindingsFrom(int index)
            {
                for (var i = bindings.Count - 1; i >= index; i--)
                {
                    ReleaseBinding(bindings[i]);
                    bindings.RemoveAt(i);
                }
            }

            private void ReleaseBinding(Binding binding)
            {
                var styles = binding.RuntimeStyles;
                for (var i = 0; i < styles.Length; i++)
                    if (ReferenceEquals(styles[i].ChangeSink, this))
                        styles[i].SetChangeSink(null);
            }

            private void RefreshSubscriptions()
            {
                subscriptions ??= new ReferenceBinding<StylePreset>(Subscribe, Unsubscribe);
                subscriptions.Reconcile(desiredPresets);
            }

            private void Subscribe(StylePreset preset)
                => preset.DeltaChanged += OnPresetChanged;

            private void Unsubscribe(StylePreset preset)
                => preset.DeltaChanged -= OnPresetChanged;

            private void PublishStructuralChange(bool setDirty = true)
            {
                if (setDirty) owner.SetDirty(UniTextDirty.Text);
                if (parserAttached) owner.NotifyStyleGraphChanged();
            }
        }
    }
}
