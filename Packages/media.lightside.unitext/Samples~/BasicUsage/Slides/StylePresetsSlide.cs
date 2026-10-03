namespace LightSide.Samples
{
    internal sealed class StylePresetsSlide : BasicUsageSlide
    {
        public override string Text =>
            "🎭 <b>Style Presets</b>\n\n<t>The quick brown fox</t> jumps over <t2>the lazy dog</t2>.\n<accent>Each theme brings its own personality</accent>.\n<t>Same markup</t>, <t2>different preset</t2> — <accent>instant transformation</accent>.\n\n<link=preset:fire>🔥 Fire</link>  <link=preset:ocean>🌊 Ocean</link>  <link=preset:neon>⚡ Neon</link>";

        private StylePreset firePreset;
        private StylePreset oceanPreset;
        private StylePreset neonPreset;

        public override void Register(BasicUsageExampleBase example)
        {
            firePreset = CreatePreset(
                ("t", "#FF4500", new BaseModifier[] { new ColorModifier(), new BoldModifier() }),
                ("t2", "#FF6B35", new BaseModifier[] { new ColorModifier(), new ItalicModifier() }),
                ("accent", "#FFD700", new BaseModifier[] { new ColorModifier(), new UnderlineModifier() }));
            oceanPreset = CreatePreset(
                ("t", "#0077B6", new BaseModifier[] { new ColorModifier(), new ItalicModifier() }),
                ("t2", "#00B4D8;2", new BaseModifier[] { new ColorModifier(), new LetterSpacingModifier() }),
                ("accent", "#90E0EF", new BaseModifier[] { new ColorModifier(), new UnderlineModifier() }));
            neonPreset = CreatePreset(
                ("t", "#FF006E", new BaseModifier[] { new ColorModifier(), new BoldModifier() }),
                ("t2", "#8338EC", new BaseModifier[] { new ColorModifier(), new UppercaseModifier() }),
                ("accent", "#3A86FF;3,3,0.5", new BaseModifier[] { new ColorModifier(), new WaveModifier() }));

            example.DemoText.StylePresets.Add(neonPreset);
        }

        public override bool HandleLink(BasicUsageExampleBase example, string url)
        {
            if (!url.StartsWith("preset:")) return false;
            var theme = url.Substring(7);
            example.DemoText.StylePresets.Clear();
            example.DemoText.StylePresets.Add(theme switch
            {
                "fire" => firePreset,
                "ocean" => oceanPreset,
                "neon" => neonPreset,
                _ => firePreset
            });
            example.UpdateStatus($"<color=#2ECC71>Theme:</color> {theme}");
            return true;
        }

        public override void Dispose(BasicUsageExampleBase example)
        {
            example.DemoText?.StylePresets.Clear();
            if (firePreset != null) UnityEngine.Object.Destroy(firePreset);
            if (oceanPreset != null) UnityEngine.Object.Destroy(oceanPreset);
            if (neonPreset != null) UnityEngine.Object.Destroy(neonPreset);
        }

        private static StylePreset CreatePreset(params (string tag, string parameter, BaseModifier[] modifiers)[] entries)
        {
            var preset = UnityEngine.ScriptableObject.CreateInstance<StylePreset>();
            foreach (var entry in entries)
            {
                BaseModifier modifier;
                if (entry.modifiers.Length == 1)
                {
                    modifier = entry.modifiers[0];
                }
                else
                {
                    var composite = new CompositeModifier();
                    composite.Modifiers.ReplaceAll(entry.modifiers);
                    modifier = composite;
                }

                preset.Styles.Add(new Style
                {
                    Modifier = modifier,
                    Source = new TagRule(entry.tag) { DefaultParameter = entry.parameter }
                });
            }
            return preset;
        }
    }
}
