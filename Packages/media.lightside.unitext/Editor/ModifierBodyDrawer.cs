using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal sealed class ModifierStructureChangedEvent : EventBase<ModifierStructureChangedEvent>
    {
        protected override void Init()
        {
            base.Init();
            bubbles = true;
        }
    }

    /// <summary>
    /// Draws modifier configuration and live <c>[Parameter]</c> values. Default-valued parameters
    /// stay hidden unless the user reveals them for the current object session.
    /// </summary>
    internal sealed class ModifierBodyDrawer : IManagedReferenceDrawer
    {
        private readonly string moveField;
        private readonly string afterField;

        internal ModifierBodyDrawer(string moveField = null, string afterField = null)
        {
            this.moveField = moveField;
            this.afterField = afterField;
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            TypedManagedReferenceDrawerRegistry.Register(typeof(BaseModifier),
                new ModifierBodyDrawer());
        }

        private static readonly OptInListState state = new("LightSide.UniText.ModifierParameter.");

        /// <inheritdoc/>
        public VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var modifier = property.managedReferenceValue as BaseModifier;
            if (modifier == null) return new VisualElement();

            var root = InspectorVisuals.CreateStack();
            UniTextInspectorTheme.Initialize(root);
            root.AddToClassList("unitext-managed-reference-body");
            var members = ParameterFieldUtility.GetParameterMembers(modifier.GetType());
            var parameterPaths = ParameterPaths(members);
            AddVisibleConfiguration(root, property, parameterPaths, moveField, afterField);

            if (modifier is CompositeModifier)
            {
                var items = InspectorHelpers.RequireRelative(property, "modifiers.items");
                SerializedPropertyField.OnChange(root, () =>
                {
                    using var evt = ModifierStructureChangedEvent.GetPooled();
                    evt.target = root;
                    root.SendEvent(evt);
                }, items);
            }

            var schema = ParameterFieldUtility.GetFields(modifier.GetType());
            if (CreateLiveParameterList(property, modifier, schema, members) is { } parameters)
                root.Add(parameters);
            return root;
        }

        private static void AddVisibleConfiguration(VisualElement root,
            SerializedProperty property, HashSet<string> parameterPaths,
            string moveField, string afterField)
        {
            SerializedProperty moved = null;
            foreach (var child in VisibleConfiguration(property, parameterPaths))
            {
                if (child.name == moveField)
                {
                    moved = child.Copy();
                    continue;
                }
                AddConfigurationField(root, property, child);
                if (moved != null && child.name == afterField)
                {
                    AddConfigurationField(root, property, moved);
                    moved = null;
                }
            }
            if (moved != null) AddConfigurationField(root, property, moved);
        }

        private static void AddConfigurationField(VisualElement root,
            SerializedProperty owner, SerializedProperty property)
            => root.Add(SerializedPropertyField.Create(property));

        internal static VisualElement CreateLiveParameterList(SerializedProperty property,
            object owner)
        {
            if (owner == null) return null;
            return CreateLiveParameterList(property, owner,
                ParameterFieldUtility.GetFields(owner.GetType()),
                ParameterFieldUtility.GetParameterMembers(owner.GetType()));
        }

        private static VisualElement CreateLiveParameterList(SerializedProperty property,
            object owner, ParameterFieldUtility.ParamField[] schema,
            ParameterFieldUtility.ParameterMember[] members)
            => schema.Length == 0 ? null : new LiveParameterList(property, owner, schema, members);

        private sealed class LiveParameterList : VisualElement
        {
            private readonly SerializedPropertyBinding binding;
            private readonly Type ownerType;
            private readonly ParameterFieldUtility.ParamField[] schema;
            private readonly ParameterFieldUtility.ParameterMember[] members;
            private readonly SerializedPropertyBinding[] memberBindings;
            private readonly string key;
            private readonly List<int> visibleRows = new();
            private readonly InspectorListView list;
            private string[] tokens;
            private int[] variants;
            private ParameterFieldUtility.ParamContext parameterContext;

            public LiveParameterList(SerializedProperty property, object owner,
                ParameterFieldUtility.ParamField[] schema,
                ParameterFieldUtility.ParameterMember[] members)
            {
                binding = new SerializedPropertyBinding(property);
                ownerType = owner.GetType();
                this.schema = schema;
                this.members = members;
                memberBindings = new SerializedPropertyBinding[members.Length];
                var observedProperties = new SerializedProperty[members.Length + 1];
                observedProperties[0] = property;
                for (var i = 0; i < members.Length; i++)
                {
                    var member = InspectorHelpers.RequireRelative(
                        property, members[i].SerializedPath);
                    memberBindings[i] = new SerializedPropertyBinding(member);
                    observedProperties[i + 1] = member;
                }
                key = OptInListState.StateKey(property);
                state.RestoreRevealed(key, members.Length);
                UniTextInspectorTheme.Initialize(this);
                list = new InspectorListView("Parameters",
                    DefaultParameterDrawer.CreateParameterRow, BindRow,
                    InspectorVisuals.ClearContent, false, RowIdentity, RefreshRow);
                Add(list);
                list.ExpandedChanged += value => state.SetExpanded(key, value);
                list.ClearRequested += ResetParameters;
                var add = list.Header.AddButton;
                add.clicked += () =>
                {
                    if (tokens != null) OpenParameterSelector(add.worldBound);
                };
                SerializedPropertyField.Observe(this, Refresh, observedProperties);
            }

            private void Refresh()
            {
                var property = binding.FindSerializedProperty();
                if (property == null) return;
                var owner = ResolveOwner();
                if (owner == null) return;
                ReadTokens();
                parameterContext = new ParameterFieldUtility.ParamContext(owner, variants);
                visibleRows.Clear();
                for (var i = 0; i < schema.Length; i++)
                    if (ParameterFieldUtility.RowShown(
                            ParameterFieldUtility.RowFilter.LiveDiff,
                            schema, tokens, i, index => state.IsRevealed(key, index), parameterContext) ||
                        memberBindings[i].HasMultipleValues)
                        visibleRows.Add(i);
                list.Rebuild(visibleRows.Count, state.GetExpanded(key));
            }

            private void ReadTokens()
            {
                tokens ??= new string[members.Length];
                variants ??= new int[members.Length];
                for (var i = 0; i < members.Length; i++)
                {
                    var member = members[i];
                    var value = memberBindings[i].Value;
                    tokens[i] = ParameterFieldUtility.EncodeToken(
                        in schema[i], ParameterFieldUtility.FormatToken(member, value));
                    variants[i] = ParameterFieldUtility.ResolveVariantIndex(in schema[i], value);
                }
            }

            private object ResolveOwner()
            {
                var current = InspectorHelpers.ResolveInstance(
                    binding.SerializedObject.targetObject, binding.PropertyPath);
                return current != null && current.GetType() == ownerType ? current : null;
            }

            private object ResolveOwner(UnityEngine.Object target)
            {
                var current = SerializedPropertyBinding.ResolveInstance(
                    target, binding.PropertyPath);
                return current.GetType() == ownerType
                    ? current
                    : throw new InvalidOperationException(
                        $"Selected modifier at '{binding.PropertyPath}' changed type from '{ownerType.FullName}' to '{current.GetType().FullName}'.");
            }

            private object RowIdentity(int displayIndex)
                => visibleRows[displayIndex];

            private void BindRow(VisualElement row, int displayIndex)
            {
                var property = binding.FindSerializedProperty();
                if (property == null) return;
                var memberIndex = visibleRows[displayIndex];
                var serializedMember = InspectorHelpers.RequireRelative(
                    property, members[memberIndex].SerializedPath);
                var memberBinding = new SerializedPropertyBinding(serializedMember);
                var field = ParameterFieldUtility.CreateToolkitField(
                    schema[memberIndex], tokens[memberIndex], parameterContext, memberIndex,
                    memberBinding.HasMultipleValues,
                    (previous, next) =>
                    {
                        state.SetRevealed(key, memberIndex, true);
                        memberBinding.TransformValue((target, value) =>
                            ParameterFieldUtility.MergeParameterValue(
                                members[memberIndex], in schema[memberIndex], value,
                                previous, next,
                                new ParameterFieldUtility.ParamContext(ResolveOwner(target)),
                                parameterContext, memberIndex),
                            "Change " + schema[memberIndex].name);
                        if (ParameterFieldUtility.AffectsSiblingVisibility(schema, memberIndex))
                            Refresh();
                    });
                new SerializedPropertyContext(memberBinding, serializedMember,
                        schema[memberIndex].name)
                    .Bind(field);
                field.tooltip = members[memberIndex]
                    .GetCustomAttribute<TooltipAttribute>()?.tooltip;
                field.AddToClassList(InspectorVisuals.TooltipOwnerClass);
                field.AddToClassList(InspectorVisuals.ParameterFieldClass);
                row.Add(field);
                row.Add(InspectorListView.CreateRemoveButton(() =>
                {
                    var current = binding.FindSerializedProperty();
                    if (current == null) return;
                    ResetField(current, members[memberIndex],
                        schema[memberIndex], key, memberIndex);
                    Refresh();
                }, $"Reset {schema[memberIndex].name}"));
            }

            private void RefreshRow(VisualElement row, int displayIndex)
            {
                var memberIndex = visibleRows[displayIndex];
                ParameterFieldUtility.RefreshToolkitField(row, tokens[memberIndex], parameterContext,
                    memberBindings[memberIndex].HasMultipleValues);
            }

            /// <summary>
            /// Opens the parameter toggle selector: checked entries are the rows the list currently
            /// shows; choosing one reveals it with its activation value or resets it back to the
            /// default and hides it.
            /// </summary>
            private void OpenParameterSelector(Rect rect)
            {
                var items = ParameterFieldUtility.BuildToggleItems(
                    ParameterFieldUtility.RowFilter.LiveDiff, schema, tokens, parameterContext);
                if (items.Length == 0) return;
                Selector.ShowMultiple(rect, items,
                    value => value is int index && visibleRows.Contains(index),
                    value =>
                    {
                        if (value is not int index) return;
                        var current = binding.FindSerializedProperty();
                        if (current == null) return;
                        if (visibleRows.Contains(index))
                        {
                            ResetField(current, members[index], schema[index], key, index);
                        }
                        else
                        {
                            state.SetRevealed(key, index, true);
                            var activation = schema[index].activationValue;
                            if (activation != null)
                                ApplyField(current, members[index], schema[index],
                                    schema[index].name, activation);
                            state.SetExpanded(key, true);
                        }
                        Refresh();
                        InternalEditorUtility.RepaintAllViews();
                    });
            }

            private void ResetParameters()
            {
                for (var i = 0; i < schema.Length; i++)
                    state.SetRevealed(key, i, false);
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Reset Parameters");
                var group = Undo.GetCurrentGroup();
                for (var i = 0; i < schema.Length; i++)
                    memberBindings[i].SetValue(
                        ParameterFieldUtility.ParseParameterValue(
                            members[i], in schema[i],
                            ParameterFieldUtility.EncodeToken(
                                in schema[i], schema[i].defaultValue),
                            parameterContext, i),
                        "Reset Parameters");
                Undo.CollapseUndoOperations(group);
                Refresh();
            }
        }

        #region Apply serialized state

        private static void ResetField(SerializedProperty property,
            ParameterFieldUtility.ParameterMember member,
            ParameterFieldUtility.ParamField pf, string key, int index)
        {
            state.SetRevealed(key, index, false);
            ApplyField(property, member, pf, pf.name, pf.defaultValue);
        }

        /// <summary>
        /// Applies the edit to the counterpart serialized field on every compatible selected object.
        /// </summary>
        private static void ApplyField(SerializedProperty property,
            ParameterFieldUtility.ParameterMember member,
            ParameterFieldUtility.ParamField field, string label, string token)
            => ApplyField(property.serializedObject, property.propertyPath,
                member, field, label, token);

        private static void ApplyField(SerializedObject so, string path,
            ParameterFieldUtility.ParameterMember member,
            ParameterFieldUtility.ParamField pf,
            string label, string token)
        {
            var value = ParameterFieldUtility.ParseParameterValue(
                member, in pf, ParameterFieldUtility.EncodeToken(in pf, token));
            var field = InspectorHelpers.RequireProperty(
                so, path + "." + member.SerializedPath);
            new SerializedPropertyBinding(field).SetValue(value, "Change " + label);
        }

        #endregion

        private static readonly Dictionary<ParameterFieldUtility.ParameterMember[], HashSet<string>>
            parameterPathCache = new();

        private static HashSet<string> ParameterPaths(ParameterFieldUtility.ParameterMember[] members)
        {
            if (parameterPathCache.TryGetValue(members, out var cached)) return cached;
            var set = new HashSet<string>(members.Length);
            foreach (var member in members) set.Add(member.SerializedPath);
            parameterPathCache[members] = set;
            return set;
        }

        private static IEnumerable<SerializedProperty> VisibleConfiguration(SerializedProperty property,
            HashSet<string> parameterPaths, string prefix = "")
        {
            foreach (var child in InspectorHelpers.VisibleChildren(property))
            {
                var path = prefix + child.name;
                if (parameterPaths.Contains(path)) continue;
                if (ContainsChildPath(parameterPaths, path))
                {
                    foreach (var nested in VisibleConfiguration(child, parameterPaths, path + "."))
                        yield return nested;
                    continue;
                }
                yield return child;
            }
        }

        private static bool ContainsChildPath(HashSet<string> paths, string parent)
        {
            var prefix = parent + ".";
            foreach (var path in paths)
                if (path.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
