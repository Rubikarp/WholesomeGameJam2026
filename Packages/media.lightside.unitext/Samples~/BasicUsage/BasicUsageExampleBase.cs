using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Samples
{
    /// <summary>Navigates the Basic Usage slides and owns their shared text and input plumbing.</summary>
    public abstract class BasicUsageExampleBase : MonoBehaviour
    {
        [Header("UniText Components")]
        [SerializeField] protected UniText demoText;
        [SerializeField] protected UniText statusText;

        [Header("Configuration")]
        [Tooltip("Named swatches used by the paint and highlight slides.")]
        [SerializeField] private UniTextPaints demoPaints;

        [Tooltip("Named sprites used by the inline-sprites slide.")]
        [SerializeField] private UniTextSprites demoSprites;

        [Tooltip("OpenType MATH font used by the mathematical typesetting slide.")]
        [SerializeField] private UniTextFont mathFont;
        
        [SerializeField] private Button button;
        [SerializeField] private GameObject basicUsageContainer;
        [SerializeField] private GameObject editableContainer;

        [Tooltip("Field the slideshow focuses to capture the editable screen with the soft keyboard up.")]
        [SerializeField] private UniTextEditable keyboardCaptureField;

        protected readonly List<Style> registeredStyles = new();
        private readonly HashSet<BasicUsageSlide> registeredSlides = new();
        protected int currentExample;
        protected LinkModifier linkModifier;

        private char[] setTextBuffer = new char[2048];
        private readonly StringBuilder setTextStringBuilder = new(2048);
        private readonly PrefixResolver demoResolver = new()
        {
            prefix = "🔧 [Resolver substituted prefix]\n\n"
        };
        private BasicUsageSlide[] slides;
        private BasicUsageSlide activeSlide;
        private BackslashEscapeRule escapeRule;
        private RectTransform demoContainer;
        private readonly List<System.Action<float>> phaseWriters = new();
        private float animationPhase;
        private float defaultContainerWidth;
        private bool wasPressed;
        private bool pointerDown;
        private Vector2 pointerStart;
        private Vector2 pointerLast;

        /// <summary>Number of example slides in the demo.</summary>
        public int ExampleCount => slides?.Length ?? 0;

        /// <summary>Zero-based index of the slide currently shown.</summary>
        public int CurrentExample => currentExample;

        /// <summary>Field the slideshow focuses for its soft-keyboard captures; unassigned outside that scene.</summary>
        public UniTextEditable KeyboardCaptureField => keyboardCaptureField;

        internal UniText DemoText => demoText;
        internal UniTextPaints DemoPaints => demoPaints != null
            ? demoPaints
            : throw new InvalidOperationException("Basic Usage requires Demo Paints.");
        internal UniTextSprites DemoSprites => demoSprites != null
            ? demoSprites
            : throw new InvalidOperationException("Basic Usage requires Demo Sprites.");
        internal UniTextFont MathFont => mathFont;
        internal RectTransform DemoContainer => demoContainer;
        internal float DefaultContainerWidth => defaultContainerWidth;

        /// <summary>Runs before the sample initializes.</summary>
        protected virtual void OnInit()
        {
            button.onClick.AddListener(OnButtonClick);
        }

        private void OnButtonClick() => ShowEditable(!editableContainer.activeSelf);

        /// <summary>Swaps the screen the button toggles: the editable-text demo, or the slides.</summary>
        public void ShowEditable(bool editable)
        {
            basicUsageContainer.SetActive(!editable);
            editableContainer.SetActive(editable);
        }
        
        /// <summary>Applies text through the serialized <see cref="UniTextBase.Text"/> property.</summary>
        protected virtual void ApplyText(string text)
        {
            if (demoText != null && text != null) demoText.Text = text;
        }

        /// <summary>Applies a character range; <paramref name="browserPayload"/> is its string form for external mirrors.</summary>
        protected virtual void ApplySetText(char[] buffer, int offset, int length, string browserPayload)
        {
            if (demoText != null && buffer != null) demoText.SetText(buffer, offset, length);
        }

        /// <summary>Applies read-only memory; <paramref name="browserPayload"/> is its string form for external mirrors.</summary>
        protected virtual void ApplySetTextMemory(ReadOnlyMemory<char> source, string browserPayload)
        {
            if (demoText != null) demoText.SetText(source);
        }

        /// <summary>Applies a string builder; <paramref name="browserPayload"/> is its string form for external mirrors.</summary>
        protected virtual void ApplySetTextStringBuilder(StringBuilder source, string browserPayload)
        {
            if (demoText != null) demoText.SetText(source);
        }

        private void Awake()
        {
            OnInit();
        }

        private void Start()
        {
            if (demoText == null)
            {
                Debug.LogError($"[{GetType().Name}] DemoText is not assigned!");
                return;
            }

            demoContainer = demoText.rectTransform.parent as RectTransform;
            if (demoContainer != null) defaultContainerWidth = demoContainer.sizeDelta.x;

            SetupSlides();
            ShowExample(0);
        }

        private void EnsureRegistered(BasicUsageSlide slide)
        {
            if (escapeRule == null)
            {
                AddSharedStyles();
                SetupLinkEvents();
            }
            if (registeredSlides.Add(slide)) slide.Register(this);
        }

        private void AddSharedStyles()
        {
            escapeRule = new BackslashEscapeRule();
            demoText.AddRule(escapeRule);

            AddStyle(new BoldModifier(), new TagRule("b"));
            AddStyle(new ItalicModifier(), new TagRule("i"));
            AddStyle(new UnderlineModifier(), new TagRule("u"));
            AddStyle(new StrikethroughModifier(), new TagRule("s"));
            AddStyle(new ColorModifier(), new TagRule("color"));
            AddStyle(new SizeModifier(), new TagRule("size"));
            AddStyle(new RubyModifier(), new RubyParseRule());
            AddStyle(new LetterSpacingModifier(), new TagRule("cspace"));
            AddStyle(new CharacterWidthModifier(), new TagRule("cwidth"));
            AddStyle(new LineHeightModifier(), new TagRule("line-height"));
            AddStyle(new FontModifier(), new TagRule("font"));

            linkModifier = new LinkModifier();
            AddStyle(linkModifier, new TagRule("link"));

            AddStyle(new ShadowModifier(), new TagRule("shadow"));
            AddStyle(new GlowModifier(), new TagRule("glow"));
            AddStyle(new ExtrudeModifier(), new TagRule("extrude"));
            AddStyle(new StrokeModifier(), new TagRule("stroke"));
            AddStyle(new FillModifier(), new TagRule("fill"));
            AddStyle(new InnerShadowModifier(), new TagRule("inner-shadow"));
            AddStyle(new UppercaseModifier(), new TagRule("upper"));
            AddStyle(new LowercaseModifier(), new TagRule("lower"));
            AddStyle(new SmallCapsModifier(), new TagRule("smallcaps"));

            var scriptRule = new CompositeParseRule();
            scriptRule.Rules.ReplaceAll(new ParseRule[]
            {
                new TagRule("sup") { DefaultParameter = "super" },
                new TagRule("sub") { DefaultParameter = "sub" },
            });
            AddStyle(new ScriptPositionModifier(), scriptRule);

            AddStyle(new WordSpacingModifier(), new TagRule("wspace"));
            AddStyle(new ParagraphSpacingModifier(), new TagRule("pspace"));
            AddStyle(new AlignmentModifier(), new TagRule("align"));
            AddStyle(new IndentModifier(), new TagRule("indent"));
            AddStyle(new ListModifier(), new TagRule("li"));
            AddStyle(new NoBreakModifier(), new TagRule("nobr"));
            AddStyle(new SeparatorModifier(), new SeparatorParseRule());
            AddStyle(new EllipsisModifier(), new TagRule("ellipsis"));
            AddStyle(new TruncateModifier(), new TagRule("truncate"));
            AddAnimation(new WaveModifier(), "wave");
            AddAnimation(new WobbleModifier(), "wobble");
            AddAnimation(new PulseModifier(), "pulse");
            AddAnimation(new BounceModifier(), "bounce");
            AddAnimation(new ShakeModifier(), "shake");
            AddAnimation(new FloatModifier(), "float");
            AddAnimation(new PendulumModifier(), "pendulum");
            AddAnimation(new SpinModifier(), "spin");
            var glitch = new GlitchModifier();
            AddStyle(glitch, new TagRule("glitch"));
            phaseWriters.Add(value => glitch.Phase = value);
        }

        private void AddAnimation<T>(GlyphParamModifier<T> modifier, string tag) where T : unmanaged
        {
            AddStyle(modifier, new TagRule(tag));
            phaseWriters.Add(value => modifier.Phase = value);
        }

        internal Style AddStyle(BaseModifier modifier, RangeSource source)
        {
            var style = new Style { Modifier = modifier, Source = source };
            demoText.Styles.Add(style);
            registeredStyles.Add(style);
            return style;
        }

        private void SetupSlides()
        {
            slides = new BasicUsageSlide[]
            {
                new IntroSlide(),
                new BidirectionalTextSlide(),
                new EmojiSlide(),
                new StylePresetsSlide(),
                new BasicStylingSlide(),
                new PaletteSlide(),
                new RelativeSizesSlide(),
                new ArabicSlide(),
                new HebrewSlide(),
                new ComplexScriptsSlide(),
                new InteractiveLinksSlide(),
                new SpacingOverviewSlide(),
                new RubySlide(),
                new RubyLayoutSlide(),
                new LanguageSlide(),
                new FontSlide(),
                new MathSlide(),
                new SystemFontSlide(),
                new PaintLayersSlide(),
                new HighlightMappingsSlide(),
                new HighlightPaintsSlide(),
                new AnimationsSlide(),
                new BoldSlide(),
                new ItalicSlide(),
                new UnderlineSlide(),
                new StrikethroughSlide(),
                new ColorSlide(),
                new SizeSlide(),
                new CaseSlide(),
                new ScriptPositionSlide(),
                new SpacingSlide(),
                new LineHeightSlide(),
                new ParagraphSpacingSlide(),
                new AlignmentSlide(),
                new IndentSlide(),
                new ListsSlide(),
                new NoBreakSlide(),
                new SeparatorSlide(),
                new OverflowSlide(),
                new RevealSlide(),
                new DirectionSlide(),
                new InlineSpritesSlide(),
                new AssetBackedModifiersSlide(),
                new InteractiveRangesSlide(),
                new SpoilersSlide(),
                new FindSlide(),
                new OutroSlide()
            };
        }

        private void SetupLinkEvents()
        {
            if (linkModifier == null) return;
            linkModifier.AutoOpenUrl = false;
            linkModifier.LinkClicked += OnLinkClicked;
            linkModifier.LinkEntered += OnLinkEnter;
            linkModifier.LinkExited += OnLinkExit;
        }

        private void Update()
        {
            animationPhase += Time.deltaTime;
            for (var i = 0; i < phaseWriters.Count; i++) phaseWriters[i](animationPhase);
            activeSlide?.Update(this);
            HandleTapNavigation();

            var leftPressed = InputUtils.GetKey(KeyCode.LeftArrow) || InputUtils.GetKey(KeyCode.A);
            var rightPressed = InputUtils.GetKey(KeyCode.RightArrow) || InputUtils.GetKey(KeyCode.D) || InputUtils.GetKey(KeyCode.Space);

            if (leftPressed)
            {
                if (!wasPressed) { PreviousExample(); wasPressed = true; }
            }
            else if (rightPressed)
            {
                if (!wasPressed) { NextExample(); wasPressed = true; }
            }
            else
            {
                wasPressed = false;
            }
        }

        /// <summary>Routes stationary releases outside active text ranges while leaving drags to the scene.</summary>
        private void HandleTapNavigation()
        {
            if (InputUtils.GetPointerPressed())
            {
                if (!pointerDown) { pointerDown = true; pointerStart = InputUtils.MousePosition; }
                pointerLast = InputUtils.MousePosition;
                return;
            }
            if (!pointerDown) return;
            pointerDown = false;

            const float tapSlop = 20f;
            if ((pointerLast - pointerStart).sqrMagnitude > tapSlop * tapSlop) return;
            if (TapHitInteractive(pointerLast)) return;

            if (pointerLast.x < Screen.width * 0.5f) PreviousExample();
            else NextExample();
        }

        private bool TapHitInteractive(Vector2 screenPos)
        {
            if (demoText == null) return false;
            var cam = CanvasUtil.GetCanvasCamera(demoText.canvas);
            var hit = demoText.HitTestRange(screenPos, cam, 0f);
            return hit.hit && (HasRangeAt(linkModifier, hit.cluster) || activeSlide?.HasRangeAt(hit.cluster) == true);
        }

        private static bool HasRangeAt(InteractiveModifier modifier, int cluster)
            => modifier != null && modifier.TryGetRange(cluster, out _);

        /// <summary>Shows the next slide, wrapping to the beginning.</summary>
        public void NextExample()
        {
            currentExample = (currentExample + 1) % slides.Length;
            ShowExample(currentExample);
        }

        /// <summary>Shows the previous slide, wrapping to the end.</summary>
        public void PreviousExample()
        {
            currentExample = (currentExample - 1 + slides.Length) % slides.Length;
            ShowExample(currentExample);
        }

        private void ShowExample(int index)
        {
            activeSlide?.Exit(this);
            activeSlide = slides[index];
            EnsureRegistered(activeSlide);
            activeSlide.Enter(this);
            var source = activeSlide.Text;

            string mode;
            switch (index % 5)
            {
                case 0:
                    if (demoText.TextResolver != null) demoText.TextResolver = null;
                    ApplyText(source);
                    mode = "Text (persists)";
                    Debug.Log($"[SetText Test] Text setter ← \"{source}\"");
                    break;
                case 1:
                    if (demoText.TextResolver != null) demoText.TextResolver = null;
                    if (setTextBuffer.Length < source.Length)
                        setTextBuffer = new char[Mathf.NextPowerOfTwo(source.Length)];
                    source.CopyTo(0, setTextBuffer, 0, source.Length);
                    ApplySetText(setTextBuffer, 0, source.Length, source);
                    mode = "SetText(char[])";
                    Debug.Log($"[SetText Test] SetText(char[]) ← \"{source}\"");
                    break;
                case 2:
                    if (demoText.TextResolver != null) demoText.TextResolver = null;
                    ApplySetTextMemory(source.AsMemory(), source);
                    mode = "SetText(ReadOnlyMemory<char>)";
                    Debug.Log($"[SetText Test] SetText(ROM) ← \"{source}\"");
                    break;
                case 3:
                    if (demoText.TextResolver != null) demoText.TextResolver = null;
                    setTextStringBuilder.Clear();
                    setTextStringBuilder.Append(source);
                    ApplySetTextStringBuilder(setTextStringBuilder, source);
                    mode = "SetText(StringBuilder)";
                    Debug.Log($"[SetText Test] SetText(StringBuilder) ← \"{source}\"");
                    break;
                default:
                    if (demoText.TextResolver != demoResolver) demoText.TextResolver = demoResolver;
                    ApplySetTextMemory(source.AsMemory(), source);
                    mode = "SetText + IUniTextResolver";
                    Debug.Log($"[SetText Test] Resolver active ← \"{source}\" (prefix added at render)");
                    break;
            }

            UpdateStatus($"Example {index + 1}/{slides.Length} ({mode}) - Press Arrow keys");
        }

        internal void SetContainerWidth(float width)
        {
            if (demoContainer == null) return;
            var size = demoContainer.sizeDelta;
            size.x = width;
            demoContainer.sizeDelta = size;
        }

        private void OnLinkClicked(string url)
        {
            if (activeSlide?.HandleLink(this, url) == true) return;

            UpdateStatus($"<color=#2ECC71>Clicked:</color> {url}");
            if (url.StartsWith("http")) Application.OpenURL(url);
        }

        private void OnLinkEnter(string url)
        {
            UpdateStatus($"<color=#3498DB>Hovering:</color> {url}");
        }

        private void OnLinkExit()
        {
            UpdateStatus($"Example {currentExample + 1}/{slides.Length}");
        }

        internal void UpdateStatus(string message)
        {
            if (statusText != null) statusText.Text = message;
        }

        private void OnDestroy()
        {
            activeSlide?.Exit(this);

            if (linkModifier != null)
            {
                linkModifier.LinkClicked -= OnLinkClicked;
                linkModifier.LinkEntered -= OnLinkEnter;
                linkModifier.LinkExited -= OnLinkExit;
            }

            foreach (var slide in registeredSlides)
                slide.Dispose(this);
            registeredSlides.Clear();

            foreach (var style in registeredStyles)
                demoText?.Styles.Remove(style);
            registeredStyles.Clear();

            if (escapeRule != null) demoText?.RemoveRule(escapeRule);
        }

        private sealed class PrefixResolver : IUniTextResolver
        {
            public string prefix;

            public bool TryResolve(ReadOnlyMemory<char> source, out ReadOnlyMemory<char> result)
            {
                result = (prefix + source).AsMemory();
                return true;
            }
        }
    }
}
