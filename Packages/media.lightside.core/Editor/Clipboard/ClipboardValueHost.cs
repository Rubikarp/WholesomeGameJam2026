using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Non-generic root the clipboard discovers hosts through. Do not derive from this directly —
    /// derive from <see cref="ClipboardValueHost{T}"/>.
    /// </summary>
    public abstract class ClipboardValueHostBase : ScriptableObject
    {
        internal abstract void Load(object[] source, bool collection);
        internal abstract object[] Store(bool collection);
    }

    /// <summary>
    /// Editing surface for clipboard values of type <typeparamref name="T"/>. A managed reference
    /// cannot carry a value type, so a value type becomes editable in the clipboard through one
    /// concrete subclass — <c>sealed class PaintSwatchClipboardHost :
    /// ClipboardValueHost&lt;PaintSwatch&gt; { }</c> — discovered automatically. Values without a
    /// host still show, read-only.
    /// </summary>
    public abstract class ClipboardValueHost<T> : ClipboardValueHostBase
    {
        [SerializeField] internal T value;
        [SerializeField] internal List<T> values = new();

        internal override void Load(object[] source, bool collection)
        {
            if (!collection)
            {
                value = (T)PropertyClipboard.CloneObject(source[0]);
                return;
            }
            values.Capacity = source.Length;
            for (var i = 0; i < source.Length; i++)
                values.Add((T)PropertyClipboard.CloneObject(source[i]));
        }

        internal override object[] Store(bool collection)
        {
            if (!collection) return new object[] { value };
            var result = new object[values.Count];
            for (var i = 0; i < values.Count; i++) result[i] = values[i];
            return result;
        }
    }

    /// <summary>Resolves the concrete host editing a value type, walking base types for Unity objects.</summary>
    internal static class ClipboardValueHosts
    {
        private static Dictionary<Type, Type> hostTypes;

        internal static Type Find(Type valueType)
        {
            hostTypes ??= Discover();
            for (var current = valueType; current != null; current = current.BaseType)
                if (hostTypes.TryGetValue(current, out var host))
                    return host;
            return null;
        }

        private static Dictionary<Type, Type> Discover()
        {
            var result = new Dictionary<Type, Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<ClipboardValueHostBase>())
            {
                if (type.IsAbstract) continue;
                for (var current = type.BaseType; current != null; current = current.BaseType)
                    if (current.IsGenericType &&
                        current.GetGenericTypeDefinition() == typeof(ClipboardValueHost<>))
                    {
                        result[current.GetGenericArguments()[0]] = type;
                        break;
                    }
            }
            return result;
        }
    }

    internal sealed class ColorClipboardHost : ClipboardValueHost<Color> { }
    internal sealed class Color32ClipboardHost : ClipboardValueHost<Color32> { }
    internal sealed class GradientClipboardHost : ClipboardValueHost<Gradient> { }
    internal sealed class GradientStopClipboardHost : ClipboardValueHost<GradientStop> { }
    internal sealed class CurveClipboardHost : ClipboardValueHost<AnimationCurve> { }
    internal sealed class StringClipboardHost : ClipboardValueHost<string> { }
    internal sealed class IntClipboardHost : ClipboardValueHost<int> { }
    internal sealed class FloatClipboardHost : ClipboardValueHost<float> { }
    internal sealed class BoolClipboardHost : ClipboardValueHost<bool> { }
    internal sealed class Vector2ClipboardHost : ClipboardValueHost<Vector2> { }
    internal sealed class Vector3ClipboardHost : ClipboardValueHost<Vector3> { }
    internal sealed class Vector4ClipboardHost : ClipboardValueHost<Vector4> { }
    internal sealed class RectClipboardHost : ClipboardValueHost<Rect> { }
    internal sealed class PaintClipboardHost : ClipboardValueHost<Paint> { }
    internal sealed class PaintSourceClipboardHost : ClipboardValueHost<PaintSource> { }
    internal sealed class PaintProjectionClipboardHost : ClipboardValueHost<PaintProjection> { }
    internal sealed class ObjectClipboardHost : ClipboardValueHost<UnityEngine.Object> { }
}
