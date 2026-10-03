using System;

namespace LightSide
{
    /// <summary>
    /// <see cref="IPaintProvider"/> backed by the project-wide <see cref="UniTextSettings.Paints"/>
    /// asset. Use when the same paint swatches are shared across all UniText components.
    /// </summary>
    [Serializable]
    [TypeDescription("Resolves names through the project-wide UniTextSettings.PaintCatalog asset.")]
    public sealed class GlobalSettingsPaintProvider : BoundNamedCatalog<PaintSwatch, UniTextPaints>, IPaintProvider
    {
        protected override UniTextPaints ResolveAsset() => UniTextSettings.Paints;

        protected override void OnFirstSubscriber() => UniTextSettings.Changed += OnSettingsChanged;

        protected override void OnLastSubscriberRemoved() => UniTextSettings.Changed -= OnSettingsChanged;

        private void OnSettingsChanged(in StateChange change)
        {
            if (!UniTextSettings.Affects(in change, UniTextSettings.Members.Paints)) return;
            RebindResolvedAsset();
        }
    }
}
