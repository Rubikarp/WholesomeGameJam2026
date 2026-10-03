using System.Collections.Generic;
using System.Linq;
using System;

public static class Extension_Collection
{
    public static bool Contain<T>(this T[] array, T toFind)
    {
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i].Equals(toFind)) { return true; }
        }
        return false;
    }
    public static void Swap<T>(this IList<T> list, int indexA, int indexB)
    {
        (list[indexA], list[indexB]) = (list[indexB], list[indexA]);
    }

    public static T Last<T>(this T[] array) { return array[^1]; }
    public static T Last<T>(this IList<T> list) { return list[^1]; }

    public static T FromEnd<T>(this T[] array, int nbr) { return array[^nbr]; }
    public static T FromEnd<T>(this IList<T> list, int nbr) { return list[^nbr]; }

    public static T Random<T>(this T[] array) { return array[UnityEngine.Random.Range(0, array.Length)]; }
    public static T Random<T>(this IList<T> list) { return list[UnityEngine.Random.Range(0, list.Count)]; }

    /// <summary>
    /// Shuffles the elements in the list using the Durstenfeld implementation of the Fisher-Yates algorithm.
    /// This method modifies the input list in-place, ensuring each permutation is equally likely, and returns the list for method chaining.
    /// Reference: http://en.wikipedia.org/wiki/Fisher-Yates_shuffle
    /// </summary>
    /// <param name="list">The list to be shuffled.</param>
    /// <typeparam name="T">The type of the elements in the list.</typeparam>
    /// <returns>The shuffled list.</returns>
    public static IList<T> Shuffle<T>(this IList<T> list)
    {
        int count = list.Count;
        while (count > 1)
        {
            --count;
            //UnityEngine.Random
            int index = UnityEngine.Random.Range(0, count + 1);
            (list[index], list[count]) = (list[count], list[index]);
        }
        return list;
    }
    public static T[] Shuffle<T>(this T[] array) => array.ToList().Shuffle().ToArray();

    public static bool ContainsKeyOfType<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, Type typeToCheck)
    {
        foreach (KeyValuePair<TKey, TValue> KVPair in dictionary)
        {
            if (typeToCheck.IsInstanceOfType(KVPair.Key))
                return true;
        }

        return false;
    }
}
