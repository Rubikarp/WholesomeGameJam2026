using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoDesktop : MonoBehaviour
{
    public Camera backgroundCamera;
    [Tooltip("Always renders after the app windows. Its Flexible Glass Camera Override controls only the Dock backdrop.")]
    public Camera dockCamera;
    [Tooltip("Shared window-layer cameras, ordered back to front. Assigned by window depth, not by app. Unused cameras stay disabled.")]
    public Camera[] windowCameras;
    [Min(1), Tooltip("Maximum window blur layers, excluding the Dock. 1 shares one backdrop across all windows; higher values let lower windows blur into those above. Windows beyond the limit share the last camera.")]
    public int maxStackedBlurs = 2;
    public GlassDemoWindow[] windows;
    public UIGlass dock;
    public Text activeApp;
    public GlassDemoDock dockController;
    public GlassDemoMenuBar menuBar;
    public GlassDemoWindow ActiveWindow { get; private set; }
    public bool IsFullScreen => ActiveWindow && ActiveWindow.IsFullScreen && ActiveWindow.IsOpen;
    private readonly List<GlassDemoWindow> order = new();
    private bool desktopFocused;
    private int activeBlurLimit;
    private int WindowLayerLimit => Mathf.Min(Mathf.Max(1, maxStackedBlurs), windowCameras?.Length ?? 0);

    private void Start()
    {
        order.Clear();
        foreach (var window in windows)
        {
            window.Initialize(window.startOpen);
            order.Add(window);
        }
        RefreshStack();
    }

    private void Update()
    {
        if (activeBlurLimit != WindowLayerLimit) RefreshStack();
    }

    public void Focus(GlassDemoWindow window)
    {
        desktopFocused = false;
        if (ActiveWindow && ActiveWindow != window && ActiveWindow.IsFullScreen)
            ActiveWindow.ExitFullScreen();
        ActiveWindow = window;
        order.Remove(window);
        order.Add(window);
        RefreshStack();
    }

    public void SetMaxStackedBlurs(int value)
    {
        value = Mathf.Max(1, value);
        if (maxStackedBlurs == value) return;
        maxStackedBlurs = value;
        RefreshStack();
    }

    public void RefreshStack()
    {
        if (!backgroundCamera || windows == null || WindowLayerLimit == 0) return;
        if (order.Count == 0) order.AddRange(windows);
        if (desktopFocused) ActiveWindow = null;
        else if (!ActiveWindow || !ActiveWindow.IsRunning)
        {
            ActiveWindow = null;
            for (var i = order.Count - 1; i >= 0; i--)
                if (Application.isPlaying ? order[i].IsOpen : order[i].startOpen) { ActiveWindow = order[i]; break; }
        }
        var stack = backgroundCamera.GetUniversalAdditionalCameraData().cameraStack;
        stack.Clear();
        activeBlurLimit = WindowLayerLimit;
        foreach (var camera in windowCameras)
        {
            camera.enabled = false;
            camera.cullingMask = 0;
        }
        var index = 0;
        var title = "Desktop";
        foreach (var window in order)
        {
            var visible = (Application.isPlaying ? window.IsOpen : window.startOpen) && (!IsFullScreen || window == ActiveWindow);
            window.SetFocused(window == ActiveWindow);
            if (!visible) window.ReleaseInputFocus();
            window.windowCanvas.enabled = visible;
            if (!visible) continue;
            var slot = Mathf.Min(index, activeBlurLimit - 1);
            var camera = windowCameras[slot];
            if (!camera.enabled)
            {
                camera.enabled = true;
                camera.depth = 10 + slot;
                stack.Add(camera);
            }
            camera.cullingMask |= 1 << window.gameObject.layer;
            window.windowCanvas.worldCamera = camera;
            window.windowCanvas.sortingOrder = 10 + index;
            window.SetBackdrop(slot == 0 ? backgroundCamera : windowCameras[slot - 1]);
            index++;
        }
        if (dockCamera)
        {
            dockCamera.enabled = true;
            dockCamera.depth = 10 + index;
            stack.Add(dockCamera);
            if (dock) dock.cameraReference = dockCamera;
        }
        if (activeApp) activeApp.text = ActiveWindow ? ActiveWindow.appName : title;
        if (menuBar) menuBar.RefreshAppMenus();
        if (dockController) dockController.RefreshIndicators();
    }

    public void Arrange()
    {
        foreach (var window in windows)
        {
            if (!window.IsOpen) continue;
            window.RestoreLayout();
            window.Open();
        }
    }

    public void CloseAll()
    {
        foreach (var window in windows)
            if (window.IsOpen) window.Close();
    }

    public void QuitAll()
    {
        foreach (var window in windows) window.Quit();
    }

    public void ShowDesktop()
    {
        desktopFocused = true;
        foreach (var window in windows) if (window.IsOpen) window.Minimize();
        ActiveWindow = null;
        RefreshStack();
    }

    public GlassDemoWindow FindApp(string appName)
    {
        foreach (var window in windows) if (window.appName == appName) return window;
        return null;
    }

    public void OpenSettings() => FindApp("Settings")?.Open();

    public void OpenWallpaper()
    {
        var settings = FindApp("Settings");
        settings.Open();
        settings.GetComponent<GlassDemoSettings>().SelectPage(1);
    }

    internal static void SetSource(GlassImage image, Camera source)
    {
        if (!image || image.cameraReference == source) return;
        var enabled = image.enabled;
        image.enabled = false;
        image.cameraReference = source;
        image.enabled = enabled;
        image.SetAllDirty();
    }
}
}
