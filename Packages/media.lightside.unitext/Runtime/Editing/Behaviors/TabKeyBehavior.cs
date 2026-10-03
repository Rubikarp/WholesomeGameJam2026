using System;

namespace LightSide
{
    /// <summary>
    /// Makes plain Tab insert a tab character instead of moving focus to the next control.
    /// Modified combinations (Shift+Tab, Ctrl+Tab) pass through for navigation.
    /// Add this for code editors and multi-line composers.
    /// </summary>
    [Serializable]
    [TypeDescription("Tab inserts a tab character instead of moving focus")]
    [TypeGroup("Keys", 3)]
    public sealed class TabKeyBehavior : InputBehavior
    {
        protected override void OnEnable() => editable.KeyResolver.Subscribe(Resolve);
        protected override void OnDisable() => editable.KeyResolver.Unsubscribe(Resolve);

        private void Resolve(ref KeyResolve key)
        {
            if (key.action != EditAction.None) return;
            if (key.key != NativeKeyCode.Tab || key.modifiers != NativeModifiers.None) return;
            key.action = EditAction.InsertTab;
        }
    }
}
