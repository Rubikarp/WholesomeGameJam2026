using System;

namespace LightSide
{
    internal interface IRangeConfigurationSource
    {
        void AddConfigurationListener(Action listener);
        void RemoveConfigurationListener(Action listener);
    }

    /// <summary>
    /// Base for serializable interaction policy objects whose nested changes can affect a live
    /// rule graph. Consumers bind only while the object participates in an active modifier.
    /// </summary>
    [Serializable]
    [StateHierarchy]
    public abstract class RangeConfigurationObject : IRangeConfigurationSource
    {
        [NonSerialized] private Action configurationChanged;
        [NonSerialized] private int updateDepth;
        [NonSerialized] private bool updatePending;

        protected bool HasConfigurationListeners => configurationChanged != null;

        protected void NotifyConfigurationChanged()
        {
            if (updateDepth != 0)
            {
                updatePending = true;
                return;
            }
            configurationChanged?.Invoke();
        }

        protected void BeginConfigurationUpdate()
            => updateDepth++;

        protected void EndConfigurationUpdate()
        {
            if (updateDepth == 0)
                throw new InvalidOperationException("A range configuration update ended without a matching begin.");
            updateDepth--;
            if (updateDepth != 0 || !updatePending) return;
            updatePending = false;
            configurationChanged?.Invoke();
        }

        protected void BindConfigurationChild(object child)
        {
            if (child is IRangeConfigurationSource source)
                source.AddConfigurationListener(NotifyConfigurationChanged);
        }

        protected void UnbindConfigurationChild(object child)
        {
            if (child is IRangeConfigurationSource source)
                source.RemoveConfigurationListener(NotifyConfigurationChanged);
        }

        /// <summary>
        /// Rebinds a replaced configuration child when this object is observed, then publishes the
        /// replacement as one configuration change.
        /// </summary>
        protected void ApplyConfigurationChildChange(object previous, object current)
        {
            var isBound = HasConfigurationListeners;
            if (isBound) UnbindConfigurationChild(previous);
            if (isBound) BindConfigurationChild(current);
            NotifyConfigurationChanged();
        }

        protected void BindConfigurationChildren<T>(T[] children)
        {
            if (children == null) return;
            for (var i = 0; i < children.Length; i++) BindConfigurationChild(children[i]);
        }

        protected void UnbindConfigurationChildren<T>(T[] children)
        {
            if (children == null) return;
            for (var i = 0; i < children.Length; i++) UnbindConfigurationChild(children[i]);
        }

        protected virtual void OnConfigurationBound() { }

        protected virtual void OnConfigurationUnbound() { }

        void IRangeConfigurationSource.AddConfigurationListener(Action listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            var wasUnbound = configurationChanged == null;
            configurationChanged += listener;
            if (wasUnbound) OnConfigurationBound();
        }

        void IRangeConfigurationSource.RemoveConfigurationListener(Action listener)
        {
            configurationChanged -= listener;
            if (configurationChanged == null) OnConfigurationUnbound();
        }
    }
}
