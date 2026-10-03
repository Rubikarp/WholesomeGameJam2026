using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// A disclosure whose expanded state is the serialized property's own, over a hierarchy body that
    /// withdraws its affordance while it has nothing to show. Children added to it become body content.
    /// </summary>
    /// <remarks>
    /// The expanded state is view state, not a serialized value: it is written straight to the reacquired
    /// property and never applied through <see cref="SerializedPropertyContext.Edit"/>, so toggling a
    /// disclosure neither dirties the object nor flushes an unrelated pending edit.
    /// <para>
    /// The body is built whether or not the disclosure is open. A body populated only while expanded locks
    /// itself shut, because the disclosure is withdrawn from an empty body and a collapsed row then never
    /// becomes openable again.
    /// </para>
    /// </remarks>
    public sealed class InspectorSerializedFoldout : VisualElement
    {
        private readonly SerializedPropertyContext context;
        private readonly VisualElement body;
        private Action<SerializedProperty> content;

        /// <summary>
        /// Creates the disclosure over the context's property. Call
        /// <see cref="Observe(Action{SerializedProperty})"/> once the content is built.
        /// </summary>
        public InspectorSerializedFoldout(SerializedPropertyContext context)
        {
            this.context = context;
            InspectorVisuals.Attach(this);
            AddToClassList("lightside-inspector-container");
            Header = new InspectorFoldoutHeader();
            Header.AddToClassList("lightside-inspector-container__header");
            hierarchy.Add(Header);
            body = InspectorVisuals.CreateHierarchyBody(Header);
            body.AddToClassList("lightside-inspector-container__body");
            hierarchy.Add(body);

            Header.Changed += expanded =>
            {
                var property = this.context.Binding.FindSerializedProperty();
                if (property == null) return;
                property.isExpanded = expanded;
                Apply(property);
            };
        }

        /// <summary>The header, whose <see cref="InspectorFoldoutHeader.Actions"/> carry the summary row.</summary>
        public InspectorFoldoutHeader Header { get; }

        /// <summary>
        /// Whether the header offers to collapse. False where the body is the whole presentation and there
        /// is nothing to collapse to.
        /// </summary>
        public bool Collapsible { get; set; } = true;

        /// <inheritdoc/>
        public override VisualElement contentContainer => body;

        /// <summary>
        /// Adopts the refresh that rebuilds the body, and runs it now and after every serialized change and
        /// Undo/Redo. The body's visibility and the header's disclosure follow it automatically.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="refresh"/> is <see langword="null"/>.</exception>
        public InspectorSerializedFoldout Observe(Action<SerializedProperty> refresh)
        {
            content = refresh ?? throw new ArgumentNullException(nameof(refresh));
            return context.Observe(this, Apply);
        }

        /// <summary>
        /// Completes a disclosure whose body is built once, so only the disclosure itself tracks the
        /// property.
        /// </summary>
        public InspectorSerializedFoldout Observe() => context.Observe(this, Apply);

        /// <summary>
        /// Re-runs the refresh against the reacquired property, for a change no serialized tracking reports.
        /// Does nothing when the property no longer resolves.
        /// </summary>
        public void Refresh()
        {
            var property = context.Binding.FindSerializedProperty();
            if (property != null) Apply(property);
        }

        private void Apply(SerializedProperty property)
        {
            content?.Invoke(property);
            InspectorVisuals.RefreshHierarchyBody(Header, body, property.isExpanded, Collapsible);
        }
    }
}
