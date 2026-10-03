using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Combines modifiers and splits semicolon-delimited parameters by child order.</summary>
    [Serializable]
    [TypeGroup("Utility", 10)]
    [TypeDescription("Combines multiple modifiers into one, splitting parameters by ';'.")]
    public sealed partial class CompositeModifier : BaseModifier
    {
        /// <summary>Child modifiers applied in order.</summary>
        [Tooltip("Child modifiers to apply in order. Parameters are split by ';'.")]
        [SerializeField, StateList(nameof(ApplyModifiersChange),
            Validator = nameof(ValidateModifiersMutation), Owned = true)]
        private TypedList<BaseModifier> modifiers = new();

        [NonSerialized] private readonly List<BaseModifier> initializedChildren = new();
        [NonSerialized] private ReferenceBinding<BaseModifier> boundChildren;

        internal override IReadOnlyList<BaseModifier> Children => modifiers.Items;

        /// <summary>
        /// The style-level capability test: returns <paramref name="root"/> itself, or the first
        /// composite leaf (depth-first), assignable to <paramref name="modifierType"/>; null when none.
        /// Every "does this style speak X" lookup goes through here so composites are never invisible.
        /// </summary>
        public static BaseModifier FindLeaf(BaseModifier root, Type modifierType)
        {
            if (root == null || modifierType == null) return null;
            if (modifierType.IsInstanceOfType(root)) return root;
            if (root.Children is not { } children) return null;
            for (int i = 0; i < children.Count; i++)
            {
                var leaf = FindLeaf(children[i], modifierType);
                if (leaf != null) return leaf;
            }
            return null;
        }

        internal override void SetChangeSink(IModifierChangeSink sink)
        {
            if (sink != null)
                ValidateGraph(this);
            boundChildren?.Clear();
            SetChangeSinkAfterValidation(sink);
            ReconcileBoundChildren();
        }

        internal override void SetOwner(UniTextBase owner, IModifierChangeSink sink = null)
        {
            base.SetOwner(owner, sink);
            for (var i = 0; i < modifiers.Count; i++)
            {
                var child = modifiers[i];
                if (!ReferenceEquals(child?.AttachmentParent, this)) continue;
                child.SetOwner(owner, ChangeSink);
            }
        }

        private void ApplyModifiersChange()
        {
            ReconcileInitializedChildren();
            ReconcileBoundChildren();
            MarkStructureChanged();
        }

        private void ReconcileBoundChildren()
        {
            boundChildren ??= new ReferenceBinding<BaseModifier>(ConnectChild, DisconnectChild);
            boundChildren.Reconcile(modifiers);
        }

        private void ConnectChild(BaseModifier child)
        {
            child.SetAttachmentParent(this);
            if (ChangeSink != null) child.SetChangeSink(ChangeSink);
        }

        private void DisconnectChild(BaseModifier child)
        {
            if (!ReferenceEquals(child.AttachmentParent, this)) return;
            child.SetAttachmentParent(null);
            if (ChangeSink != null && ReferenceEquals(child.ChangeSink, ChangeSink))
                child.SetChangeSink(null);
        }

        private void ReconcileInitializedChildren()
        {
            for (var i = initializedChildren.Count - 1; i >= 0; i--)
            {
                var child = initializedChildren[i];
                if (ReferenceIdentity.Contains(modifiers, child)) continue;
                if (!ReferenceEquals(child.AttachmentParent, this))
                {
                    initializedChildren.RemoveAt(i);
                    continue;
                }
                child.Destroy();
                child.SetOwner(null);
                child.SetChangeSink(null);
                child.SetAttachmentParent(null);
                initializedChildren.RemoveAt(i);
            }

            if (!IsInitialized || uniText == null) return;
            for (var i = 0; i < modifiers.Count; i++)
            {
                var child = modifiers[i];
                if (child == null || ReferenceIdentity.Contains(initializedChildren, child)) continue;
                child.SetAttachmentParent(this);
                child.SetOwner(uniText, ChangeSink);
                child.Prepare();
                initializedChildren.Add(child);
            }
        }

        private void ValidateModifiersMutation(in StateListMutation<BaseModifier> mutation)
        {
            for (var i = 0; i < mutation.Count; i++)
                if (ReferenceEquals(mutation[i], this))
                    throw new InvalidOperationException(
                        "A CompositeModifier cannot contain itself.");
            ValidateGraphs(in mutation);
        }

        private void DestroyChildren()
        {
            for (var i = 0; i < initializedChildren.Count; i++)
            {
                var child = initializedChildren[i];
                if (!ReferenceEquals(child?.AttachmentParent, this)) continue;
                child?.Destroy();
            }
            initializedChildren.Clear();
        }

        protected internal override void PrepareForParallel()
        {
            for (var i = 0; i < modifiers.Count; i++)
                modifiers[i]?.PrepareForParallel();
        }

        protected override void OnEnable()
        {
            initializedChildren.Clear();
            for (var i = 0; i < modifiers.Count; i++)
            {
                var mod = modifiers[i];
                if (mod == null) continue;
                mod.SetAttachmentParent(this);
                mod.SetOwner(uniText, ChangeSink);
                if (mod.IsInitialized) mod.Disable();
                mod.Prepare();
                initializedChildren.Add(mod);
            }
        }

        protected override void OnDisable()
        {
            for (var i = 0; i < initializedChildren.Count; i++)
            {
                var mod = initializedChildren[i];
                if (mod != null && mod.IsInitialized)
                    mod.Disable();
            }
        }

        protected override void OnDestroy()
        {
            DestroyChildren();
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var count = modifiers.Count;
            if (count == 0) return;

            for (var i = 0; i < count; i++)
            {
                var child = modifiers[i];
                if (child == null) continue;
                var childParameters = context.Parameters.Child(i);
                var childContext = context.WithParameters(in childParameters);
                child.Apply(in childContext, CurrentApplyCycle);
            }
        }

        internal override bool SignatureMatches(BaseModifier other)
        {
            if (other is not CompositeModifier o || modifiers.Count != o.modifiers.Count) return false;
            for (var i = 0; i < modifiers.Count; i++)
            {
                var a = modifiers[i];
                var b = o.modifiers[i];
                if (a == null || b == null) { if (a != b) return false; continue; }
                if (!a.SignatureMatches(b)) return false;
            }
            return true;
        }

        internal override string Signature
        {
            get
            {
                var sb = new System.Text.StringBuilder("(");
                for (var i = 0; i < modifiers.Count; i++)
                {
                    if (i > 0) sb.Append('+');
                    sb.Append(modifiers[i]?.Signature ?? "null");
                }
                return sb.Append(')').ToString();
            }
        }

        public override string ToString()
        {
            if (modifiers == null || modifiers.Count == 0) return nameof(CompositeModifier);
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i] == null) continue;
                if (sb.Length > 0) sb.Append(" + ");
                var n = modifiers[i].GetType().Name;
                sb.Append(n.EndsWith("Modifier", StringComparison.Ordinal) ? n.Substring(0, n.Length - "Modifier".Length) : n);
            }
            return sb.Length > 0 ? sb.ToString() : nameof(CompositeModifier);
        }
    }
}
