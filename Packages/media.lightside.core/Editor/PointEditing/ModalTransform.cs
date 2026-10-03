using System;
using System.Globalization;
using UnityEngine;

namespace LightSide
{
    /// <summary>In-plane axes a modal transform is constrained to. Flags, so <see cref="Both"/> = unconstrained.</summary>
    [Flags]
    public enum TransformAxis
    {
        None = 0,
        X = 1,
        Y = 2,
        Both = X | Y,
    }

    /// <summary>The kind of modal transform currently in progress.</summary>
    public enum TransformMode
    {
        None,
        Translate,
        Rotate,
        Scale,
    }

    /// <summary>What one <see cref="ModalTransform.HandleEvent"/> call resolved to. The caller applies
    /// <see cref="ModalTransform.Build"/> whenever <see cref="Changed"/> is set, then finalizes on
    /// <see cref="Committed"/> (record undo) or restores the pre-transform snapshot on <see cref="Cancelled"/>.</summary>
    public readonly struct ModalStep
    {
        /// <summary>The operator is still running after this event.</summary>
        public readonly bool Active;

        /// <summary>The transform changed; rebuild via <see cref="ModalTransform.Build"/> and re-apply from the snapshot.</summary>
        public readonly bool Changed;

        /// <summary>The user confirmed. Apply the final transform, then record undo and reset.</summary>
        public readonly bool Committed;

        /// <summary>The user cancelled. Restore the snapshot and reset.</summary>
        public readonly bool Cancelled;

        /// <summary>The input belongs to the active operator and must not reach another handler.</summary>
        public readonly bool Consumed;

        /// <summary>Creates the complete result of processing one modal input event.</summary>
        public ModalStep(bool active, bool changed, bool committed, bool cancelled,
            bool consumed)
        {
            Active = active;
            Changed = changed;
            Committed = committed;
            Cancelled = cancelled;
            Consumed = consumed;
        }
    }

    /// <summary>
    /// Blender-style modal transform operator — grab / scale / rotate — for a group of points in a working
    /// plane, decoupled from what the points are and how they're drawn. The host feeds pointer positions in
    /// plane space; this tracks the modal state (active mode, axis constraint, typed numeric value) from the
    /// keyboard and produces a pure <see cref="Func{T,TResult}"/> mapping an original plane point to its
    /// transformed position. Scale and rotate pivot on the centre of the supplied bounds.
    /// </summary>
    /// <remarks>
    /// Deliberately free of any <c>UnityEditor</c> dependency, so the
    /// same operator can drive a SceneView tool, a 2-D graph, a node canvas, or a runtime gizmo.
    /// <para>Key handling while active: <c>G/S/R</c> pick Translate/Scale/Rotate (pressing the active mode's key
    /// again cancels; a different key switches mode); <c>X/Y</c> toggle an axis constraint; digits, <c>.</c>,
    /// leading <c>-</c>/<c>+</c> and Backspace edit a numeric value (allowed once an axis is set, or in Rotate);
    /// <c>Return</c> or a left click commit; <c>Escape</c> cancels. A typed value overrides mouse movement.</para>
    /// </remarks>
    public sealed class ModalTransform
    {
        /// <summary>The active mode, or <see cref="TransformMode.None"/> when idle.</summary>
        public TransformMode Mode { get; private set; }

        /// <summary>The current axis constraint; <see cref="TransformAxis.Both"/> means unconstrained.</summary>
        public TransformAxis Axis { get; private set; } = TransformAxis.Both;

        /// <summary>The numeric value being typed, or empty. Interpreted per mode when non-empty.</summary>
        public string Expression { get; private set; } = string.Empty;

        /// <summary>Whether a transform is in progress.</summary>
        public bool IsActive => Mode != TransformMode.None;

        private Rect pivot;
        private Vector2 startPointer;
        private Vector2 lastPointer;

        private bool CanUseExpression => Expression.Length > 0 && CanEditExpression;
        private bool CanEditExpression => Mode == TransformMode.Rotate || Mode == TransformMode.Scale || Axis != TransformAxis.Both;

        /// <summary>
        /// Begins a transform. <paramref name="pivotBounds"/> is the selection's bounds (its centre is the
        /// scale/rotate pivot); <paramref name="startPointer"/> is the pointer position, in plane space, at the
        /// moment the operator starts.
        /// </summary>
        public void Begin(TransformMode mode, Rect pivotBounds, Vector2 startPointer)
        {
            Mode = mode;
            Axis = TransformAxis.Both;
            Expression = string.Empty;
            pivot = pivotBounds;
            this.startPointer = startPointer;
            lastPointer = startPointer;
        }

        /// <summary>Clears all modal state. Call after committing or cancelling.</summary>
        public void Reset()
        {
            Mode = TransformMode.None;
            Axis = TransformAxis.Both;
            Expression = string.Empty;
        }

        /// <summary>
        /// Advances the operator with one neutral editor event. No-op when not <see cref="IsActive"/>.
        /// </summary>
        internal ModalStep HandleEvent(PointEditEvent e, Vector2 currentPointer)
        {
            if (!IsActive) return default;

            bool pointerMoved = currentPointer != lastPointer;
            lastPointer = currentPointer;

            bool changed = false, commit = false, cancel = false, consumed = false;

            if (e.Type == PointEditEventType.KeyDown)
            {
                switch (e.KeyCode)
                {
                    case KeyCode.Escape: cancel = true; consumed = true; break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter: commit = true; consumed = true; break;
                    case KeyCode.G: changed |= SetMode(TransformMode.Translate, ref cancel); consumed = true; break;
                    case KeyCode.S: changed |= SetMode(TransformMode.Scale, ref cancel); consumed = true; break;
                    case KeyCode.R: changed |= SetMode(TransformMode.Rotate, ref cancel); consumed = true; break;
                    case KeyCode.X: ToggleAxis(TransformAxis.X); changed = true; consumed = true; break;
                    case KeyCode.Y: ToggleAxis(TransformAxis.Y); changed = true; consumed = true; break;
                    default:
                        if (TryEditExpression(e)) { changed = true; consumed = true; }
                        break;
                }
            }
            else if (e.Type == PointEditEventType.PointerDown && e.Button == 0)
            {
                commit = true;
                consumed = true;
            }
            else if (pointerMoved && Expression.Length == 0)
            {
                changed = true;
                consumed = true;
            }

            if (commit) changed = true;
            return new ModalStep(IsActive && !commit && !cancel,
                changed, commit, cancel, consumed);
        }

        /// <summary>
        /// Builds the current transform: a function from an original plane point to its transformed position.
        /// Uses the typed numeric value when one is in effect, otherwise the pointer delta from the start.
        /// </summary>
        public Func<Vector2, Vector2> Build(Vector2 currentPointer)
        {
            if (CanUseExpression && TryParse(Expression, out float value))
                return BuildNumeric(value);

            var m = BuildMatrix(currentPointer);
            return p =>
            {
                var r = m.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f));
                return new Vector2(r.x, r.y);
            };
        }

        /// <summary>Short status string for an on-screen HUD, e.g. "Scale X: 1.5". Empty when idle.</summary>
        public string Describe()
        {
            if (!IsActive) return string.Empty;
            string mode = Mode switch
            {
                TransformMode.Translate => "Move",
                TransformMode.Rotate => "Rotate",
                TransformMode.Scale => "Scale",
                _ => string.Empty,
            };
            string axis = Axis == TransformAxis.X ? " X" : Axis == TransformAxis.Y ? " Y" : string.Empty;
            string val = Expression.Length > 0 ? $": {Expression}" : string.Empty;
            return $"{mode}{axis}{val}";
        }

        private bool SetMode(TransformMode mode, ref bool cancel)
        {
            if (Mode == mode)
            {
                cancel = true;
                return false;
            }

            Mode = mode;
            Axis = TransformAxis.Both;
            Expression = string.Empty;
            return true;
        }

        private void ToggleAxis(TransformAxis a)
        {
            Axis = Axis == a ? TransformAxis.Both : a;
            Expression = string.Empty;
        }

        private bool TryEditExpression(PointEditEvent e)
        {
            if (!CanEditExpression) return false;

            if (e.KeyCode == KeyCode.Backspace)
            {
                if (Expression.Length == 0) return false;
                Expression = Expression[..^1];
                return true;
            }

            char c = e.Character;
            if (char.IsDigit(c))
            {
                Expression += c;
                return true;
            }
            if ((c == '-' || c == '+') && Expression.Length == 0)
            {
                Expression += c;
                return true;
            }
            if (c == '.' && !Expression.Contains('.'))
            {
                Expression += c;
                return true;
            }

            return false;
        }

        private Matrix4x4 BuildMatrix(Vector2 current)
        {
            Vector2 center = pivot.center;

            switch (Mode)
            {
                case TransformMode.Translate:
                {
                    Vector2 delta = current - startPointer;
                    if (Axis == TransformAxis.X) delta.y = 0f;
                    else if (Axis == TransformAxis.Y) delta.x = 0f;
                    return Matrix4x4.Translate(new Vector3(delta.x, delta.y, 0f));
                }
                case TransformMode.Rotate:
                {
                    float angle = Vector2.SignedAngle(startPointer - center, current - center);
                    return Pivoted(center, Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angle)));
                }
                case TransformMode.Scale:
                {
                    float from = Vector2.Distance(center, startPointer);
                    float factor = from > 1e-6f ? Vector2.Distance(center, current) / from : 1f;
                    factor = Mathf.Clamp(factor, 0.01f, 100f);
                    return Pivoted(center, Matrix4x4.Scale(ScaleVector(factor)));
                }
                default:
                    return Matrix4x4.identity;
            }
        }

        private Func<Vector2, Vector2> BuildNumeric(float value)
        {
            Vector2 center = pivot.center;

            switch (Mode)
            {
                case TransformMode.Translate:
                {
                    bool absolute = char.IsDigit(Expression[0]) || Expression[0] == '.';
                    return Axis == TransformAxis.Y
                        ? p => new Vector2(p.x, absolute ? value : p.y + value)
                        : p => new Vector2(absolute ? value : p.x + value, p.y);
                }
                case TransformMode.Rotate:
                {
                    var m = Pivoted(center, Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, value)));
                    return p => (Vector2)m.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f));
                }
                case TransformMode.Scale:
                {
                    var m = Pivoted(center, Matrix4x4.Scale(ScaleVector(Mathf.Clamp(value, 0.01f, 100f))));
                    return p => (Vector2)m.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f));
                }
                default:
                    return p => p;
            }
        }

        private Vector3 ScaleVector(float factor) => Axis switch
        {
            TransformAxis.X => new Vector3(factor, 1f, 1f),
            TransformAxis.Y => new Vector3(1f, factor, 1f),
            _ => new Vector3(factor, factor, 1f),
        };

        private static Matrix4x4 Pivoted(Vector2 center, Matrix4x4 m)
        {
            var c = new Vector3(center.x, center.y, 0f);
            return Matrix4x4.Translate(c) * m * Matrix4x4.Translate(-c);
        }

        private static bool TryParse(string s, out float value)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
