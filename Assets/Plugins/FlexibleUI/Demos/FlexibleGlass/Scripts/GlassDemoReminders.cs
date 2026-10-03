using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoReminders : MonoBehaviour
{
    [Serializable] private sealed class Reminder
    {
        public string title;
        public bool complete;
        public Reminder(string title) => this.title = title;
    }
    public InputField newReminder;
    public RectTransform content;
    public GlassDemoReminderRow rowTemplate;
    public Text count;
    public Text heading;
    public Button[] filters;
    private readonly List<Reminder> reminders = new() { new("Book a table for Friday"), new("Pick up coffee beans"), new("Send the weekend photos"), new("Water the plants") };
    private readonly List<GlassDemoReminderRow> rows = new();
    private int filter;
    public int TotalCount => reminders.Count;
    public int CompletedCount
    {
        get
        {
            var complete = 0;
            foreach (var reminder in reminders) if (reminder.complete) complete++;
            return complete;
        }
    }

    private void Start() => Rebuild();
    public void FocusNew() => newReminder.ActivateInputField();
    public void AddReminder()
    {
        if (string.IsNullOrWhiteSpace(newReminder.text)) { FocusNew(); return; }
        reminders.Add(new Reminder(newReminder.text.Trim()));
        newReminder.SetTextWithoutNotify(string.Empty);
        filter = 0;
        Rebuild();
        FocusNew();
    }
    public void Submit(string value)
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) AddReminder();
#else
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) AddReminder();
#endif
    }
    public void SelectFilter(int index) { filter = index; Rebuild(); }
    private void Rebuild()
    {
        foreach (var row in rows) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
        rows.Clear();
        var complete = 0;
        foreach (var reminder in reminders)
        {
            if (reminder.complete) complete++;
            if (filter == 1 && reminder.complete || filter == 2 && !reminder.complete) continue;
            var row = Instantiate(rowTemplate, content);
            row.gameObject.SetActive(true);
            row.title.SetTextWithoutNotify(reminder.title);
            row.done.SetIsOnWithoutNotify(reminder.complete);
            row.title.textComponent.color = new Color(1, 1, 1, reminder.complete ? 0.4f : 0.95f);
            row.title.onValueChanged.AddListener(value => reminder.title = value);
            row.done.onValueChanged.AddListener(value => { reminder.complete = value; Rebuild(); });
            row.delete.onClick.AddListener(() => { reminders.Remove(reminder); Rebuild(); });
            rows.Add(row);
        }
        count.text = (reminders.Count - complete) + " remaining";
        heading.text = filter == 2 ? "Completed" : filter == 1 ? "Upcoming" : "Reminders";
        for (var i = 0; i < filters.Length; i++) GlassDemoWindow.SetGlassSelection(filters[i], i == filter, new Color(0.12f, 0.44f, 0.85f));
        GetComponent<GlassDemoWindow>().RefreshGlass();
    }
}
}
