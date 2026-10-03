using System;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoShapeMotion : MonoBehaviour
{
    [Serializable]
    public class MovingShape
    {
        public UIGlass glass;
        public Vector2 center;
        public Vector2 travel;
        public Vector2 frequency = Vector2.one;
        public float phase;
        public float tilt;
        [NonSerialized] public Vector2 dragOffset;
        [NonSerialized] public float elapsed;
        [NonSerialized] public bool dragging;
    }

    public MovingShape[] shapes;
    public RectTransform stage;
    public RectTransform backdropStage;
    public RectTransform[] coloredTiles;
    public GameObject cutoutHandle;
    public FlexibleGlassCameraOverride cameraSettings;
    public Text playbackLabel;
    public Text cutoutsLabel;
    public Text blurLabel;
    public Text speedLabel;
    public Text motionHint;
    public Slider speedSlider;
    public bool playing = true;
    public bool cutouts = true;
    [Range(0.2f, 2f)] public float speed = 1f;

    private RectTransform canvasRect;
    private RectTransform backdropCanvasRect;
    private GlassDemoBackdropMotion backdropMotion;
    private Rect lastBackdropBounds;

    private void Awake()
    {
        CacheCanvases();
        UpdateLabels();
    }

    private void Start()
    {
        Canvas.ForceUpdateCanvases();
        UpdateLayout();
        ResetBackdrop();
        ApplyShapes();
    }

    private void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Space)) TogglePlayback();
#elif ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current?.spaceKey.wasPressedThisFrame == true) TogglePlayback();
#endif
        if (!canvasRect || !backdropCanvasRect) CacheCanvases();
        UpdateLayout();
        if (backdropMotion == null) ResetBackdrop();
        var bounds = BackdropBounds();
        if (playing)
        {
            var deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.1f) * speed;
            foreach (var shape in shapes)
                if (!shape.dragging) shape.elapsed += deltaTime;
            ApplyShapes();
            backdropMotion.Step(deltaTime, bounds);
        }
        else if (bounds != lastBackdropBounds) backdropMotion.Step(0f, bounds);
        lastBackdropBounds = bounds;
    }

    public void TogglePlayback()
    {
        playing = !playing;
        UpdateLabels();
    }

    public void BeginShapeDrag(int index) => shapes[index].dragging = true;

    public void EndShapeDrag(int index) => shapes[index].dragging = false;

    public void ResetMotion()
    {
        CacheCanvases();
        Canvas.ForceUpdateCanvases();
        UpdateLayout();
        foreach (var shape in shapes)
        {
            shape.dragOffset = Vector2.zero;
            shape.elapsed = 0f;
        }
        speed = 1f;
        if (speedSlider) speedSlider.SetValueWithoutNotify(speed);
        playing = true;
        ResetBackdrop();
        ApplyShapes();
        UpdateLabels();
    }

    public void SetSpeed(float value)
    {
        speed = value;
        UpdateLabels();
    }

    public void ToggleCutouts()
    {
        cutouts = !cutouts;
        foreach (var shape in shapes)
            if (shape.glass.operation == GlassSdfOperation.Subtract) shape.glass.gameObject.SetActive(cutouts);
        if (cutoutHandle) cutoutHandle.SetActive(cutouts);
        UpdateLabels();
    }

    public void ToggleBlur()
    {
        cameraSettings.Iterations = cameraSettings.Iterations == 0 ? 2 : 0;
        UpdateLabels();
    }

    public void MoveShape(int index, Vector2 delta)
    {
        shapes[index].dragOffset += delta;
        ApplyShapes();
    }

    public Vector2 ClampToScreen(Vector2 position)
    {
        var lower = stage.InverseTransformPoint(canvasRect.TransformPoint(canvasRect.rect.min + Vector2.one * 20f));
        var upper = stage.InverseTransformPoint(canvasRect.TransformPoint(canvasRect.rect.max - Vector2.one * 20f));
        return new Vector2(Mathf.Clamp(position.x, lower.x, upper.x), Mathf.Clamp(position.y, lower.y, upper.y));
    }

    private void UpdateLabels()
    {
        if (playbackLabel) playbackLabel.text = playing ? "Pause" : "Play";
        if (motionHint) motionHint.text = playing ? "Drag to arrange  /  Space to pause" : "Drag to arrange  /  Space to resume";
        if (cutoutsLabel) cutoutsLabel.text = cutouts ? "Cutout: on" : "Cutout: off";
        if (blurLabel) blurLabel.text = cameraSettings.Iterations > 0 ? "Blur: on" : "Blur: off";
        if (speedLabel) speedLabel.text = speed.ToString("0.0") + "x";
    }

    private void CacheCanvases()
    {
        canvasRect = (RectTransform)stage.GetComponentInParent<Canvas>().transform;
        backdropCanvasRect = (RectTransform)backdropStage.GetComponentInParent<Canvas>().transform;
    }

    private void UpdateLayout()
    {
        var scale = Mathf.Min(1f, Mathf.Max(0.3f, (canvasRect.rect.width - 80f) / 1580f));
        stage.localScale = Vector3.one * scale;
        backdropStage.localScale = stage.localScale;
    }

    private Rect BackdropBounds()
    {
        var lower = backdropStage.InverseTransformPoint(backdropCanvasRect.TransformPoint(backdropCanvasRect.rect.min));
        var upper = backdropStage.InverseTransformPoint(backdropCanvasRect.TransformPoint(backdropCanvasRect.rect.max));
        return Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
    }

    private void ResetBackdrop()
    {
        backdropMotion ??= new GlassDemoBackdropMotion(coloredTiles);
        lastBackdropBounds = BackdropBounds();
        backdropMotion.Reset(lastBackdropBounds);
    }

    private void ApplyShapes()
    {
        foreach (var shape in shapes)
        {
            var rect = (RectTransform)shape.glass.transform;
            var t = shape.elapsed * 0.24f;
            var motion = new Vector2(Mathf.Sin(t * shape.frequency.x + shape.phase), Mathf.Sin(t * shape.frequency.y + shape.phase * 1.7f));
            rect.anchoredPosition = shape.center + Vector2.Scale(shape.travel, motion) + shape.dragOffset;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.7f + shape.phase) * shape.tilt);
        }
    }
}
}
