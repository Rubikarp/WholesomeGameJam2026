namespace LightSide
{
    /// <summary>
    /// Single source of truth for every Editor menu path UniText contributes —
    /// <c>Tools/UniText/</c>, <c>Assets/Create/UniText/</c>, <c>GameObject/UI .../UniText/</c>,
    /// component context menus (<c>CONTEXT/UniText/</c>, <c>CONTEXT/UniTextWorld/</c>), the
    /// runtime <c>[AddComponentMenu]</c> and <c>[CreateAssetMenu]</c> values, and the
    /// Project Settings page. Every <c>[MenuItem]</c>, <c>Menu.SetChecked</c>, and
    /// <c>EditorApplication.ExecuteMenuItem</c> call site references a constant defined
    /// here so the package's user-visible menu layout can be reviewed — and reorganized —
    /// in one place.
    /// </summary>
    internal static class UniTextMenu
    {
        private const string Root = "UniText";

        internal static class Tools
        {
            private const string P = "Tools/" + Root + "/";
            public const string Window = P + "Tools";
            public const string Migration = P + "Migration";
            public const string UpgradeLegacyAssets = P + "Upgrade Legacy Assets";
            public const string GlyphDiagnostic = P + "Glyph Diagnostic";
            public const string ClipboardInspector = P + "Clipboard Inspector";
            public const string RangeDebugger = P + "Range Debugger";
            public const string RespectSceneVisibility = P + "Respect Scene Visibility";
            public const string ShowSceneVisibilityOverlay = P + "Show Scene Visibility Overlay";
        }

        internal static class Context
        {
            private const string Text = "CONTEXT/UniText/";
            private const string World = "CONTEXT/UniTextWorld/";
            public const string TextLocalize = Text + "Localize";
            public const string WorldLocalize = World + "Localize";
        }

        
        internal static class Hierarchy
        {
            private const string Canvas = "GameObject/UI (Canvas)/" + Root + "/";
            private const string World = "GameObject/UI (World)/" + Root + "/";
            public const string CanvasText = Canvas + "Text";
            public const string CanvasButton = Canvas + "Button";
            public const string CanvasSelectableText = Canvas + "Selectable Text";
            public const string CanvasEditableText = Canvas + "Editable Text";
            public const string CanvasInputField = Canvas + "Input Field";
            public const string WorldText = World + "World Text";
        }

        internal static class Create
        {
            private const string P = "Assets/Create/" + Root + "/";
            public const string CustomMaterialShader = P + "Custom Effect";
            public const string FontAsset = P + "Font Asset";
            public const string ColorFontAsset = P + "Color Font Asset";
            public const string SystemFontAsset = P + "System Font Asset";
            public const string FontStackCombined = P + "Font Stack (Combined)";
            public const string FontStackPerFont = P + "Font Stack (Per Font)";
            public const string FontVariant = P + "Font Variant";
        }

        internal static class AddComponent
        {
            private const string P = Root + "/";
            public const string Canvas = "UI (Canvas)/" + Root;
            public const string WorldRaycaster = "Event/" + Root + " World Raycaster";
            public const string Selectable = P + "Selectable";
            public const string Editable = P + "Editable";
            public const string AnimationBridge = P + "Animation Bridge";
            public const string Driver = P + "UniText Driver";
            public const string ContextMenu = P + "Context Menu";
            public const string ScrollRectRangeViewport = P + "ScrollRect Range Viewport";
            public const string CameraRangeViewport = P + "Camera Range Viewport";
            public const string UniTextMagnifier = P + "Magnifier";
            public const string UniTextSelectionHandles = P + "Selection Handles";
            public const string PasteControl = P + "Paste Control";
        }

        internal static class CreateAsset
        {
            private const string P = Root + "/";
            public const string Paints = P + "Paints";
            public const string Objects = P + "Objects";
            public const string Sprites = P + "Sprites";
            public const string RevealHandlers = P + "Reveal Handlers";
            public const string StylePreset = P + "Style Preset";
            public const string ModifierGraphPreset = P + "Modifier Graph Preset";
            public const string BehaviorPreset = P + "Behavior Preset";
            public const string RangeChannel = P + "Range Channel";
        }

        /// <summary>Project Settings page, nested under the shared LightSide root so every package of the family groups together.</summary>
        public const string Settings = LightSideMenu.ProjectSettingsRoot + "/" + Root;
    }
}
