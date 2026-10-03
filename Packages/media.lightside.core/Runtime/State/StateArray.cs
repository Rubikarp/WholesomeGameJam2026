using System;
using System.ComponentModel;

namespace LightSide
{
    /// <summary>Creates detached array replacements for generated structural mutation kernels.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class StateArray
    {
        /// <summary>Returns a copy with one item appended.</summary>
        public static T[] Add<T>(T[] source, T item) => Insert(source, source?.Length ?? 0, item);

        /// <summary>Returns a copy with one item inserted at the requested index.</summary>
        public static T[] Insert<T>(T[] source, int index, T item)
        {
            source ??= Array.Empty<T>();
            if ((uint)index > (uint)source.Length) throw new ArgumentOutOfRangeException(nameof(index));
            var result = new T[source.Length + 1];
            if (index != 0) Array.Copy(source, 0, result, 0, index);
            result[index] = item;
            if (index != source.Length) Array.Copy(source, index, result, index + 1, source.Length - index);
            return result;
        }

        /// <summary>Returns a copy with the requested item replaced.</summary>
        public static T[] Replace<T>(T[] source, int index, T item)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if ((uint)index >= (uint)source.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (Same(source[index], item)) return source;
            var result = (T[])source.Clone();
            result[index] = item;
            return result;
        }

        /// <summary>Returns a copy without the item at the requested index.</summary>
        public static T[] RemoveAt<T>(T[] source, int index)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if ((uint)index >= (uint)source.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (source.Length == 1) return Array.Empty<T>();
            var result = new T[source.Length - 1];
            if (index != 0) Array.Copy(source, 0, result, 0, index);
            if (index != result.Length) Array.Copy(source, index + 1, result, index, result.Length - index);
            return result;
        }

        /// <summary>Returns a copy with one item moved to the requested destination index.</summary>
        public static T[] Move<T>(T[] source, int fromIndex, int toIndex)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if ((uint)fromIndex >= (uint)source.Length) throw new ArgumentOutOfRangeException(nameof(fromIndex));
            if ((uint)toIndex >= (uint)source.Length) throw new ArgumentOutOfRangeException(nameof(toIndex));
            if (fromIndex == toIndex) return source;
            var result = (T[])source.Clone();
            var item = result[fromIndex];
            if (fromIndex < toIndex) Array.Copy(result, fromIndex + 1, result, fromIndex, toIndex - fromIndex);
            else Array.Copy(result, toIndex, result, toIndex + 1, fromIndex - toIndex);
            result[toIndex] = item;
            return result;
        }

        internal static bool Same<T>(T left, T right) => typeof(T).IsValueType || typeof(T) == typeof(string)
            ? ValueEquality<T>.Same(left, right)
            : ReferenceEquals(left, right);
    }
}
