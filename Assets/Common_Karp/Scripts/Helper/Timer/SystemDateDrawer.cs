using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class SystemDateDrawer : MonoBehaviour
{
    [Header("Base Info")]
    private TextMeshProUGUI timeTextSlot;

    private void Awake()
    {
        timeTextSlot = GetComponent<TextMeshProUGUI>();
    }

    public void Update()
    {
        var today = System.DateTime.Now;
        timeTextSlot.text = $"{today.ToString("M")} {today.Year}";
    }
}
