using System;

namespace WorldGame.UI
{
    public enum EReactState
    {
        Normal,
        Focused,
        Pressed,
        Disabled,
    }
    
    [Serializable]
    public struct SStateMap<T>
    {
        public T Normal;
        public T Focused;
        public T Pressed;
        public T Disabled;

        public T Resolve(EReactState state) => state switch
        {
            EReactState.Normal => Normal,
            EReactState.Focused => Focused,
            EReactState.Pressed => Pressed,
            EReactState.Disabled => Disabled,
            _ => Normal,
        };

        public SStateMap(T normal, T focused, T pressed, T disabled)
        {
            Normal = normal;
            Focused = focused;
            Pressed = pressed;
            Disabled = disabled;
        }
    }


    [Serializable]
    public abstract class StateReactorBase
    {
        public abstract void React(EReactState state, bool isInstant);
    }
}
