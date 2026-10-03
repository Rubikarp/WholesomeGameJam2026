using System;

namespace LightSide
{
    /// <summary>
    /// Modifier key flags delivered alongside <see cref="NativeKeyCode"/> from the native layer.
    /// </summary>
    [Flags]
    public enum NativeModifiers
    {
        None  = 0,
        Shift = 1 << 0,
        Ctrl  = 1 << 1,
        Alt   = 1 << 2,
        /// <summary>macOS Command key. On Windows this maps to the Win key (rarely used for editing).</summary>
        Cmd   = 1 << 3,
    }
}
