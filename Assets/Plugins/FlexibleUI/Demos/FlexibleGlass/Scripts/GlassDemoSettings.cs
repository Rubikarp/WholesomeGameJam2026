using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoSettings : MonoBehaviour
{
    public GlassDemoDesktop desktop;
    public Toggle autoHide;
    public Slider dockSize;
    public Slider blur;
    public Text blurValue;
    public Text sizeValue;
    public Slider stackedBlurs;
    public Text stackedBlursValue;
    public Image wallpaper;
    public Sprite[] wallpapers;
    public GameObject[] pages;
    public Button[] tabs;
    private FlexibleGlassCameraOverride glassSettings;
    private void Awake()
    {
        glassSettings = desktop.dockCamera.GetComponent<FlexibleGlassCameraOverride>();
        stackedBlurs.minValue = 1;
        stackedBlurs.maxValue = Mathf.Max(1, desktop.windowCameras?.Length ?? 0);
        stackedBlurs.wholeNumbers = true;
        RefreshStackedBlurs();
    }

    private void Update()
    {
        var dock = desktop.dockController;
        autoHide.SetIsOnWithoutNotify(dock.autoHide);
        if (dockSize.value != dock.size)
        {
            dockSize.SetValueWithoutNotify(dock.size);
            sizeValue.text = Mathf.RoundToInt(dock.size * 100f) + "%";
        }
        if (blur.value != glassSettings.Iterations)
        {
            blur.SetValueWithoutNotify(glassSettings.Iterations);
            blurValue.text = glassSettings.Iterations == 0 ? "Off" : glassSettings.Iterations.ToString();
        }
        if (stackedBlurs.value != Mathf.Clamp(desktop.maxStackedBlurs, stackedBlurs.minValue, stackedBlurs.maxValue))
            RefreshStackedBlurs();
    }
    private void RefreshStackedBlurs()
    {
        stackedBlurs.SetValueWithoutNotify(desktop.maxStackedBlurs);
        stackedBlursValue.text = Mathf.RoundToInt(stackedBlurs.value).ToString();
    }
    public void SetMaxStackedBlurs(float value)
    {
        desktop.SetMaxStackedBlurs(Mathf.RoundToInt(value));
        RefreshStackedBlurs();
    }
    public void SetSize(float value) { desktop.dockController.SetSize(value); sizeValue.text = Mathf.RoundToInt(value * 100f) + "%"; }
    public void SetBlur(float value) { glassSettings.Iterations = Mathf.RoundToInt(value); blurValue.text = value < 0.5f ? "Off" : Mathf.RoundToInt(value).ToString(); }
    public void SetWallpaper(int index) => wallpaper.sprite = wallpapers[index];
    public void SelectPage(int index)
    {
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].SetActive(i == index);
            GlassDemoWindow.SetGlassSelection(tabs[i], i == index, new Color(0.12f, 0.44f, 0.85f));
        }
    }
}
}
