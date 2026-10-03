using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    internal readonly struct ModifierGraphIssue
    {
        public readonly string path;
        public readonly string message;

        public ModifierGraphIssue(string path, string message)
        {
            this.path = path;
            this.message = message;
        }
    }

    internal static class ModifierGraphValidator
    {
        private const UniTextDirty EffectStages = UniTextDirty.Mesh | UniTextDirty.Positions |
                                                       UniTextDirty.Layout | UniTextDirty.Text;

        public static void Validate(UniTextBase text, List<ModifierGraphIssue> result)
        {
            ValidateStyles(text.Styles, "styles", result);
            var presets = text.StylePresets;
            for (var i = 0; i < presets.Count; i++)
                if (presets[i] != null)
                    ValidateStyles(presets[i].Styles, $"stylePresets[{i}].styles", result);
            if (text.UseGlobalStylePreset && UniTextSettings.GlobalStylePreset != null)
                ValidateStyles(UniTextSettings.GlobalStylePreset.Styles,
                    "globalStylePreset.styles", result);
        }

        public static void Validate(StylePreset preset, List<ModifierGraphIssue> result)
            => ValidateStyles(preset.Styles, "styles", result);

        public static void Validate(ModifierGraphPreset preset,
            List<ModifierGraphIssue> result)
            => ValidatePreset(preset, "root", result);

        internal static void ThrowIfInvalid(BaseModifier root,
            bool validateConfiguration = true)
        {
            if (root != null)
                ValidateRoot(root, "root", null,
                    validateConfiguration: validateConfiguration);
        }

        internal static void ThrowIfInvalid<TList>(TList roots,
            bool validateConfiguration = true)
            where TList : IReadOnlyList<BaseModifier>
        {
            var graph = Rent(null, validateConfiguration);
            try
            {
                for (var i = 0; i < roots.Count; i++)
                    if (roots[i] is { } root) graph.Visit(root, $"roots[{i}]");
                graph.Complete();
            }
            finally
            {
                Return(graph);
            }
        }

        internal static void ThrowIfInvalid(ModifierGraphPreset preset)
            => ValidatePreset(preset, "root", null);

        internal static void ThrowIfInvalid(RangeChannel channel)
        {
            if (channel != null) _ = channel.PayloadType;
        }

        internal static void CollectNodes(BaseModifier root, List<BaseModifier> result,
            bool ownedOnly)
        {
            if (root == null) return;
            var graph = Rent(null, false, result, ownedOnly);
            try
            {
                graph.Visit(root, "root");
            }
            finally
            {
                Return(graph);
            }
        }

        internal static BaseModifier FindRoot(BaseModifier node)
        {
            while (node?.AttachmentParent != null) node = node.AttachmentParent;
            return node;
        }

        private static void ValidateStyles(IReadOnlyList<Style> styles, string path,
            List<ModifierGraphIssue> result)
        {
            for (var i = 0; i < styles.Count; i++)
                if (styles[i]?.Modifier is { } modifier)
                    ValidateRoot(modifier, $"{path}[{i}].modifier", result);
        }

        private static void ValidateRoot(BaseModifier root, string path,
            List<ModifierGraphIssue> result,
            bool validateConfiguration = true)
        {
            var graph = Rent(result, validateConfiguration);
            try
            {
                graph.Visit(root, path);
                graph.Complete();
            }
            finally
            {
                Return(graph);
            }
        }

        private static void ValidatePreset(ModifierGraphPreset preset, string rootPath,
            List<ModifierGraphIssue> result)
        {
            var graph = Rent(result, true);
            try
            {
                graph.EnterPreset(preset, rootPath);
                graph.Complete();
            }
            finally
            {
                Return(graph);
            }
        }

        [ThreadStatic] private static Graph cachedGraph;

        private static Graph Rent(List<ModifierGraphIssue> result, bool validateConfiguration,
            List<BaseModifier> collected = null, bool ownedOnly = false)
        {
            var graph = cachedGraph ?? new Graph();
            cachedGraph = null;
            graph.Init(result, validateConfiguration, collected, ownedOnly);
            return graph;
        }

        private static void Return(Graph graph)
        {
            graph.Clear();
            cachedGraph = graph;
        }

        private sealed class Graph
        {
            private List<ModifierGraphIssue> result;
            private bool validateConfiguration;
            private List<BaseModifier> collected;
            private bool ownedOnly;
            private readonly List<BaseModifier> nodes = new();
            private readonly Dictionary<ModifierNodeId, BaseModifier> identified = new();
            private List<(ParameterRule rule, string path)> properties;
            private List<ModifierGraphPreset> visitingPresets;

            public void Init(List<ModifierGraphIssue> result, bool validateConfiguration,
                List<BaseModifier> collected, bool ownedOnly)
            {
                this.result = result;
                this.validateConfiguration = validateConfiguration;
                this.collected = collected;
                this.ownedOnly = ownedOnly;
            }

            public void Clear()
            {
                result = null;
                collected = null;
                nodes.Clear();
                identified.Clear();
                properties?.Clear();
                visitingPresets?.Clear();
            }

            public void EnterPreset(ModifierGraphPreset preset, string rootPath)
                => VisitPreset(preset, rootPath, false);

            public void Visit(BaseModifier node, string path)
            {
                if (node == null)
                {
                    Error(path, "Modifier node is null.");
                    return;
                }
                if (ReferenceIdentity.Contains(nodes, node))
                {
                    Error(path, "Modifier instance is used more than once or forms a cycle.");
                    return;
                }
                nodes.Add(node);
                collected?.Add(node);

                var nodeId = node.NodeId;
                if (!identified.TryAdd(nodeId, node))
                {
                    identified[nodeId] = null;
                    Error(path,
                        $"Modifier node identity '{nodeId}' is used more than once.");
                }

                if (validateConfiguration) ValidateNode(node, path);
                var children = ownedOnly ? node.OwnedChildren : node.Children;
                if (validateConfiguration && node is ModifierGraphModifier reference &&
                    children == null)
                {
                    if (reference.Preset == null)
                        Error($"{path}.preset", "ModifierGraphModifier requires a preset.");
                    else
                        VisitPreset(reference.Preset, $"{path}.preset");
                    return;
                }
                if (children == null) return;
                for (var i = 0; i < children.Count; i++)
                    Visit(children[i], $"{path}.children[{i}]");
            }

            public void Complete() { if (validateConfiguration) ValidateProperties(); }

            private void VisitPreset(ModifierGraphPreset preset, string path,
                bool nested = true)
            {
                if (visitingPresets != null &&
                    ReferenceIdentity.Contains(visitingPresets, preset))
                {
                    Error(path, "Modifier graph contains a recursive preset reference.");
                    return;
                }
                var rootPath = nested ? $"{path}.root" : path;
                if (preset.Root == null)
                {
                    Error(rootPath,
                        $"Modifier graph preset '{preset.name}' requires a root modifier.");
                    return;
                }
                (visitingPresets ??= new List<ModifierGraphPreset>()).Add(preset);
                Visit(preset.Root, rootPath);
                visitingPresets.RemoveAt(visitingPresets.Count - 1);
            }

            private void ValidateNode(BaseModifier modifier, string path)
            {
                if (modifier is InteractiveModifier interactive)
                {
                    ValidateChannel(interactive.Channel, $"{path}.channel");
                    ValidateRules(interactive, path);
                    var actions = interactive.Actions;
                    for (var i = 0; i < actions.Count; i++)
                        if (actions[i] == null)
                            Error($"{path}.actions[{i}]", "Interactive action is null.");
                }
                if (modifier is SemanticModifier semantic)
                    ValidateChannel(semantic.Channel, $"{path}.channel");
            }

            private void ValidateRules(InteractiveModifier owner, string ownerPath)
            {
                var rules = owner.Rules;
                for (var i = 0; i < rules.Count; i++)
                {
                    var path = $"{ownerPath}.rules[{i}]";
                    var rule = rules[i];
                    if (rule == null)
                    {
                        Error(path, "Interactive rule is null.");
                        continue;
                    }
                    if (rule.Playback == null) Error($"{path}.playback", "A rule requires a playback.");
                    else if (rule.Playback is SignalProgressPlayback progress &&
                             Mathf.Approximately(progress.InputMin, progress.InputMax))
                        Error($"{path}.driver",
                            "SignalProgress driver requires different input endpoints.");
                    if (rule.Selector == null && rule.Trigger == RangeRuleEvent.None)
                        Error(path, "Effect requires a selector, a trigger, or both.");
                    if (rule.Scope != RangeRuleScope.Entity &&
                        rule.Scope != RangeRuleScope.Segment)
                        Error($"{path}.scope", $"Effect has an invalid scope value {rule.Scope}.");

                    if (rule is ModifierRule modifierEffect)
                        ValidateModifierRule(modifierEffect, path);
                    else if (rule is ParameterRule parameterRule)
                        (properties ??= new List<(ParameterRule, string)>()).Add(
                            (parameterRule, path));
                    else
                        Error(path, $"Unsupported RangeStateRule type {rule.GetType().FullName}.");
                }
            }

            private void ValidateModifierRule(ModifierRule rule, string path)
            {
                if (rule.DirtyStage == UniTextDirty.None)
                    Error($"{path}.dirtyStage", "ModifierRule requires a dirty stage.");
                else if ((rule.DirtyStage & ~EffectStages) != 0)
                    Error($"{path}.dirtyStage",
                        $"ModifierRule uses unsupported dirty stages {rule.DirtyStage & ~EffectStages}.");
                if (rule.ModifierTemplate == null)
                    Error($"{path}.modifierTemplate", "ModifierRule requires a ModifierTemplate.");
            }

            private void ValidateProperties()
            {
                if (properties == null) return;
                for (var i = 0; i < properties.Count; i++)
                {
                    var (rule, path) = properties[i];
                    if (!Require(rule.TargetNode.IsValid, $"{path}.targetNode",
                            "ParameterRule requires a target modifier node.")) continue;
                    if (!Require(!string.IsNullOrWhiteSpace(rule.ParameterId),
                            $"{path}.parameterId", "ParameterRule requires a ParameterId.")) continue;
                    if (!Require(rule.TargetValue != null, $"{path}.targetValue",
                            "ParameterRule requires a typed TargetValue.")) continue;
                    if (!identified.TryGetValue(rule.TargetNode, out var target) || target == null)
                    {
                        Error($"{path}.targetNode",
                            $"Modifier node '{rule.TargetNode}' is not a unique target in this graph.");
                        continue;
                    }
                    var parameter = ParameterDescriptor.Find(target, rule.ParameterId);
                    if (parameter == null)
                    {
                        Error($"{path}.parameterId",
                            $"{target.GetType().Name} does not declare parameter '{rule.ParameterId}'.");
                        continue;
                    }
                    if (!Require(rule.TargetValue.ValueType == parameter.ValueType,
                            $"{path}.targetValue",
                            $"ParameterRule value type {rule.TargetValue.ValueType.Name} is incompatible with " +
                            $"{target.GetType().Name}.{parameter.Id} ({parameter.ValueType.Name}).")) continue;
                    var composition = GetComposition(rule.Composition);
                    if (composition == ParameterCompositions.None)
                    {
                        Error($"{path}.composition",
                            $"ParameterRule has an invalid composition value {rule.Composition}.");
                        continue;
                    }
                    if ((parameter.SupportedCompositions & composition) == 0)
                        Error($"{path}.composition",
                            $"{target.GetType().Name}.{parameter.Id} does not support {rule.Composition}.");
                }
            }

            private void ValidateChannel(RangeChannel channel, string path)
            {
                if (channel == null) return;
                try
                {
                    ThrowIfInvalid(channel);
                }
                catch (InvalidOperationException exception)
                {
                    Error(path, exception.Message);
                }
            }

            private bool Require(bool condition, string path, string message)
            {
                if (condition) return true;
                Error(path, message);
                return false;
            }

            private void Error(string path, string message)
            {
                if (result == null)
                    throw new InvalidOperationException(
                        string.IsNullOrEmpty(path) ? message : $"{path}: {message}");
                result.Add(new ModifierGraphIssue(path, message));
            }
        }

        private static ParameterCompositions GetComposition(
            ParameterComposition value)
            => value switch
            {
                ParameterComposition.Replace => ParameterCompositions.Replace,
                ParameterComposition.Add => ParameterCompositions.Add,
                ParameterComposition.Multiply => ParameterCompositions.Multiply,
                ParameterComposition.Custom => ParameterCompositions.Custom,
                _ => ParameterCompositions.None,
            };
    }
}
