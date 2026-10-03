using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoCalculator : MonoBehaviour, IPointerClickHandler
{
    public Text display;
    public Text expression;
    private GlassDemoWindow window;
    private double accumulator;
    private string operation;
    private string entry = "0";
    private bool replaceEntry;
    private bool AcceptsKeyboardInput => window.IsOpen && window.desktop.ActiveWindow == window && !window.desktop.menuBar.HasPopup;
    public bool CanPasteNumber => double.TryParse(GUIUtility.systemCopyBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !double.IsNaN(value) && !double.IsInfinity(value);

    private void Start() => Refresh();
    private void Awake() => window = GetComponent<GlassDemoWindow>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private UnityEngine.InputSystem.Keyboard keyboard;
    private bool CommandHeld => keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed);
    private void OnEnable() => SetKeyboard(UnityEngine.InputSystem.Keyboard.current);
    private void OnDisable() => SetKeyboard(null);
    private void SetKeyboard(UnityEngine.InputSystem.Keyboard value)
    {
        if (keyboard == value) return;
        if (keyboard != null) keyboard.onTextInput -= OnTextInput;
        keyboard = value;
        if (keyboard != null) keyboard.onTextInput += OnTextInput;
    }
    private void OnTextInput(char value)
    {
        // Preserve layout-aware typed symbols. Enter/Backspace are handled as keys
        // below, so platforms that also emit control characters do not apply them twice.
        if (value >= ' ' && AcceptsKeyboardInput && !CommandHeld) TypeCharacter(value);
    }
#endif
    private void Update()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        SetKeyboard(UnityEngine.InputSystem.Keyboard.current);
        if (keyboard == null || !AcceptsKeyboardInput) return;
        if (CommandHeld)
        {
            if (keyboard.cKey.wasPressedThisFrame) CopyResult();
            if (keyboard.vKey.wasPressedThisFrame) PasteNumber();
            return;
        }
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) Input("=");
        if (keyboard.backspaceKey.wasPressedThisFrame) Input("Backspace");
        if (keyboard.escapeKey.wasPressedThisFrame) Input("AC");
#else
        if (!AcceptsKeyboardInput) return;
        var modifier = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl) || UnityEngine.Input.GetKey(KeyCode.LeftCommand) || UnityEngine.Input.GetKey(KeyCode.RightCommand);
        if (modifier)
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.C)) CopyResult();
            if (UnityEngine.Input.GetKeyDown(KeyCode.V)) PasteNumber();
            return;
        }
        var typed = UnityEngine.Input.inputString;
        foreach (var key in typed) TypeCharacter(key);
        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Input("AC");
#endif
    }
    private void TypeCharacter(char key)
    {
        if (key >= '0' && key <= '9' || key == '.' || key == '+' || key == '%' || key == '=') Input(key.ToString());
        else if (key == '-') Input("−");
        else if (key == '*') Input("×");
        else if (key == '/') Input("÷");
        else if (key == '\n' || key == '\r') Input("=");
        else if (key == '\b') Input("Backspace");
    }
    public void CopyResult() => GUIUtility.systemCopyBuffer = entry;
    public void PasteNumber()
    {
        if (!CanPasteNumber) return;
        entry = Format(double.Parse(GUIUtility.systemCopyBuffer, CultureInfo.InvariantCulture));
        replaceEntry = false;
        Refresh();
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        window.Focus();
        window.desktop.menuBar.ShowCalculatorMenu(this, eventData.position);
    }
    public void Input(string value)
    {
        switch (value)
        {
            case "AC": case "C": accumulator = 0; operation = null; entry = "0"; replaceEntry = false; break;
            case "+": case "−": case "×": case "÷":
                if (!replaceEntry) Commit();
                operation = value; replaceEntry = true; break;
            case "=": Commit(); operation = null; replaceEntry = true; break;
            case "+/−": entry = Format(-Value()); break;
            case "%": entry = Format(Value() / 100d); break;
            case "Backspace": entry = replaceEntry || entry.Length < 2 || entry == "Error" ? "0" : entry.Substring(0, entry.Length - 1); replaceEntry = false; break;
            case ".":
                if (replaceEntry || entry == "Error") entry = "0";
                replaceEntry = false;
                if (!entry.Contains(".")) entry += ".";
                break;
            default:
                if (replaceEntry || entry == "0" || entry == "Error") entry = value;
                else if (entry.Length < 12) entry += value;
                replaceEntry = false;
                break;
        }
        Refresh();
    }
    private double Value() => double.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0d;
    private static string Format(double value) => double.IsNaN(value) || double.IsInfinity(value) ? "Error" : value.ToString("G10", CultureInfo.InvariantCulture);
    private void Commit()
    {
        var value = Value();
        accumulator = operation switch { "+" => accumulator + value, "−" => accumulator - value, "×" => accumulator * value, "÷" => value == 0 ? double.NaN : accumulator / value, _ => value };
        entry = Format(accumulator);
    }
    private void Refresh()
    {
        if (display) display.text = entry;
        if (expression) expression.text = operation == null ? string.Empty : Format(accumulator) + " " + operation;
    }
}
}
