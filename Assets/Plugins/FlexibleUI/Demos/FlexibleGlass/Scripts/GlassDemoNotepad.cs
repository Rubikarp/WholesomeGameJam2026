using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoNotepad : MonoBehaviour
{
    public InputField titleInput;
    public InputField bodyInput;
    public Button rowTemplate;
    public RectTransform listContent;
    public Button deleteButton;
    public Text count;
    private readonly List<string> titles = new() { "Weekend", "Shopping list", "Ideas" };
    private readonly List<string> bodies = new() {
        "Saturday\n\nCoffee with Sam at 10.\nWalk by the river.\nPick up the print from the studio.",
        "Coffee beans\nOat milk\nLemons\nFresh bread",
        "A place to put the next idea."
    };
    private readonly List<Button> rows = new();
    private int selected;

    private void Start() { RebuildRows(); Select(0); }
    public void SetTitle(string value) { titles[selected] = value; RefreshRows(); }
    public void SetBody(string value) { bodies[selected] = value; RefreshRows(); }

    public void Select(int index)
    {
        selected = Mathf.Clamp(index, 0, titles.Count - 1);
        titleInput.SetTextWithoutNotify(titles[selected]);
        bodyInput.SetTextWithoutNotify(bodies[selected]);
        RefreshRows();
    }

    public void AddNote()
    {
        titles.Add("New note");
        bodies.Add(string.Empty);
        RebuildRows();
        Select(titles.Count - 1);
        titleInput.ActivateInputField();
    }

    public void DeleteNote()
    {
        titles.RemoveAt(selected);
        bodies.RemoveAt(selected);
        if (titles.Count == 0) { titles.Add("New note"); bodies.Add(string.Empty); }
        RebuildRows();
        Select(Mathf.Min(selected, titles.Count - 1));
    }

    private void RebuildRows()
    {
        foreach (var row in rows) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
        rows.Clear();
        for (var i = 0; i < titles.Count; i++)
        {
            var index = i;
            var row = Instantiate(rowTemplate, listContent);
            row.name = "Note Row " + i;
            row.gameObject.SetActive(true);
            row.onClick.AddListener(() => Select(index));
            rows.Add(row);
        }
        GetComponent<GlassDemoWindow>().RefreshGlass();
    }

    private void RefreshRows()
    {
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].GetComponentInChildren<Text>().text = string.IsNullOrWhiteSpace(titles[i]) ? "Untitled" : titles[i];
            var preview = rows[i].transform.Find("Preview");
            if (preview)
            {
                var text = bodies[i].Replace('\n', ' ').Replace('\r', ' ').Trim();
                preview.GetComponent<Text>().text = text.Length == 0 ? "No additional text" : text.Length > 32 ? text.Substring(0, 32) + "…" : text;
            }
            GlassDemoWindow.SetGlassSelection(rows[i], i == selected, new Color(0.7f, 0.46f, 0.09f));
        }
        count.text = titles.Count + (titles.Count == 1 ? " note" : " notes");
        deleteButton.interactable = titles.Count > 0;
    }
}
}
