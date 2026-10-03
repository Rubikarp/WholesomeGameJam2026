using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable]
public class QuadDataContainer : ISerializationCallbackReceiver, IEnumerable<QuadData>
{
#if UNITY_EDITOR
    public static readonly string QuadDataListFieldName = nameof(_quadDataList);
    // Only used for previewing in the editor, and specifically used for undo.
    public int editorSelectedQuadIdx;
#endif

    [SerializeField] private List<QuadData> _quadDataList;

    public QuadData this[int i] => _quadDataList[i];

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public IEnumerator<QuadData> GetEnumerator() => _quadDataList.GetEnumerator();

    public QuadData GetQuadData(int i) => _quadDataList[i];
    public QuadData GetQuadData(string name) => _quadDataList.Find(q => q.name == name);
    public int Count => _quadDataList.Count;

    public QuadData AddQuadData()
    {
        var newQuadData = new QuadData(this, $"Quad{Count}");
        _quadDataList.Add(newQuadData);
        MessageVerticesDirty();
        return newQuadData;
    }

    public int IndexOf(QuadData quadData) => _quadDataList.IndexOf(quadData);
    public void RemoveQuadData(QuadData quadData) => RemoveQuadData(_quadDataList.IndexOf(quadData));
    public void RemoveQuadData(int idx)
    {
        if (idx < 0)
            return;

        _quadDataList.RemoveAt(idx);
        if (idx <= primaryQuadIdx)
        {
            primaryQuadIdx = Mathf.Max(0, primaryQuadIdx - 1);
            RaycastAreaDirtyEvent?.Invoke((FlexibleImage.AdvancedRaycastOptions)(-1));
        }
        VerticesDirtyEvent?.Invoke();
    }

    public int primaryQuadIdx;
    public QuadData PrimaryQuadData
    {
        get
        {
            if (primaryQuadIdx < 0 || primaryQuadIdx >= _quadDataList.Count)
                primaryQuadIdx = 0;

            return _quadDataList[primaryQuadIdx];
        }
    }

    public event Action VerticesDirtyEvent;
    public event Action<FlexibleImage.AdvancedRaycastOptions> RaycastAreaDirtyEvent;

    public void MessageVerticesDirty()
    {
        VerticesDirtyEvent?.Invoke();
    }

    public void MessageRaycastAreaDirty(QuadData callingData, FlexibleImage.AdvancedRaycastOptions flags)
    {
        if (callingData == PrimaryQuadData)
            RaycastAreaDirtyEvent?.Invoke(flags);
    }

    public QuadDataContainer() => _quadDataList = new List<QuadData>{new(this, "Quad0")};

    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize()
    {
        foreach (var quadData in _quadDataList)
            quadData.container = this;
    }
}
}
