using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace JeffGrawAssets.FlexibleUI
{
[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
public class UIBlur : MonoBehaviour, IBlur
{
#if UNITY_EDITOR
    public static readonly string BlurCommonFieldName = nameof(_common);
#endif
    public static readonly Dictionary<(Camera camera, int featureIdx), List<IBlur>> BlurDict = new();
    public static readonly Dictionary<(Camera camera, int featureIdx), int> LayersPerCameraDict = new();

    public bool zeroCanvasAlphaActive;
    public bool ActiveAtZeroAlpha => zeroCanvasAlphaActive;

    [SerializeField] private UIBlurCommon _common = new();
    public UIBlurCommon Common => _common;
    public bool CanBatch => false;
    public bool FillEntireRenderTexture => false;

    public float Alpha { get; private set; }

    private Canvas canvas;
    private CanvasRenderer cRenderer;
    private RectTransform rectT;

    static UIBlur()
    {
        SceneManager.sceneLoaded += (_, _) => RemoveEmptyDictEntriesOnStartup();
#if UNITY_EDITOR
        EditorSceneManager.sceneOpened += (_, _) => RemoveEmptyDictEntriesOnStartup();
#endif
        void RemoveEmptyDictEntriesOnStartup()
        {
            for (int i = 0; i < BlurDict.Count; i++)
            {
                var key = BlurDict.ElementAt(i).Key;
                if (key.camera)
                    continue;

                BlurDict.Remove(key);
                i--;
            }
            for (int i = 0; i < LayersPerCameraDict.Count; i++)
            {
                var key = LayersPerCameraDict.ElementAt(i).Key;
                if (key.camera)
                    continue;

                LayersPerCameraDict.Remove(key);
                i--;
            }
        }
    }

    void OnEnable()
    {
        Common.Init(this, BlurDict, LayersPerCameraDict);

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        if (cRenderer == null)
            cRenderer = GetComponent<CanvasRenderer>();

        rectT = transform as RectTransform;

        Common.ValidateBlur();
        FlexibleBlurPass.ComputeBlurEvent += Common.ComputeBlur;
    }

    void OnDisable()
    {
        Common.RemoveFromBlurList();
        FlexibleBlurPass.ComputeBlurEvent -= Common.ComputeBlur;
    }

    void OnTransformParentChanged()
    {
        canvas = GetComponentInParent<Canvas>();
        cRenderer = GetComponent<CanvasRenderer>();
    }

    void Update() => CalculateBlur();

    void OnValidate() => Common.ValidateBlur();

    private void CalculateBlur()
    {
        Alpha = cRenderer.GetInheritedAlpha() * Common.blurStrength;
        if (!canvas || !canvas.isActiveAndEnabled || Alpha == 0 && !zeroCanvasAlphaActive)
        {
            if (Common.PresentInBlurList)
                Common.RemoveFromBlurList();

            return;
        }

        Common.CacheBlur(canvas, rectT, Vector2.zero, 0f, false, Vector2.zero, useFilterPadding: false);
    }
}
}
