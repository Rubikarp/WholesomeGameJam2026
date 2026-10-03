using System.Collections.Generic;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public partial class GlassReferenceProvider : MonoBehaviour
{
#if UNITY_EDITOR
    public const string CameraReferenceFieldName = nameof(cameraReference);
    public const string FeatureNumberFieldName = nameof(featureNumber);
#endif

#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.AutoStaticsCleanup]
#endif
    public static readonly Dictionary<Canvas, (Camera camera, int featureNumber)> CameraReferenceDict = new();

    [SerializeField] private Camera cameraReference;
    public Camera CameraReference
    {
        get => cameraReference;
        set
        {
            cameraReference = value;
            UpdateDictionary();
        }
    }

    [SerializeField] private int featureNumber;
    public int FeatureNumber
    {
        get => featureNumber;
        set
        {
            featureNumber = Mathf.Max(0, value);
            UpdateDictionary();
        }
    }

    private Canvas canvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CameraReferenceDict.Clear();
#if UNITY_EDITOR
        foreach (var provider in Resources.FindObjectsOfTypeAll<GlassReferenceProvider>())
        {
            if (!provider || !provider.gameObject.scene.IsValid() || !provider.isActiveAndEnabled)
                continue;
            provider.canvas = provider.GetComponent<Canvas>();
            provider.UpdateDictionary();
        }
#endif
    }

    private void OnEnable()
    {
        canvas = GetComponent<Canvas>();
        UpdateDictionary();
    }

    private void OnDisable()
    {
        if (canvas)
            CameraReferenceDict.Remove(canvas);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        featureNumber = Mathf.Max(0, featureNumber);
        if (!canvas)
            canvas = GetComponent<Canvas>();
        UpdateDictionary();
    }
#endif

    private void UpdateDictionary()
    {
        if (canvas && isActiveAndEnabled)
            CameraReferenceDict[canvas] = (cameraReference, featureNumber);
        else if (canvas)
            CameraReferenceDict.Remove(canvas);
    }
}
}
