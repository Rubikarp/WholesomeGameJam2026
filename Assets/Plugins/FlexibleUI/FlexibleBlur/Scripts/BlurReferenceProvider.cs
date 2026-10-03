using System.Collections.Generic;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class BlurReferenceProvider : MonoBehaviour
{
#if UNITY_EDITOR
    public const string CameraReferenceFieldName = nameof(cameraReference);
    public const string FeatureNumberFieldName = nameof(featureNumber);
#endif

    public static readonly Dictionary<Canvas, (Camera camera, int featureNumber)> CameraReferenceDict = new ();

    [SerializeField] private Camera cameraReference;
    public Camera CameraReference
    {
        get => cameraReference;
        set
        {
            cameraReference = value;
            UpdateDict();
        }
    }

    [SerializeField] private int featureNumber;
    public int FeatureNumber
    {
        get => featureNumber;
        set
        {
            featureNumber = value;
            UpdateDict();
        }
    }

    private Canvas canvas;

    void OnEnable()
    {
        canvas = GetComponent<Canvas>();
        UpdateDict();
    }

#if UNITY_EDITOR
    void OnValidate() => OnEnable();
#endif
    void OnDisable()
    {
        CameraReferenceDict.Remove(canvas);
    }

    void UpdateDict()
    {
        CameraReferenceDict[canvas] = (cameraReference, featureNumber);
    }
}
}