using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>Immutable input supplied to custom serialized-property UI Toolkit renderers.</summary>
    public readonly struct SerializedPropertyContext
    {
        /// <summary>Creates renderer context directly from one serialized field.</summary>
        public SerializedPropertyContext(SerializedProperty property, string label = null)
            : this(new SerializedPropertyBinding(property), property,
                label ?? property?.displayName)
        {
        }

        /// <summary>Creates renderer context for one bound serialized field.</summary>
        public SerializedPropertyContext(SerializedPropertyBinding binding, SerializedProperty property,
            string label)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            if (property == null) throw new ArgumentNullException(nameof(property));
            Property = property.Copy();
            Label = label;
        }

        /// <summary>The Unity-serialized binding that feeds generated runtime reconciliation.</summary>
        public SerializedPropertyBinding Binding { get; }
        /// <summary>A stable copy of the serialized property used for metadata and change tracking.</summary>
        public SerializedProperty Property { get; }
        /// <summary>The display label selected by the caller.</summary>
        public string Label { get; }
        /// <summary>The primary target's current serialized value.</summary>
        public object Value => Binding.Value;
        /// <summary>The exact serialized value type.</summary>
        public Type ValueType => Binding.ValueType;

        /// <summary>Writes through Unity serialization on every selected target.</summary>
        public void SetValue(object value, string undoName = null)
            => Binding.SetValue(value, undoName);

        /// <summary>Refreshes a custom view after serialized changes and Undo/Redo.</summary>
        public T Observe<T>(T element, Action refresh) where T : VisualElement
            => SerializedPropertyField.Observe(element, refresh, Property);

        /// <summary>
        /// Refreshes a custom view with the property reacquired from the binding, which is what a structural
        /// edit or an Undo requires.
        /// </summary>
        /// <remarks>
        /// A property that no longer resolves is an intentionally supported state, not an error: the object or
        /// the field is gone and the view is about to be rebuilt or discarded, so the refresh is skipped. That
        /// decision lives here so it is made once rather than answered differently at each call site.
        /// </remarks>
        public T Observe<T>(T element, Action<SerializedProperty> refresh) where T : VisualElement
        {
            if (refresh == null) throw new ArgumentNullException(nameof(refresh));
            var binding = Binding;
            return SerializedPropertyField.Observe(element, () =>
            {
                var current = binding.FindSerializedProperty();
                if (current != null) refresh(current);
            }, Property);
        }

        /// <summary>Edits this property independently on every selected target through Unity serialization.</summary>
        public void Edit(Action<SerializedProperty> edit, string undoName = null)
            => Binding.EditSerializedProperties(edit, undoName);

        /// <summary>Completes a custom control that represents this serialized property.</summary>
        public T Bind<T>(T element, Action refresh = null) where T : VisualElement
        {
            if (refresh != null) Observe(element, refresh);
            SerializedPropertyField.Configure(element, Binding, refresh);
            return element;
        }

        /// <summary>
        /// Binds a custom value field through the standard serialized-property pipeline. Supply
        /// <paramref name="read"/> and <paramref name="write"/> when the displayed value is a
        /// projection; supply <paramref name="merge"/> when a compound edit must preserve unchanged
        /// parts independently on every selected target.
        /// </summary>
        public BaseField<TValue> Bind<TValue>(BaseField<TValue> field,
            Func<TValue, object> write = null,
            Func<object, TValue, TValue, object> merge = null,
            Func<object, TValue> read = null,
            string undoName = null)
            => SerializedPropertyField.Bind(this, field, write, merge, read, undoName);

    }

    /// <summary>
    /// Owns the canonical UI Toolkit lifecycle for serialized fields. Use <see cref="Create"/> for
    /// standard controls, <see cref="SerializedPropertyContext.Bind{T}(T, Action)"/> for custom
    /// controls, <see cref="Observe{T}"/> for derived presentation, and <see cref="OnChange{T}"/>
    /// for serialized side effects.
    /// </summary>
    public static class SerializedPropertyField
    {
        private const string ListDropTargetUssClassName = "lightside-list__header--drop-target";
        private static readonly Dictionary<Type, Func<SerializedPropertyContext, VisualElement>> renderers = new();
        private static readonly Dictionary<Type, Func<SerializedPropertyContext, VisualElement>> attributeRenderers = new();
        private static readonly Dictionary<Type, Func<SerializedPropertyContext, VisualElement>> attributeHeaderRenderers = new();
        private static readonly HashSet<TrackedRefresh> trackedRefreshes = new();
        private static readonly HashSet<TrackedRefresh> pendingRefreshes = new();
        private static readonly List<TrackedRefresh> refreshSnapshot = new();
        private static readonly HashSet<SerializedObject> refreshObjects =
            new(ReferenceIdentityComparer<SerializedObject>.Instance);
        private static bool refreshingTrackedProperties;
        private static bool refreshScheduled;

        static SerializedPropertyField()
        {
            RegisterAttributeRenderer<VectorDragFieldAttribute>(CreateVector2Drag);
            SerializedPropertyBinding.Written += QueueRefresh;
            SerializedStateEditorDriver.ObjectsChanged += QueueRefresh;
            Undo.undoRedoPerformed += QueueAllRefreshes;
        }

        /// <summary>Refreshes a view when any represented property changes, including Undo/Redo.</summary>
        public static T Observe<T>(T element, Action refresh,
            params SerializedProperty[] properties) where T : VisualElement
        {
            Track(element, refresh, properties);
            refresh();
            return element;
        }

        /// <summary>Invokes an effect after represented properties change without running it initially.</summary>
        public static T OnChange<T>(T element, Action changed,
            params SerializedProperty[] properties) where T : VisualElement
        {
            Track(element, changed, properties);
            return element;
        }

        internal static void Track(VisualElement element, Action refresh,
            params SerializedProperty[] properties)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (refresh == null) throw new ArgumentNullException(nameof(refresh));
            if (properties == null || properties.Length == 0)
                throw new ArgumentException("At least one serialized property is required.",
                    nameof(properties));

            var tracked = new SerializedProperty[properties.Length];
            var paths = new string[properties.Length];
            var contentHashes = new uint[properties.Length];
            for (var i = 0; i < properties.Length; i++)
            {
                if (properties[i] == null)
                    throw new ArgumentNullException(nameof(properties));
                tracked[i] = properties[i].Copy();
                paths[i] = properties[i].propertyPath;
                contentHashes[i] = properties[i].contentHash;
            }

            var pending = false;
            var refreshing = false;
            var tracking = false;
            TrackedRefresh trackedRefresh = null;

            bool HasPointerCapture()
            {
                if (element.panel == null) return false;
                var capture = PointerCaptureHelper.GetCapturingElement(
                    element.panel, PointerId.mousePointerId) as VisualElement;
                return capture != null && (capture == element || element.Contains(capture));
            }

            bool CaptureContentHashes(out bool changed)
            {
                changed = false;
                for (var i = 0; i < tracked.Length; i++)
                {
                    var owner = tracked[i].serializedObject;
                    if (!IsSerializedObjectCurrent(owner)) owner.UpdateIfRequiredOrScript();
                    var current = owner.FindProperty(paths[i]);
                    if (current == null) return false;
                    var hash = current.contentHash;
                    if (contentHashes[i] != hash) changed = true;
                    contentHashes[i] = hash;
                }
                return true;
            }

            void ApplyRefresh(bool contentCurrent = false)
            {
                if (refreshing) return;
                refreshing = true;
                pending = false;
                try
                {
                    if (contentCurrent || CaptureContentHashes(out _)) refresh();
                }
                finally
                {
                    refreshing = false;
                }
            }

            void RefreshChangedContent()
            {
                if (!CaptureContentHashes(out var changed) || !changed) return;
                if (HasPointerCapture())
                {
                    pending = true;
                    return;
                }
                ApplyRefresh(true);
            }

            void Flush()
            {
                if (pending) ApplyRefresh();
            }

            void SetTracking(bool value)
            {
                if (tracking == value) return;
                tracking = value;
                if (value)
                {
                    trackedRefreshes.Add(trackedRefresh);
                }
                else
                {
                    trackedRefreshes.Remove(trackedRefresh);
                    pendingRefreshes.Remove(trackedRefresh);
                }
            }

            trackedRefresh = new TrackedRefresh(element, RefreshChangedContent, tracked);
            element.RegisterCallback<AttachToPanelEvent>(_ => SetTracking(true));
            element.RegisterCallback<DetachFromPanelEvent>(_ => SetTracking(false));
            element.RegisterCallback<PointerUpEvent>(_ => Flush(), TrickleDown.TrickleDown);
            element.RegisterCallback<PointerCaptureOutEvent>(_ => Flush(),
                TrickleDown.TrickleDown);
            if (element.panel != null) SetTracking(true);
        }

        private static void QueueRefresh(IReadOnlyCollection<Object> targets)
        {
            foreach (var refresh in trackedRefreshes)
                if (refresh.Matches(targets)) pendingRefreshes.Add(refresh);
            FlushRefreshes();
        }

        internal static bool IsSerializedObjectCurrent(SerializedObject serializedObject)
            => refreshingTrackedProperties && refreshObjects.Contains(serializedObject);

        private static void QueueAllRefreshes()
        {
            pendingRefreshes.UnionWith(trackedRefreshes);
            FlushRefreshes();
        }

        /// <summary>
        /// Runs pending refreshes in the frame that queued them, so dependent views repaint with
        /// the write. A write performed by a refresh itself defers to the next editor tick.
        /// </summary>
        private static void FlushRefreshes()
        {
            if (refreshingTrackedProperties) ScheduleRefresh();
            else RefreshTrackedProperties();
        }

        private static void ScheduleRefresh()
        {
            if (refreshScheduled || pendingRefreshes.Count == 0) return;
            refreshScheduled = true;
            EditorApplication.delayCall += RefreshTrackedProperties;
        }

        private static void RefreshTrackedProperties()
        {
            refreshScheduled = false;
            refreshSnapshot.Clear();
            refreshSnapshot.AddRange(pendingRefreshes);
            pendingRefreshes.Clear();
            refreshSnapshot.Sort(CompareRefreshDepth);
            refreshObjects.Clear();
            for (var i = 0; i < refreshSnapshot.Count; i++)
                if (trackedRefreshes.Contains(refreshSnapshot[i]))
                    refreshSnapshot[i].CollectSerializedObjects(refreshObjects);
            try
            {
                foreach (var serializedObject in refreshObjects)
                    serializedObject.UpdateIfRequiredOrScript();
                refreshingTrackedProperties = true;
                for (var i = 0; i < refreshSnapshot.Count; i++)
                    if (trackedRefreshes.Contains(refreshSnapshot[i]))
                        refreshSnapshot[i].Refresh();
            }
            finally
            {
                refreshingTrackedProperties = false;
                refreshObjects.Clear();
                refreshSnapshot.Clear();
            }
        }

        private static int CompareRefreshDepth(TrackedRefresh left, TrackedRefresh right)
            => VisualDepth(left.Element).CompareTo(VisualDepth(right.Element));

        private static int VisualDepth(VisualElement element)
        {
            var depth = 0;
            while ((element = element.parent) != null) depth++;
            return depth;
        }

        private sealed class TrackedRefresh
        {
            private readonly HashSet<int> targetIds = new();
            private readonly IReadOnlyList<SerializedProperty> properties;

            public TrackedRefresh(VisualElement element, Action refresh,
                IReadOnlyList<SerializedProperty> properties)
            {
                Element = element;
                Refresh = refresh;
                this.properties = properties;
                for (var i = 0; i < properties.Count; i++)
                    foreach (var target in properties[i].serializedObject.targetObjects)
                    {
                        if (target == null) continue;
                        targetIds.Add(ObjectUtils.GetInstanceIdCompat(target));
                        if (target is Component component)
                            targetIds.Add(ObjectUtils.GetInstanceIdCompat(component.gameObject));
                    }
            }

            public VisualElement Element { get; }
            public Action Refresh { get; }

            public void CollectSerializedObjects(ISet<SerializedObject> result)
            {
                for (var i = 0; i < properties.Count; i++)
                    result.Add(properties[i].serializedObject);
            }

            public bool Matches(IReadOnlyCollection<Object> targets)
            {
                foreach (var target in targets)
                    if (target != null &&
                        targetIds.Contains(ObjectUtils.GetInstanceIdCompat(target))) return true;
                return false;
            }
        }

        /// <summary>
        /// Registers the shared renderer for a serialized value type or interface. Controls created by
        /// the renderer must use the supplied context to bind or observe their serialized state.
        /// </summary>
        public static void RegisterRenderer<T>(Func<SerializedPropertyContext, VisualElement> renderer)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            renderers[typeof(T)] = renderer;
        }

        /// <summary>Registers a renderer selected by a serialized field attribute.</summary>
        public static void RegisterAttributeRenderer<T>(
            Func<SerializedPropertyContext, VisualElement> renderer)
            where T : PropertyAttribute
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            attributeRenderers[typeof(T)] = renderer;
        }

        /// <summary>
        /// Registers the shared renderer for a value type resolved at run time — the form discovery uses, where
        /// the selecting type is read off a drawer rather than written at the call site.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="valueType"/> or <paramref name="renderer"/> is <see langword="null"/>.</exception>
        internal static void RegisterRenderer(Type valueType,
            Func<SerializedPropertyContext, VisualElement> renderer)
        {
            if (valueType == null) throw new ArgumentNullException(nameof(valueType));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            renderers[valueType] = renderer;
        }

        /// <inheritdoc cref="RegisterRenderer(Type,Func{SerializedPropertyContext,VisualElement})"/>
        internal static void RegisterAttributeRenderer(Type attributeType,
            Func<SerializedPropertyContext, VisualElement> renderer)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            attributeRenderers[attributeType] = renderer;
        }

        /// <summary>Registers a compact header action selected by a serialized field attribute.</summary>
        public static void RegisterAttributeHeaderRenderer<T>(
            Func<SerializedPropertyContext, VisualElement> renderer)
            where T : PropertyAttribute
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            attributeHeaderRenderers[typeof(T)] = renderer;
        }

        /// <summary>Creates the registered trailing header action for a field, when one exists.</summary>
        public static bool TryCreateHeaderAction(SerializedProperty property,
            out VisualElement action)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var binding = new SerializedPropertyBinding(property);
            var renderer = FindAttributeRenderer(binding.SerializedField, attributeHeaderRenderers);
            if (renderer == null)
            {
                action = null;
                return false;
            }

            var context = new SerializedPropertyContext(binding, property, property.displayName);
            action = renderer(context) ?? throw new InvalidOperationException(
                $"Header renderer for '{binding.SerializedField.Name}' returned null.");
            return true;
        }

        /// <summary>
        /// Creates an Undoable, prefab-aware control for one serialized field, rendered by an
        /// explicit renderer instead of registry lookup — for values whose presentation depends on
        /// context the field's own declaration cannot carry.
        /// </summary>
        public static VisualElement Create(SerializedProperty property, string label,
            Func<SerializedPropertyContext, VisualElement> renderer)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            var binding = new SerializedPropertyBinding(property);
            var context = new SerializedPropertyContext(binding, property,
                label ?? property.displayName);
            return Finish(renderer(context), context);
        }

        /// <summary>
        /// Creates the control for a field of the edited object, named by its serialized path. Resolving the
        /// path here is what keeps a required field's absence an error rather than a null dereference three
        /// calls later.
        /// </summary>
        /// <exception cref="InvalidOperationException">The object declares no such property.</exception>
        public static VisualElement Create(SerializedObject serializedObject, string propertyPath,
            string label = null)
            => Create(InspectorHelpers.RequireProperty(serializedObject, propertyPath), label);

        /// <summary>
        /// Creates the control for a field of <paramref name="parent"/>, named by its relative path. Named
        /// apart from <see cref="Create(SerializedProperty,string)"/> because the two would otherwise be
        /// indistinguishable at a two-argument call site, where the second string would silently become a label.
        /// </summary>
        /// <exception cref="InvalidOperationException"><paramref name="parent"/> declares no such property.</exception>
        public static VisualElement CreateRelative(SerializedProperty parent, string relativePath,
            string label = null)
            => Create(InspectorHelpers.RequireRelative(parent, relativePath), label);

        /// <summary>Creates an Undoable, prefab-aware control for one serialized field.</summary>
        public static VisualElement Create(SerializedProperty property, string label = null)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var binding = new SerializedPropertyBinding(property);
            var context = new SerializedPropertyContext(binding, property, label ?? property.displayName);
            if (FindAttributeRenderer(binding.SerializedField) is { } attributeRenderer)
                return Finish(attributeRenderer(context), context);
            if (FindRenderer(binding.ValueType) is { } renderer)
                return Finish(renderer(context), context);
            if (IsCollectionWrapper(binding.ValueType))
                return CreateCollectionWrapper(context);
            if (IsCollection(binding.ValueType))
                return CreateCollection(context);

            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                if (binding.SerializedField.GetCustomAttribute<TypeSelectorAttribute>() != null)
                    return Finish(TypeSelectorDrawer.CreateToolkit(context), context);
                if (property.managedReferenceValue is { } reference &&
                    FindRenderer(reference.GetType()) is { } referenceRenderer)
                    return Finish(referenceRenderer(context), context);
                return Unsupported(context,
                    "Managed references require [TypeSelector] for serialized editing.");
            }

            var value = binding.Value;
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    return Bind(context, CreateStringField(context));
                case SerializedPropertyType.Boolean:
                    return Bind(context, new InspectorToggle(context.Label));
                case SerializedPropertyType.Integer:
                    return CreateInteger(context);
                case SerializedPropertyType.Float:
                    return CreateFloat(context);
                case SerializedPropertyType.Enum:
                    return CreateEnum(context, (Enum)value);
                case SerializedPropertyType.Color:
                    return Bind(context, new ColorField(context.Label),
                        color => binding.ValueType == typeof(Color32) ? (object)(Color32)color : color,
                        MergeColor,
                        current => current is Color32 current32
                            ? (Color)current32
                            : (Color)current);
                case SerializedPropertyType.Vector2:
                    return Bind(context, new Vector2Field(context.Label),
                        merge: (current, previous, next) =>
                            MergeVector2((Vector2)current, previous, next));
                case SerializedPropertyType.Vector3:
                    return Bind(context, new Vector3Field(context.Label),
                        merge: (current, previous, next) =>
                            MergeVector3((Vector3)current, previous, next));
                case SerializedPropertyType.Vector4:
                    return Bind(context, new Vector4Field(context.Label),
                        merge: (current, previous, next) =>
                            MergeVector4((Vector4)current, previous, next));
                case SerializedPropertyType.Vector2Int:
                    return Bind(context, new Vector2IntField(context.Label),
                        merge: (current, previous, next) =>
                            MergeVector2Int((Vector2Int)current, previous, next));
                case SerializedPropertyType.Vector3Int:
                    return Bind(context, new Vector3IntField(context.Label),
                        merge: (current, previous, next) =>
                            MergeVector3Int((Vector3Int)current, previous, next));
                case SerializedPropertyType.Rect:
                    return Bind(context, new RectField(context.Label),
                        merge: (current, previous, next) =>
                            MergeRect((Rect)current, previous, next));
                case SerializedPropertyType.Bounds:
                    return Bind(context, new BoundsField(context.Label),
                        merge: (current, previous, next) =>
                            MergeBounds((Bounds)current, previous, next));
                case SerializedPropertyType.AnimationCurve:
                    return Bind(context, new CurveField(context.Label));
                case SerializedPropertyType.Gradient:
                    return Bind(context, new GradientField(context.Label));
                case SerializedPropertyType.ObjectReference:
                {
                    var type = binding.ValueType;
                    var allowSceneObjects = AllowsSceneObjects(context, type);
                    var field = new InspectorObjectField(
                        context.Label, type, allowSceneObjects);
                    return Bind(context, field);
                }
                case SerializedPropertyType.Generic:
                    return CreateContainer(context);
                default:
                    return Unsupported(context,
                        $"{binding.ValueType.Name} has no serialized UI Toolkit renderer.");
            }
        }

        /// <summary>Creates the standard field and verifies its concrete control type.</summary>
        public static T Create<T>(SerializedProperty property, string label = null)
            where T : VisualElement
        {
            var element = Create(property, label);
            return element as T ?? throw new InvalidOperationException(
                $"Serialized property '{property.propertyPath}' created {element.GetType().Name}, " +
                $"not {typeof(T).Name}.");
        }

        /// <summary>Creates a pill toggle bound to one serialized Boolean field.</summary>
        public static InspectorPillButton CreateToggle(SerializedProperty property,
            string label = null, Action changed = null)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var binding = new SerializedPropertyBinding(property);
            if (binding.ValueType != typeof(bool))
                throw new ArgumentException("A toggle requires a Boolean property.", nameof(property));
            var text = label ?? property.displayName;
            InspectorPillButton toggle = null;

            void Refresh()
            {
                toggle.SetState(
                    (bool)binding.Value, binding.HasMultipleValues, EditorResources.ToggleAccent);
                changed?.Invoke();
            }

            toggle = new InspectorPillButton(() =>
            {
                binding.SetValue(
                    binding.HasMultipleValues || !(bool)binding.Value, $"Change {text}");
                Refresh();
            })
            {
                text = text,
                tooltip = property.tooltip,
            };
            return new SerializedPropertyContext(binding, property, text).Bind(toggle, Refresh);
        }

        /// <summary>
        /// Creates the shared collection editor while delegating element selection to the owning feature.
        /// The callback supplies the add-button anchor and an insertion action that applies to every
        /// selected target with independent element instances.
        /// </summary>
        public static VisualElement CreateCollection(SerializedProperty property, string label,
            Action<Rect, Action<object>> selectElement)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (selectElement == null) throw new ArgumentNullException(nameof(selectElement));
            var binding = new SerializedPropertyBinding(property);
            if (!IsCollection(binding.ValueType))
                throw new InvalidOperationException(
                    $"Serialized property '{property.propertyPath}' is not a collection.");
            return CreateCollection(new SerializedPropertyContext(
                binding, property, label ?? property.displayName), selectElement);
        }

        private static VisualElement CreateInteger(SerializedPropertyContext context)
        {
            if (context.ValueType == typeof(LayerMask))
                return Bind(context, new LayerMaskField { label = context.Label },
                    next => (LayerMask)next,
                    (current, previous, next) =>
                        (LayerMask)MergeMask(((LayerMask)current).value, previous, next),
                    current => ((LayerMask)current).value);
            if (context.ValueType == typeof(int))
            {
                if (context.Binding.SerializedField.GetCustomAttribute<RangeAttribute>() is { } range)
                    return Bind(context,
                        new InspectorSliderInt(context.Label,
                            Mathf.RoundToInt(range.min), Mathf.RoundToInt(range.max)));
                return Bind(context, new IntegerField(context.Label));
            }
            if (context.ValueType == typeof(long))
                return Bind(context, new LongField(context.Label));
            if (context.ValueType == typeof(uint))
                return Bind(context, new LongField(context.Label),
                    next => checked((uint)Math.Max(0L, next)),
                    read: current => (uint)current);
            return Unsupported(context,
                $"Integer type {context.ValueType.Name} needs an explicit renderer.");
        }

        private static VisualElement CreateFloat(SerializedPropertyContext context)
        {
            if (context.ValueType == typeof(float))
            {
                if (context.Binding.SerializedField.GetCustomAttribute<RangeAttribute>() is { } range)
                    return Bind(context,
                        new InspectorSlider(context.Label, range.min, range.max));
                return Bind(context, new FloatField(context.Label));
            }
            if (context.ValueType == typeof(double))
                return Bind(context, new DoubleField(context.Label));
            return Unsupported(context,
                $"Floating-point type {context.ValueType.Name} needs an explicit renderer.");
        }

        private static VisualElement CreateEnum(SerializedPropertyContext context, Enum value)
            => context.ValueType.IsDefined(typeof(FlagsAttribute), false)
                ? Bind(context, new EnumSelectorField(context.Label, value),
                    merge: MergeFlags)
                : Bind(context, new EnumSelectorField(context.Label, value, IsAuthorableMember));

        private static readonly Dictionary<Type, HashSet<long>> hiddenEnumMembers = new();

        /// <summary>
        /// Excludes an enum's negative inheritance sentinel (see <see cref="InheritableAttribute"/>) and any
        /// member marked <see cref="HideFromTypeSelectorAttribute"/> from a plain field's choices, so one enum
        /// can serve both a resolved value and an override, and can carry values other machinery produces
        /// without offering them by hand. A field carrying the inheritance attribute is drawn by its own
        /// renderer and keeps the sentinel.
        /// </summary>
        private static bool IsAuthorableMember(Enum member)
            => Convert.ToInt64(member) >= 0L && !HiddenMembers(member.GetType()).Contains(Convert.ToInt64(member));

        private static HashSet<long> HiddenMembers(Type enumType)
        {
            if (hiddenEnumMembers.TryGetValue(enumType, out var hidden)) return hidden;
            hidden = new HashSet<long>();
            foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.IsDefined(typeof(HideFromTypeSelectorAttribute), false))
                    hidden.Add(Convert.ToInt64(field.GetValue(null)));
            hiddenEnumMembers[enumType] = hidden;
            return hidden;
        }

        private static VisualElement CreateVector2Drag(SerializedPropertyContext context)
            => Bind(context, new EditorVector2DragField(context.Label),
                merge: (current, previous, next) =>
                    MergeVector2((Vector2)current, previous, next));

        private static TextField CreateStringField(SerializedPropertyContext context)
        {
            var fieldInfo = context.Binding.SerializedField;
            var textArea = fieldInfo.GetCustomAttribute<TextAreaAttribute>();
            var multiline = textArea != null || fieldInfo.GetCustomAttribute<MultilineAttribute>() != null;
            TextField field = multiline
                ? new InspectorTextArea(context.Label)
                : new TextField(context.Label);
            if (textArea != null)
                field.style.minHeight = Mathf.Max(1, textArea.minLines) * 18f;
            return field;
        }

        private static VisualElement CreateContainer(SerializedPropertyContext context)
        {
            var root = new VisualElement();
            root.AddToClassList("lightside-inspector-container");
            var header = new InspectorFoldoutHeader(context.Label);
            header.AddToClassList("lightside-inspector-container__header");
            var actions = header.ActionRow;
            root.Add(header);
            var body = InspectorVisuals.CreateHierarchyBody(header);
            body.AddToClassList("lightside-inspector-container__body");
            root.Add(body);

            void Rebuild()
            {
                var current = context.Binding.FindSerializedProperty();
                if (current == null) return;
                InspectorVisuals.ClearContent(actions);
                InspectorVisuals.ClearContent(body);
                foreach (var child in InspectorHelpers.VisibleChildren(current))
                {
                    if (TryCreateHeaderAction(child, out var action))
                    {
                        action.RegisterCallback<ChangeEvent<bool>>(_ => Rebuild());
                        actions.Add(action);
                    }
                    body.Add(Create(child));
                }
                InspectorVisuals.RefreshHierarchyBody(header, body, current.isExpanded);
            }

            header.Changed += value =>
            {
                var current = context.Binding.FindSerializedProperty();
                if (current == null) return;
                current.isExpanded = value;
                Rebuild();
            };
            Rebuild();
            return Finish(root, context, Rebuild);
        }

        private static VisualElement CreateCollection(SerializedPropertyContext context,
            Action<Rect, Action<object>> selectElement = null)
        {
            InspectorListView list = null;
            list = new InspectorListView(context.Label,
                () =>
                {
                    var row = new CollectionRow(index => RemoveAt(index));
                    row.Field.AddToClassList("lightside-inspector-list__element");
                    row.Field.AddToClassList("lightside-list__field");
                    return row;
                },
                (element, index) =>
                {
                    var row = (CollectionRow)element;
                    var property = context.Binding.FindSerializedProperty();
                    if (property == null || (uint)index >= (uint)property.arraySize) return;
                    var item = property.GetArrayElementAtIndex(index);
                    var itemLabel = item.propertyType == SerializedPropertyType.Generic
                        ? item.displayName
                        : string.Empty;
                    var field = Create(item, itemLabel);
                    field.AddToClassList("lightside-list__element-field");
                    row.Field.Add(field);
                },
                element => InspectorVisuals.ClearContent(((CollectionRow)element).Field),
                true, index => CollectionItemIdentity(
                    context.Binding.FindSerializedProperty(), index));
            list.AddToClassList("lightside-inspector-list");
            var elementType = ElementType(context.ValueType);

            void Rebuild()
            {
                var property = context.Binding.FindSerializedProperty();
                if (property == null || !property.isArray) return;
                GetCollectionSizeRange(context, out var minimum, out var maximum);
                list.Rebuild(minimum, property.isExpanded);
                list.Header.SetCount(minimum, maximum);
            }

            void EditCollections(string undoName, Action<SerializedProperty> edit)
                => context.Binding.EditSerializedProperties(edit, undoName);

            list.ExpandedChanged += value =>
            {
                var property = context.Binding.FindSerializedProperty();
                if (property == null) return;
                property.isExpanded = value;
                Rebuild();
            };
            list.ItemIndexChanged += (from, to) =>
            {
                var undoName = $"Reorder {context.Label}";
                EditCollections(undoName, property =>
                {
                    if ((uint)from >= (uint)property.arraySize ||
                        (uint)to >= (uint)property.arraySize) return;
                    property.MoveArrayElement(from, to);
                });
                Rebuild();
            };
            void PasteCollection(ClipboardTargetKind kind, ClipboardEntry entry, string undoName)
            {
                var target = new ClipboardTarget(elementType, kind);
                EditCollections(undoName,
                    current => PropertyClipboard.Paste(current, target, entry));
                Rebuild();
            }

            list.HeaderMenuOpening += menu =>
            {
                var property = context.Binding.FindSerializedProperty();
                if (property == null || !property.isArray) return;
                InspectorContextMenu.AppendCommand(menu, "Copy List",
                    InspectorContextMenu.CopyIcon, _ =>
                    {
                        var current = context.Binding.FindSerializedProperty();
                        if (current != null && current.isArray)
                            PropertyClipboard.CopyCollection(current, elementType);
                    });
                PropertyClipboardMenu.AppendPaste(menu, "Paste List",
                    new ClipboardTarget(elementType, ClipboardTargetKind.CollectionReplace),
                    entry => PasteCollection(ClipboardTargetKind.CollectionReplace, entry,
                        $"Paste {context.Label}"));
                PropertyClipboardMenu.AppendPaste(menu, "Paste Element",
                    new ClipboardTarget(elementType, ClipboardTargetKind.CollectionAppend),
                    entry => PasteCollection(ClipboardTargetKind.CollectionAppend, entry,
                        $"Paste Element into {context.Label}"));
                PrefabOverrideMenu.Append(menu, context.Binding, Rebuild);
            };
            list.RowMenuOpening += (menu, index) =>
            {
                var property = context.Binding.FindSerializedProperty();
                if (property == null || (uint)index >= (uint)property.arraySize) return;
                PopulateCollectionElementMenu(menu,
                    new SerializedPropertyBinding(property.GetArrayElementAtIndex(index)),
                    Rebuild);
            };
            list.ClearRequested += () =>
            {
                EditCollections($"Clear {context.Label}", property => property.arraySize = 0);
                Rebuild();
            };
            void Insert(object item)
            {
                GetCollectionSizeRange(context, out var insertionIndex, out _);
                EditCollections($"Add {context.Label}", property =>
                {
                    InsertElement(property, insertionIndex, item);
                    property.isExpanded = true;
                });
                Rebuild();
            }

            void InsertElement(SerializedProperty property, int index, object item)
            {
                property.InsertArrayElementAtIndex(index);
                var element = property.GetArrayElementAtIndex(index);
                var value = PropertyClipboard.CloneObject(item);
                if (element.propertyType == SerializedPropertyType.ManagedReference)
                    element.managedReferenceValue = value;
                else if (element.propertyType == SerializedPropertyType.ObjectReference)
                    element.objectReferenceValue = (Object)value;
                else
                    element.boxedValue = value;
            }

            if (typeof(Object).IsAssignableFrom(elementType))
            {
                var allowSceneObjects = AllowsSceneObjects(context, elementType);

                bool HasValidDrop()
                {
                    var values = DragAndDrop.objectReferences;
                    for (var i = 0; i < values.Length; i++)
                        if (TryResolveDraggedObject(values[i], elementType,
                                allowSceneObjects, out _))
                            return true;
                    return false;
                }

                void ClearDropTarget() =>
                    list.Header.RemoveFromClassList(ListDropTargetUssClassName);

                list.Header.RegisterCallback<DragUpdatedEvent>(evt =>
                {
                    var valid = HasValidDrop();
                    DragAndDrop.visualMode = valid
                        ? DragAndDropVisualMode.Copy
                        : DragAndDropVisualMode.Rejected;
                    list.Header.EnableInClassList(ListDropTargetUssClassName, valid);
                    evt.StopPropagation();
                });
                list.Header.RegisterCallback<DragPerformEvent>(evt =>
                {
                    if (!HasValidDrop()) return;
                    DragAndDrop.AcceptDrag();
                    var values = DragAndDrop.objectReferences;
                    GetCollectionSizeRange(context, out var insertionIndex, out _);
                    EditCollections($"Add {context.Label}", property =>
                    {
                        var index = insertionIndex;
                        for (var i = 0; i < values.Length; i++)
                            if (TryResolveDraggedObject(values[i], elementType,
                                    allowSceneObjects, out var value))
                                InsertElement(property, index++, value);
                        property.isExpanded = true;
                    });
                    ClearDropTarget();
                    Rebuild();
                    evt.StopPropagation();
                });
                list.Header.RegisterCallback<DragLeaveEvent>(_ => ClearDropTarget());
                list.Header.RegisterCallback<DragExitedEvent>(_ => ClearDropTarget());
            }

            list.Header.AddButton.clicked += () =>
            {
                if (selectElement != null)
                {
                    selectElement(list.Header.AddButton.worldBound, Insert);
                    return;
                }
                if (typeof(Object).IsAssignableFrom(elementType))
                {
                    ObjectSelector.Show(list.Header.AddButton.worldBound, elementType, null,
                        selected => Insert(selected), false,
                        AllowsSceneObjects(context, elementType));
                    return;
                }
                if (elementType.IsAbstract || elementType.IsInterface)
                {
                    ManagedReferenceTypeMenu.Show(list.Header.AddButton.worldBound, elementType, null,
                        type => Insert(ManagedReferenceTypeMenu.Create(type)), false);
                    return;
                }
                Insert(DefaultElement(elementType));
            };
            void RemoveAt(int index)
            {
                list.NoteRemoval(index);
                EditCollections($"Remove {context.Label}", property =>
                {
                    if ((uint)index >= (uint)property.arraySize) return;
                    var count = property.arraySize;
                    property.DeleteArrayElementAtIndex(index);
                    if (property.arraySize == count) property.DeleteArrayElementAtIndex(index);
                });
                Rebuild();
            }
            return Finish(context.Observe(list, Rebuild), context);
        }

        private static void GetCollectionSizeRange(SerializedPropertyContext context,
            out int minimum, out int maximum)
        {
            var min = int.MaxValue;
            var max = 0;
            context.Binding.VisitTargetProperties((_, property) =>
            {
                if (!property.isArray)
                    throw new InvalidOperationException(
                        $"Serialized property '{property.propertyPath}' is not a collection.");
                min = Math.Min(min, property.arraySize);
                max = Math.Max(max, property.arraySize);
            });
            minimum = min == int.MaxValue ? 0 : min;
            maximum = max;
        }

        private static object CollectionItemIdentity(SerializedProperty collection, int index)
        {
            if (collection == null || (uint)index >= (uint)collection.arraySize)
                return null;
            var element = collection.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ManagedReference)
                return element.managedReferenceId >= 0
                    ? (1, element.managedReferenceId)
                    : null;
            if (element.propertyType == SerializedPropertyType.ObjectReference)
                return element.objectReferenceValue != null
                    ? (2, element.objectReferenceValue)
                    : null;
            if (element.propertyType == SerializedPropertyType.Generic)
            {
                var child = element.Copy();
                var end = child.GetEndProperty();
                while (child.Next(true) && !SerializedProperty.EqualContents(child, end))
                    if (child.propertyType == SerializedPropertyType.ManagedReference &&
                        child.managedReferenceId >= 0)
                        return (1, child.managedReferenceId);
            }
            return null;
        }

        private sealed class CollectionRow : VisualElement
        {
            public CollectionRow(Action<int> remove)
            {
                Content = new VisualElement();
                Content.AddToClassList("lightside-list__content");
                Field = new VisualElement();
                Content.Add(Field);
                Add(Content);
                Add(InspectorListView.CreateRemoveButton(() =>
                {
                    if (userData is int index && index >= 0) remove(index);
                }, "Remove element"));
            }

            public VisualElement Content { get; }
            public VisualElement Field { get; }
        }

        private static bool IsCollection(Type type)
            => type.IsArray || typeof(IList).IsAssignableFrom(type);

        private static bool IsCollectionWrapper(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (!current.IsGenericType) continue;
                var definition = current.GetGenericTypeDefinition();
                if (definition == typeof(TypedList<>) || definition == typeof(StyledList<>))
                    return true;
            }
            return false;
        }

        private static VisualElement CreateCollectionWrapper(SerializedPropertyContext context)
        {
            var wrapper = context.Binding.RequireSerializedProperty();
            var items = InspectorHelpers.RequireRelative(wrapper, "items");
            var result = Create(items, context.Label);
            result.tooltip = Tooltip(context.Binding);
            return result;
        }

        private static object DefaultElement(Type elementType)
        {
            if (elementType == typeof(string)) return string.Empty;
            if (typeof(Object).IsAssignableFrom(elementType)) return null;
            return Activator.CreateInstance(elementType);
        }

        private static bool TryResolveDraggedObject(Object source, Type elementType,
            bool allowSceneObjects, out Object value)
        {
            value = source;
            if (!elementType.IsInstanceOfType(value))
            {
                if (source is GameObject gameObject &&
                    typeof(Component).IsAssignableFrom(elementType))
                    value = gameObject.GetComponent(elementType);
                else if (source is Component component && elementType == typeof(GameObject))
                    value = component.gameObject;
                else
                    value = null;
            }
            return value != null && (allowSceneObjects || EditorUtility.IsPersistent(value));
        }

        private static bool AllowsSceneObjects(SerializedPropertyContext context, Type type)
        {
            if (type != typeof(GameObject) && !typeof(Component).IsAssignableFrom(type))
                return false;
            var targets = context.Binding.SerializedObject.targetObjects;
            for (var i = 0; i < targets.Length; i++)
                if (EditorUtility.IsPersistent(targets[i])) return false;
            return true;
        }

        private static Type ElementType(Type type)
            => SerializedReflection.GetCollectionElementType(type) ??
               throw new InvalidOperationException(
                   $"Serialized collection property '{type.FullName}' must be an array or List<T>.");

        internal static BaseField<TValue> Bind<TValue>(SerializedPropertyContext context,
            BaseField<TValue> field, Func<TValue, object> write = null,
            Func<object, TValue, TValue, object> merge = null,
            Func<object, TValue> read = null,
            string undoName = null)
        {
            void RefreshField() => Refresh(context, field, read);

            void Apply(TValue previous, TValue next)
            {
                if (context.Binding.FindSerializedProperty() == null) return;
                if (merge == null)
                    context.Binding.SetValue(
                        write == null ? next : write(next), undoName);
                else
                    context.Binding.TransformValue(current =>
                        merge(current, previous, next), undoName);
                RefreshField();
            }

            if (typeof(TValue) == typeof(string) && (VisualElement)field is InspectorTextArea textArea)
                textArea.RegisterSerializedValueChanged((previous, next) =>
                    Apply((TValue)(object)previous, (TValue)(object)next));
            else
                field.RegisterValueChangedCallback(evt => Apply(evt.previousValue, evt.newValue));
            Track(field, RefreshField, context.Property);
            RefreshField();
            return (BaseField<TValue>)Finish(field, context, RefreshField);
        }

        private static void Refresh<TValue>(SerializedPropertyContext context,
            BaseField<TValue> field, Func<object, TValue> read)
        {
            var current = context.Binding.Value;
            var value = read == null ? (TValue)current : read(current);
            if (!EqualityComparer<TValue>.Default.Equals(field.value, value))
                field.SetValueWithoutNotify(value);
            field.showMixedValue = read == null
                ? context.Binding.HasMultipleValues
                : context.Binding.HaveDifferentValues(read);
            if ((VisualElement)field is TextField textField)
                InspectorVisuals.RefreshTextFieldPresentation(textField);
        }

        private static object MergeColor(object current, Color previous, Color next)
        {
            var value = current is Color32 color32 ? (Color)color32 : (Color)current;
            if (next.r != previous.r) value.r = next.r;
            if (next.g != previous.g) value.g = next.g;
            if (next.b != previous.b) value.b = next.b;
            if (next.a != previous.a) value.a = next.a;
            return current is Color32 ? (object)(Color32)value : value;
        }

        private static int MergeMask(int current, int previous, int next)
        {
            var changed = previous ^ next;
            return current & ~changed | next & changed;
        }

        private static object MergeFlags(object current, Enum previous, Enum next)
        {
            var type = current.GetType();
            var currentBits = EnumBits((Enum)current);
            var previousBits = EnumBits(previous);
            var nextBits = EnumBits(next);
            var changed = previousBits ^ nextBits;
            return Enum.ToObject(type, currentBits & ~changed | nextBits & changed);
        }

        private static ulong EnumBits(Enum value)
        {
            return Type.GetTypeCode(Enum.GetUnderlyingType(value.GetType())) switch
            {
                TypeCode.SByte => unchecked((ulong)Convert.ToSByte(value)),
                TypeCode.Int16 => unchecked((ulong)Convert.ToInt16(value)),
                TypeCode.Int32 => unchecked((ulong)Convert.ToInt32(value)),
                TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(value)),
                TypeCode.Byte => Convert.ToByte(value),
                TypeCode.UInt16 => Convert.ToUInt16(value),
                TypeCode.UInt32 => Convert.ToUInt32(value),
                TypeCode.UInt64 => Convert.ToUInt64(value),
                _ => throw new InvalidOperationException(
                    $"Unsupported enum storage type {Enum.GetUnderlyingType(value.GetType()).FullName}."),
            };
        }

        private static Vector2 MergeVector2(Vector2 current, Vector2 previous, Vector2 next)
        {
            if (next.x != previous.x) current.x = next.x;
            if (next.y != previous.y) current.y = next.y;
            return current;
        }

        private static Vector3 MergeVector3(Vector3 current, Vector3 previous, Vector3 next)
        {
            if (next.x != previous.x) current.x = next.x;
            if (next.y != previous.y) current.y = next.y;
            if (next.z != previous.z) current.z = next.z;
            return current;
        }

        private static Vector4 MergeVector4(Vector4 current, Vector4 previous, Vector4 next)
        {
            if (next.x != previous.x) current.x = next.x;
            if (next.y != previous.y) current.y = next.y;
            if (next.z != previous.z) current.z = next.z;
            if (next.w != previous.w) current.w = next.w;
            return current;
        }

        private static Vector2Int MergeVector2Int(Vector2Int current,
            Vector2Int previous, Vector2Int next)
        {
            if (next.x != previous.x) current.x = next.x;
            if (next.y != previous.y) current.y = next.y;
            return current;
        }

        private static Vector3Int MergeVector3Int(Vector3Int current,
            Vector3Int previous, Vector3Int next)
        {
            if (next.x != previous.x) current.x = next.x;
            if (next.y != previous.y) current.y = next.y;
            if (next.z != previous.z) current.z = next.z;
            return current;
        }

        private static Rect MergeRect(Rect current, Rect previous, Rect next)
        {
            if (next.x != previous.x) current.x = next.x;
            if (next.y != previous.y) current.y = next.y;
            if (next.width != previous.width) current.width = next.width;
            if (next.height != previous.height) current.height = next.height;
            return current;
        }

        private static Bounds MergeBounds(Bounds current, Bounds previous, Bounds next)
        {
            current.center = MergeVector3(current.center, previous.center, next.center);
            current.size = MergeVector3(current.size, previous.size, next.size);
            return current;
        }

        private static VisualElement Finish(VisualElement element, SerializedPropertyContext context,
            Action changed = null)
        {
            if (element == null)
                throw new InvalidOperationException(
                    $"Serialized renderer for '{context.ValueType.FullName}' returned null.");
            if (string.IsNullOrEmpty(context.Label) &&
                element.ClassListContains(BaseField<int>.ussClassName))
                element.AddToClassList(BaseField<int>.noLabelVariantUssClassName);
            var configured = Configure(element, context.Binding, changed);
            if (!string.IsNullOrWhiteSpace(context.Label))
                InspectorVisuals.SetTooltipTitle(configured, context.Label);
            return configured;
        }

        /// <summary>
        /// Completes a manually bound custom control with shared inspector styling, metadata tooltip,
        /// prefab-override state and the standard property context menu.
        /// </summary>
        internal static T Configure<T>(T element, SerializedPropertyBinding binding,
            Action changed = null) where T : VisualElement
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            InspectorVisuals.Attach(element);
            if (string.IsNullOrEmpty(element.tooltip)) element.tooltip = Tooltip(binding);
            InspectorVisuals.SetTooltipTitle(element, binding.DisplayName);
            element.AddToClassList(InspectorVisuals.TooltipOwnerClass);
            element.AddToClassList("lightside-serialized-property-field");
            InspectorVisuals.AlignFieldHierarchy(element);
            PrefabOverrideVisual.AttachDefault(element, binding);
            AddContextMenu(element, binding, changed);
            return element;
        }

        internal static string Tooltip(SerializedPropertyBinding binding)
            => binding.SerializedField.GetCustomAttribute<TooltipAttribute>()?.tooltip;

        /// <summary>
        /// Adds an exact prefab-override indicator to a custom serialized control. Repeated bindings
        /// from the same serialized object combine several represented values into one indicator;
        /// set <paramref name="includeChildren"/> when the control represents a whole container.
        /// </summary>
        public static void AddPrefabOverrideIndicator(VisualElement element,
            SerializedPropertyBinding binding, bool includeChildren = false)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            PrefabOverrideVisual.Attach(element, binding, includeChildren: includeChildren);
        }

        /// <summary>Adds standard serialized clipboard, duplication and prefab-override actions.</summary>
        public static void AddContextMenu(VisualElement element, SerializedPropertyBinding binding,
            Action changed = null)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (element.ClassListContains(
                    InspectorContextMenuManipulator.OwnerUssClassName)) return;
            element.AddManipulator(new InspectorContextMenuManipulator(menu =>
            {
                if (binding.TryGetContainingCollection(out _, out _))
                {
                    PopulateCollectionElementMenu(menu, binding, changed);
                    return;
                }

                if (binding.Value != null)
                    InspectorContextMenu.AppendCommand(menu, "Copy",
                        InspectorContextMenu.CopyIcon, _ => PropertyClipboard.Copy(
                            binding.RequireSerializedProperty(), binding.ValueType));

                var target = PropertyClipboardMenu.ValueTarget(binding);
                PropertyClipboardMenu.AppendPaste(menu, "Paste", target, entry =>
                {
                    binding.EditSerializedProperties(
                        current => PropertyClipboard.Paste(current, target, entry), "Paste");
                    changed?.Invoke();
                });

                PrefabOverrideMenu.Append(menu, binding, changed);
            }));
        }

        internal static void AppendDuplicateAction(DropdownMenu menu,
            SerializedPropertyBinding binding)
        {
            if (!binding.TryGetContainingCollection(out var collection, out var index)) return;
            InspectorContextMenu.AppendCommand(menu, "Duplicate",
                InspectorContextMenu.DuplicateIcon, _ =>
                    collection.EditSerializedProperties(property =>
                    {
                        if ((uint)index >= (uint)property.arraySize) return;
                        PropertyClipboard.DuplicateElement(property, index);
                    }, $"Duplicate {binding.DisplayName}"));
        }

        private static void PopulateCollectionElementMenu(DropdownMenu menu,
            SerializedPropertyBinding binding, Action changed)
        {
            var property = binding.FindSerializedProperty();
            if (property == null) return;
            InspectorContextMenu.AppendCommand(menu, "Copy",
                InspectorContextMenu.CopyIcon, _ =>
                {
                    var current = binding.FindSerializedProperty();
                    if (current != null) PropertyClipboard.Copy(current, binding.ValueType);
                });

            var target = PropertyClipboardMenu.ValueTarget(binding);
            PropertyClipboardMenu.AppendPaste(menu, "Paste", target, entry =>
            {
                binding.EditSerializedProperties(
                    current => PropertyClipboard.Paste(current, target, entry), "Paste Element");
                changed?.Invoke();
            });

            AppendDuplicateAction(menu, binding);
            PrefabOverrideMenu.Append(menu, binding, changed);
        }

        private static VisualElement Unsupported(SerializedPropertyContext context, string message)
        {
            var box = new HelpBox($"{context.Label}: {message}", HelpBoxMessageType.Error);
            box.AddToClassList("lightside-serialized-property-field--unsupported");
            return Finish(box, context);
        }

        /// <summary>Whether a type renderer would draw a value of <paramref name="type"/>.</summary>
        internal static bool HasRenderer(Type type)
            => type != null && FindRenderer(type) != null;

        private static Func<SerializedPropertyContext, VisualElement> FindRenderer(Type type)
        {
            if (renderers.TryGetValue(type, out var exact)) return exact;
            Func<SerializedPropertyContext, VisualElement> best = null;
            Type bestType = null;
            foreach (var pair in renderers)
            {
                if (!pair.Key.IsAssignableFrom(type)) continue;
                if (bestType == null || bestType.IsAssignableFrom(pair.Key))
                {
                    bestType = pair.Key;
                    best = pair.Value;
                }
            }
            return best;
        }

        private static Func<SerializedPropertyContext, VisualElement> FindAttributeRenderer(
            FieldInfo field, Dictionary<Type, Func<SerializedPropertyContext, VisualElement>> source = null)
        {
            source ??= attributeRenderers;
            foreach (var attribute in field.GetCustomAttributes<PropertyAttribute>())
                if (source.TryGetValue(attribute.GetType(), out var renderer))
                    return renderer;
            return null;
        }

    }
}
