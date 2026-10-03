using System;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class SystemTimeDrawer : MonoBehaviour
{
    private enum TimeFormat
    {
        HourOnly,        // 14:35
        HourMinuteSecond, // 14:35:12
        DayAndTime,     // Wed 14:35
        ShortDate,       // 06/03/2026
        LongDate,        // Wednesday, June 3, 2026
    }

    [Header("Base Info")]
    [SerializeField] private TextMeshProUGUI timeTextSlot;
    [SerializeField] private TimeFormat timeFormat = TimeFormat.HourOnly;

    [Header("Display Options")]
    [SerializeField] private bool use24HourFormat = true;
    
    private void Update()
    {
        Refresh();
    }
    private void OnValidate()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (timeTextSlot == null) timeTextSlot = GetComponent<TextMeshProUGUI>();

        DateTime now = DateTime.Now;

        timeTextSlot.text = timeFormat switch
        {
            TimeFormat.HourOnly => FormatTime(now),
            TimeFormat.HourMinuteSecond => $"{FormatTime(now)}:{now.ToString("ss")}",
            TimeFormat.ShortDate => now.ToShortDateString(),
            TimeFormat.LongDate => now.ToLongDateString(),
            TimeFormat.DayAndTime => $"{now:ddd} {FormatTime(now)}",
            _ => "error"
        };
    }

    private string FormatTime(DateTime dateTime)
    {
        if (use24HourFormat) return dateTime.ToString("HH:mm");
        return dateTime.ToString("hh:mm tt");
    }
}

