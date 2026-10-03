using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public enum GlassDesktopMenu { System, App, File, Edit, View, Window, Help, Control }

[Serializable]
public sealed class GlassDemoMenuRow
{
    public Button button;
    public Text label;
    public Text shortcut;
}

public sealed class GlassDemoMenuBar : MonoBehaviour
{
    public GlassDemoDesktop desktop;
    public RectTransform bar;
    public GameObject viewMenu;
    public GameObject fileMenu;
    public GameObject editMenu;
    public GameObject windowMenu;
    public Image wallpaper;
    public RectTransform popup;
    public GameObject dismiss;
    public GlassDemoMenuRow[] rows;
    public GameObject searchPanel;
    public InputField searchInput;
    public Button[] searchResults;
    public GameObject helpPanel;
    public GameObject calendarPanel;
    public Text calendarTitle;
    public Text[] calendarDays;
    public GameObject controlPanel;
    public Toggle dockAutoHide;
    public Slider dockBlur;
    public Text dockBlurValue;
    public bool DockMenuOpen { get; private set; }
    public bool HasPopup => dismiss && dismiss.activeSelf;
    private RectTransform currentTrigger;
    private InputField lastInput;
    private int rowCount;
    private DateTime calendarMonth;
    private readonly Vector3[] corners = new Vector3[4];
    private Sprite displayedWallpaper;
    private Text[] barText;
    private Image[] barImages;
    private FlexibleGlassCameraOverride dockSettings;
    private int displayedDockBlur = -1;

    private void Awake()
    {
        barText = bar.GetComponentsInChildren<Text>(true);
        barImages = bar.GetComponentsInChildren<Image>(true);
        dockSettings = desktop.dockCamera.GetComponent<FlexibleGlassCameraOverride>();
    }

    private void Update()
    {
        if (controlPanel && controlPanel.activeSelf)
        {
            dockAutoHide.SetIsOnWithoutNotify(desktop.dockController.autoHide);
            dockBlur.SetValueWithoutNotify(dockSettings.Iterations);
            if (displayedDockBlur != dockSettings.Iterations)
            {
                displayedDockBlur = dockSettings.Iterations;
                dockBlurValue.text = displayedDockBlur == 0 ? "Off" : displayedDockBlur.ToString();
            }
        }
        if (wallpaper && wallpaper.sprite != displayedWallpaper)
        {
            displayedWallpaper = wallpaper.sprite;
            var foreground = displayedWallpaper && displayedWallpaper.name == "Glass Interface Landscape"
                ? new Color(0.12f, 0.14f, 0.18f) : Color.white;
            foreach (var text in barText) text.color = foreground;
            foreach (var icon in barImages) if (!icon.TryGetComponent<Button>(out _)) icon.color = foreground;
        }
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (!HasPopup && selected && selected.TryGetComponent<InputField>(out var input)) lastInput = input;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        var escape = keyboard?.escapeKey.wasPressedThisFrame == true;
        var pointerY = UnityEngine.InputSystem.Pointer.current?.position.ReadValue().y ?? float.NegativeInfinity;
#else
        var escape = Input.GetKeyDown(KeyCode.Escape);
        var pointerY = Input.mousePosition.y;
#endif
        if (escape)
        {
            if (HasPopup) Close();
            else if (desktop.IsFullScreen) desktop.ActiveWindow.ToggleFullScreen();
        }
        var reveal = !desktop.IsFullScreen || HasPopup || pointerY > Screen.height - 36f;
        var position = bar.anchoredPosition;
        var y = Mathf.MoveTowards(position.y, reveal ? -14f : 18f, Time.unscaledDeltaTime * 280f);
        if (y != position.y) bar.anchoredPosition = new Vector2(position.x, y);
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var modifier = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed);
        if (!modifier) return;
        var quit = keyboard.qKey.wasPressedThisFrame;
        var close = keyboard.wKey.wasPressedThisFrame;
        var minimize = keyboard.mKey.wasPressedThisFrame || keyboard.hKey.wasPressedThisFrame;
        var create = keyboard.nKey.wasPressedThisFrame;
        var search = keyboard.spaceKey.wasPressedThisFrame;
#else
        var modifier = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        if (!modifier) return;
        var quit = Input.GetKeyDown(KeyCode.Q);
        var close = Input.GetKeyDown(KeyCode.W);
        var minimize = Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.H);
        var create = Input.GetKeyDown(KeyCode.N);
        var search = Input.GetKeyDown(KeyCode.Space);
#endif
        if (quit && desktop.ActiveWindow) desktop.ActiveWindow.Quit();
        if (close && desktop.ActiveWindow) desktop.ActiveWindow.Close();
        if (minimize && desktop.ActiveWindow) desktop.ActiveWindow.Minimize();
        if (create && desktop.ActiveWindow)
        {
            if (desktop.ActiveWindow.TryGetComponent<GlassDemoNotepad>(out var notes)) notes.AddNote();
            else if (desktop.ActiveWindow.TryGetComponent<GlassDemoReminders>(out var reminders)) reminders.FocusNew();
        }
        if (search) ShowSearch();
    }

    public void RefreshAppMenus()
    {
        var app = desktop.ActiveWindow;
        if (fileMenu) fileMenu.SetActive(app);
        if (editMenu) editMenu.SetActive(app);
        if (viewMenu) viewMenu.SetActive(app && app.canMaximize);
        var running = false;
        foreach (var window in desktop.windows) running |= Application.isPlaying ? window.IsRunning : window.startOpen;
        if (windowMenu) windowMenu.SetActive(running);
    }

    public void ShowMenu(GlassDesktopMenu menu, RectTransform trigger, bool hover = false)
    {
        if (hover && !HasPopup) return;
        if (!hover && currentTrigger == trigger && (popup.gameObject.activeSelf || controlPanel.activeSelf)) { Close(); return; }
        Close();
        currentTrigger = trigger;
        if (menu == GlassDesktopMenu.Control) { ShowControls(); return; }
        dismiss.SetActive(true);
        popup.gameObject.SetActive(true);
        rowCount = 0;
        foreach (var row in rows) row.button.gameObject.SetActive(false);
        var app = desktop.ActiveWindow;
        var open = app && app.IsOpen;
        switch (menu)
        {
            case GlassDesktopMenu.System:
                Add("Desktop Help", ShowHelp);
                Add("System Settings…", desktop.OpenSettings);
                Add("Change Wallpaper…", desktop.OpenWallpaper);
                break;
            case GlassDesktopMenu.App:
                if (!app) Add("Desktop & Dock Settings…", desktop.OpenSettings);
                else
                {
                    if (!open) Add("Show " + app.appName, app.Open);
                    Add("Hide " + app.appName, open ? app.Minimize : null, "Ctrl+H");
                    Add("Quit " + app.appName, app.IsRunning ? app.Quit : null, "Ctrl+Q");
                }
                break;
            case GlassDesktopMenu.File:
                if (app && app.TryGetComponent<GlassDemoNotepad>(out var notes)) Add("New Note", notes.AddNote, "Ctrl+N");
                else if (app && app.TryGetComponent<GlassDemoReminders>(out var reminders)) Add("New Reminder", reminders.FocusNew, "Ctrl+N");
                else if (app && !open) Add("Open Window", app.Open);
                if (app && app.TryGetComponent<GlassDemoPhotos>(out var photos)) Add("Use as Wallpaper", photos.UseWallpaper);
                Add("Close App", open ? app.Close : null, "Ctrl+W");
                break;
            case GlassDesktopMenu.Edit:
                if (app && app.TryGetComponent<GlassDemoCalculator>(out var calculator))
                {
                    AddCalculatorActions(calculator);
                    break;
                }
                var canEdit = lastInput && app && lastInput.transform.IsChildOf(app.transform) && open;
                Add("Cut", canEdit ? () => EditSelection(0) : null, "Ctrl+X");
                Add("Copy", canEdit ? () => EditSelection(1) : null, "Ctrl+C");
                Add("Paste", canEdit ? () => EditSelection(2) : null, "Ctrl+V");
                Add("Select All", canEdit ? () => EditSelection(3) : null, "Ctrl+A");
                break;
            case GlassDesktopMenu.View:
                if (app && app.TryGetComponent<GlassDemoPhotos>(out var photoView))
                {
                    Add("Previous Photo", photoView.Previous);
                    Add("Next Photo", photoView.Next);
                    Add("Zoom In", () => photoView.ZoomBy(0.25f));
                    Add("Zoom Out", () => photoView.ZoomBy(-0.25f));
                    Add("Fit to Window", photoView.Fit);
                }
                else if (app && app.TryGetComponent<GlassDemoReminders>(out var reminderView))
                {
                    Add("All Reminders", () => reminderView.SelectFilter(0));
                    Add("Upcoming", () => reminderView.SelectFilter(1));
                    Add("Completed", () => reminderView.SelectFilter(2));
                }
                else if (app && app.TryGetComponent<GlassDemoSettings>(out var settingsView))
                {
                    Add("Desktop & Dock", () => settingsView.SelectPage(0));
                    Add("Wallpaper", () => settingsView.SelectPage(1));
                }
                Add(desktop.IsFullScreen ? "Exit Full Screen" : "Enter Full Screen", open && app.canMaximize ? app.ToggleFullScreen : null);
                break;
            case GlassDesktopMenu.Window:
                Add("Minimize", open ? app.Minimize : null, "Ctrl+M");
                Add(app && app.IsMaximized ? "Restore Size" : "Fill", open && app.canMaximize ? app.ToggleMaximize : null);
                Add("Arrange Open Windows", desktop.Arrange);
                Add("Show Desktop", desktop.ShowDesktop);
                foreach (var window in desktop.windows)
                    if (window.IsRunning) Add((window == app ? "✓  " : "") + window.appName, window.Open);
                break;
            case GlassDesktopMenu.Help:
                Add("Desktop Help", ShowHelp);
                break;
        }
        PositionPopup(trigger, false);
    }

    public void ShowDockMenu(GlassDemoDockIcon icon)
    {
        Close();
        DockMenuOpen = true;
        dismiss.SetActive(true);
        popup.gameObject.SetActive(true);
        rowCount = 0;
        foreach (var row in rows) row.button.gameObject.SetActive(false);
        var app = icon.window;
        Add(app.IsOpen ? "Show " + app.appName : "Open " + app.appName, app.Open);
        Add("Minimize", app.IsOpen ? app.Minimize : null);
        Add("Close App", app.IsRunning ? app.Close : null);
        Add("Quit " + app.appName, app.IsRunning ? app.Quit : null, "Ctrl+Q");
        PositionPopup((RectTransform)icon.transform, true);
    }

    private void Add(string label, Action action, string shortcut = "")
    {
        var row = rows[rowCount++];
        row.button.gameObject.SetActive(true);
        row.label.text = label;
        row.shortcut.text = shortcut;
        row.label.color = new Color(1f, 1f, 1f, action == null ? 0.35f : 0.96f);
        row.button.interactable = action != null;
        row.button.onClick.RemoveAllListeners();
        if (action != null) row.button.onClick.AddListener(() => { Close(); action(); });
    }

    private void PositionPopup(RectTransform trigger, bool above)
    {
        popup.sizeDelta = new Vector2(270f, rowCount * 28f + 12f);
        trigger.GetWorldCorners(corners);
        var parent = (RectTransform)popup.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, corners[above ? 1 : 0], null, out var point);
        point.x = Mathf.Clamp(point.x, parent.rect.xMin + 8f, parent.rect.xMax - popup.rect.width - 8f);
        point.y += above ? popup.rect.height + 10f : -4f;
        popup.anchoredPosition = point;
        popup.SetAsLastSibling();
    }

    public void ShowCalculatorMenu(GlassDemoCalculator calculator, Vector2 screenPosition)
    {
        Close();
        dismiss.SetActive(true);
        popup.gameObject.SetActive(true);
        rowCount = 0;
        foreach (var row in rows) row.button.gameObject.SetActive(false);
        AddCalculatorActions(calculator);
        popup.sizeDelta = new Vector2(270f, rowCount * 28f + 12f);
        var parent = (RectTransform)popup.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPosition, null, out var point);
        point.x = Mathf.Clamp(point.x, parent.rect.xMin + 8f, parent.rect.xMax - popup.rect.width - 8f);
        point.y = Mathf.Clamp(point.y, parent.rect.yMin + popup.rect.height + 8f, parent.rect.yMax - 8f);
        popup.anchoredPosition = point;
        popup.SetAsLastSibling();
    }

    private void AddCalculatorActions(GlassDemoCalculator calculator)
    {
        Add("Copy Result", calculator.CopyResult, "Ctrl+C");
        Add("Paste Number", calculator.CanPasteNumber ? calculator.PasteNumber : null, "Ctrl+V");
        Add("Clear", () => calculator.Input("AC"), "Esc");
    }

    public void Close()
    {
        if (dismiss) dismiss.SetActive(false);
        if (popup) popup.gameObject.SetActive(false);
        if (searchPanel) searchPanel.SetActive(false);
        if (helpPanel) helpPanel.SetActive(false);
        if (calendarPanel) calendarPanel.SetActive(false);
        if (controlPanel) controlPanel.SetActive(false);
        DockMenuOpen = false;
        currentTrigger = null;
    }

    public void ShowSearch()
    {
        Close(); dismiss.SetActive(true); searchPanel.SetActive(true);
        searchPanel.transform.SetAsLastSibling();
        searchInput.SetTextWithoutNotify(string.Empty);
        FilterApps(string.Empty);
        searchInput.ActivateInputField();
    }

    public void FilterApps(string query)
    {
        var row = 0;
        foreach (var window in desktop.windows)
        {
            if (window.appName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var button = searchResults[row++];
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<Text>().text = window.appName;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => { Close(); window.Open(); });
        }
        ((RectTransform)searchPanel.transform).sizeDelta = new Vector2(520f, 94f + Mathf.Max(row, 1) * 36f);
        for (; row < searchResults.Length; row++) searchResults[row].gameObject.SetActive(false);
    }

    public void OpenReminders() { Close(); desktop.FindApp("Reminders").Open(); }
    public void OpenWallpaper() { Close(); desktop.OpenWallpaper(); }
    public void OpenSettings() { Close(); desktop.OpenSettings(); }
    private void ShowControls() { dismiss.SetActive(true); controlPanel.SetActive(true); controlPanel.transform.SetAsLastSibling(); }
    public void ShowHelp() { Close(); dismiss.SetActive(true); helpPanel.SetActive(true); helpPanel.transform.SetAsLastSibling(); }
    public void ShowCalendar()
    {
        Close(); dismiss.SetActive(true); calendarPanel.SetActive(true); calendarPanel.transform.SetAsLastSibling();
        calendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        RefreshCalendar();
    }
    public void PreviousMonth() { calendarMonth = calendarMonth.AddMonths(-1); RefreshCalendar(); }
    public void NextMonth() { calendarMonth = calendarMonth.AddMonths(1); RefreshCalendar(); }
    private void RefreshCalendar()
    {
        calendarTitle.text = calendarMonth.ToString("MMMM yyyy");
        var start = ((int)calendarMonth.DayOfWeek + 6) % 7;
        var days = DateTime.DaysInMonth(calendarMonth.Year, calendarMonth.Month);
        for (var i = 0; i < calendarDays.Length; i++)
        {
            var day = i - start + 1;
            calendarDays[i].text = day > 0 && day <= days ? day.ToString() : string.Empty;
            calendarDays[i].color = day == DateTime.Today.Day && calendarMonth.Month == DateTime.Today.Month && calendarMonth.Year == DateTime.Today.Year
                ? new Color(0.35f, 0.72f, 1f) : Color.white;
        }
    }

    private void EditSelection(int operation)
    {
        if (!lastInput) return;
        var from = Mathf.Min(lastInput.selectionAnchorPosition, lastInput.selectionFocusPosition);
        var to = Mathf.Max(lastInput.selectionAnchorPosition, lastInput.selectionFocusPosition);
        if (operation < 2) GUIUtility.systemCopyBuffer = lastInput.text.Substring(from, to - from);
        if (operation == 0 || operation == 2)
        {
            var inserted = operation == 2 ? GUIUtility.systemCopyBuffer : string.Empty;
            lastInput.text = lastInput.text.Substring(0, from) + inserted + lastInput.text.Substring(to);
            lastInput.caretPosition = from + inserted.Length;
        }
        EventSystem.current.SetSelectedGameObject(lastInput.gameObject);
        lastInput.ActivateInputField();
        if (operation == 3) { lastInput.selectionAnchorPosition = 0; lastInput.selectionFocusPosition = lastInput.text.Length; }
    }
}
}
