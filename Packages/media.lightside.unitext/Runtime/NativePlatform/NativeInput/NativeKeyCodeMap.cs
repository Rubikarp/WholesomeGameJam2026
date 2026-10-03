using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// The single Unity <see cref="KeyCode"/> → <see cref="NativeKeyCode"/> table, shared by the
    /// managed backend (<see cref="ManagedInputBackend"/>) and the editor SceneView driver
    /// (Unity editor key translation). Only the navigation / editing keys plus the A–Z shortcut block
    /// are mapped; bare printable characters never appear here — they flow through TextInput.
    /// </summary>
    internal static class NativeKeyCodeMap
    {
        internal static readonly (KeyCode unity, NativeKeyCode native)[] Table =
        {
            (KeyCode.LeftArrow,   NativeKeyCode.LeftArrow),
            (KeyCode.RightArrow,  NativeKeyCode.RightArrow),
            (KeyCode.UpArrow,     NativeKeyCode.UpArrow),
            (KeyCode.DownArrow,   NativeKeyCode.DownArrow),
            (KeyCode.Home,        NativeKeyCode.Home),
            (KeyCode.End,         NativeKeyCode.End),
            (KeyCode.PageUp,      NativeKeyCode.PageUp),
            (KeyCode.PageDown,    NativeKeyCode.PageDown),
            (KeyCode.Backspace,   NativeKeyCode.Backspace),
            (KeyCode.Delete,      NativeKeyCode.Delete),
            (KeyCode.Return,      NativeKeyCode.Return),
            (KeyCode.KeypadEnter, NativeKeyCode.KeypadEnter),
            (KeyCode.Tab,         NativeKeyCode.Tab),
            (KeyCode.Escape,      NativeKeyCode.Escape),
            (KeyCode.Insert,      NativeKeyCode.Insert),
            (KeyCode.Backslash,   NativeKeyCode.Backslash),
            (KeyCode.A, NativeKeyCode.A),
            (KeyCode.B, NativeKeyCode.B),
            (KeyCode.C, NativeKeyCode.C),
            (KeyCode.D, NativeKeyCode.D),
            (KeyCode.E, NativeKeyCode.E),
            (KeyCode.F, NativeKeyCode.F),
            (KeyCode.G, NativeKeyCode.G),
            (KeyCode.H, NativeKeyCode.H),
            (KeyCode.I, NativeKeyCode.I),
            (KeyCode.J, NativeKeyCode.J),
            (KeyCode.K, NativeKeyCode.K),
            (KeyCode.L, NativeKeyCode.L),
            (KeyCode.M, NativeKeyCode.M),
            (KeyCode.N, NativeKeyCode.N),
            (KeyCode.O, NativeKeyCode.O),
            (KeyCode.P, NativeKeyCode.P),
            (KeyCode.Q, NativeKeyCode.Q),
            (KeyCode.R, NativeKeyCode.R),
            (KeyCode.S, NativeKeyCode.S),
            (KeyCode.T, NativeKeyCode.T),
            (KeyCode.U, NativeKeyCode.U),
            (KeyCode.V, NativeKeyCode.V),
            (KeyCode.W, NativeKeyCode.W),
            (KeyCode.X, NativeKeyCode.X),
            (KeyCode.Y, NativeKeyCode.Y),
            (KeyCode.Z, NativeKeyCode.Z),
        };

        internal static bool TryFromUnity(KeyCode key, out NativeKeyCode native)
        {
            for (int i = 0; i < Table.Length; i++)
            {
                if (Table[i].unity != key) continue;
                native = Table[i].native;
                return true;
            }
            native = NativeKeyCode.None;
            return false;
        }
    }
}
