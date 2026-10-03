using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoPhotos : MonoBehaviour
{
    public Sprite[] pictures;
    public string[] names;
    public int initialPicture;
    public Image picture;
    public Image wallpaper;
    public Text caption;
    public Text zoomLabel;
    public Text wallpaperLabel;
    public Slider zoom;
    public Image[] thumbnails;
    public RectTransform viewport;
    private int selected;
    private Vector2 pan;

    private void Start() => Select(initialPicture);
    private void OnRectTransformDimensionsChange() { if (picture && viewport) Layout(); }

    public void Select(int index)
    {
        selected = (index % pictures.Length + pictures.Length) % pictures.Length;
        picture.sprite = pictures[selected];
        caption.text = names[selected];
        for (var i = 0; i < thumbnails.Length; i++)
            thumbnails[i].color = i == selected ? Color.white : new Color(0.56f, 0.59f, 0.65f, 1f);
        Fit();
        RefreshWallpaperLabel();
    }

    public void Previous() => Select(selected - 1);
    public void Next() => Select(selected + 1);
    public void Fit() { pan = Vector2.zero; zoom.SetValueWithoutNotify(1f); SetZoom(1f); }
    public void SetZoom(float value) { zoomLabel.text = Mathf.RoundToInt(value * 100f) + "%"; Layout(); }
    public void ZoomBy(float amount) => zoom.value = Mathf.Clamp(zoom.value + amount, zoom.minValue, zoom.maxValue);
    public void Pan(Vector2 delta) { pan += delta; Layout(); }

    public void UseWallpaper()
    {
        wallpaper.sprite = pictures[selected];
        RefreshWallpaperLabel();
    }

    private void RefreshWallpaperLabel() => wallpaperLabel.text = wallpaper.sprite == pictures[selected] ? "Current wallpaper" : "Use as wallpaper";

    private void Layout()
    {
        if (!picture.sprite) return;
        var bounds = viewport.rect.size;
        var size = picture.sprite.rect.size;
        size *= Mathf.Min(bounds.x / size.x, bounds.y / size.y) * (zoom ? zoom.value : 1f);
        var excess = Vector2.Max(Vector2.zero, (size - bounds) * 0.5f);
        pan = new Vector2(Mathf.Clamp(pan.x, -excess.x, excess.x), Mathf.Clamp(pan.y, -excess.y, excess.y));
        picture.rectTransform.sizeDelta = size;
        picture.rectTransform.anchoredPosition = pan;
    }
}
}
