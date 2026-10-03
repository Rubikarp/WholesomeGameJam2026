using System;

namespace LightSide
{
    /// <summary>
    /// Non-generic control surface a driver holds over one driven parameter: per-member and
    /// broadcast application of a normalized 0..1 position between two typed endpoints.
    /// </summary>
    internal interface IParameterDrive
    {
        int Count { get; }
        bool IsAlive { get; }
        event Action Changed;
        void Apply(int index, float t);
        void ApplyAll(float t);
        void Withhold();
        void Release();
    }

    /// <summary>
    /// Typed bridge between a driver and a <see cref="OwnedParameterSet{TModifier,TValue}"/>:
    /// maps normalized positions through the parameter's interpolation between two endpoints.
    /// </summary>
    internal sealed class ParameterDrive<TModifier, TValue> : IParameterDrive
        where TModifier : BaseModifier
    {
        private readonly OwnedParameterSet<TModifier, TValue> set;
        private readonly ParameterDescriptor<TModifier, TValue> parameter;
        private readonly TValue from;
        private readonly TValue to;

        public int Count => set.IsAlive ? set.Count : 0;

        public bool IsAlive => set.IsAlive;

        public event Action Changed
        {
            add => set.Changed += value;
            remove => set.Changed -= value;
        }

        public ParameterDrive(OwnedParameterSet<TModifier, TValue> set,
            ParameterDescriptor<TModifier, TValue> parameter, TValue from, TValue to)
        {
            this.set = set;
            this.parameter = parameter;
            this.from = from;
            this.to = to;
        }

        public void Apply(int index, float t)
            => set.SetValue(index, parameter.Lerp(from, to, t));

        public void ApplyAll(float t)
        {
            if (!set.IsAlive) return;
            set.Value = parameter.Lerp(from, to, t);
        }

        public void Withhold()
        {
            if (set.IsAlive) set.Withhold();
        }

        public void Release()
        {
            if (set.IsAlive) set.Release();
        }
    }
}
