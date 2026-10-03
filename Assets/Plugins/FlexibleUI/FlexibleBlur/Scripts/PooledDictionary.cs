using System;
using System.Collections;
using System.Collections.Generic;

namespace JeffGrawAssets.FlexibleUI
{
public class PooledListDictionary<TKey, TList, TValue> : IEnumerable<KeyValuePair<TKey, TList>> where TList : IList<TValue>, new()
{
    private readonly Dictionary<TKey, TList> dictionary = new();
    private readonly Stack<TList> listPool = new();

    public IList<TValue> this[TKey key]
    {
        get
        {
            if (dictionary.TryGetValue(key, out var list))
                return list;

            return dictionary[key] = GetOrCreateList();
        }
    }

    public void Add(TKey key, TValue value)
    {
        if (!dictionary.TryGetValue(key, out var list))
            list = dictionary[key] = GetOrCreateList();

        list.Add(value);
    }

    public bool Remove(TKey key)
    {
        if (!dictionary.TryGetValue(key, out var list))
            return false;

        ReturnListToPool(list);
        return dictionary.Remove(key);
    }

    public bool TryGetValue(TKey key, out TList value) => dictionary.TryGetValue(key, out value);

    public void Clear()
    {
        foreach (var list in dictionary.Values)
            ReturnListToPool(list);

        dictionary.Clear();
    }

    private TList GetOrCreateList() => listPool.TryPop(out var result) ? result : new TList();

    private void ReturnListToPool(TList list)
    {
        list.Clear();
        listPool.Push(list);
    }

    public Enumerator GetEnumerator() => new (dictionary.GetEnumerator());

    IEnumerator<KeyValuePair<TKey, TList>> IEnumerable<KeyValuePair<TKey, TList>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TList>>
    {
        private Dictionary<TKey, TList>.Enumerator _enumerator;

        public Enumerator(Dictionary<TKey, TList>.Enumerator enumerator) => _enumerator = enumerator;

        public KeyValuePair<TKey, TList> Current => _enumerator.Current;

        object IEnumerator.Current => Current;

        public bool MoveNext() => _enumerator.MoveNext();

        public void Reset() => throw new NotSupportedException();

        public void Dispose() => _enumerator.Dispose();
    }
}
}