using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal unsafe partial class GlyphDiagnosticWindow
    {
        private InspectorStack toolkitControls;
        private InspectorStack toolkitMessage;
        private InspectorSection toolkitRunSection;
        private ScrollView toolkitRunStrip;
        private InspectorRow toolkitRunItems;
        private VisualElement toolkitWorkspace;
        private VisualElement toolkitCanvas;
        private Image toolkitFill;
        private VisualElement toolkitIndices;
        private Label toolkitStatus;
        private Label toolkitCursor;
        private InspectorStack toolkitSidePanel;
        private int capturedPointer = -1;

        private void CreateToolkitGUI()
        {
            var panel = rootVisualElement;
            UniTextInspectorTheme.Initialize(panel);
            InspectorVisuals.ClearContent(panel);
            var root = InspectorVisuals.CreateWindowRoot(panel);
            toolkitControls = InspectorVisuals.CreateSectionStack();
            toolkitControls.AddToClassList("unitext-glyph-diagnostic__controls");
            root.Add(toolkitControls);
            toolkitMessage = InspectorVisuals.CreateStack();
            root.Add(toolkitMessage);
            toolkitRunSection = InspectorVisuals.CreateSection("Glyph Run");
            toolkitRunStrip = new ScrollView(ScrollViewMode.Horizontal);
            toolkitRunStrip.AddToClassList("unitext-glyph-diagnostic__run");
            toolkitRunItems = InspectorVisuals.CreateCompactRow();
            toolkitRunItems.AddToClassList("unitext-glyph-diagnostic__run-items");
            toolkitRunStrip.Add(toolkitRunItems);
            toolkitRunSection.Add(toolkitRunStrip);
            root.Add(toolkitRunSection);
            toolkitWorkspace = CreateWorkspace();
            root.Add(toolkitWorkspace);
            BuildToolkitControls();
            RefreshToolkitDiagnostics();
        }

        private void BuildToolkitControls()
        {
            InspectorVisuals.ClearContent(toolkitControls);
            var shaping = InspectorVisuals.CreateSection("Shaping");
            var fontField = new InspectorObjectField("Font", typeof(UniTextFont))
            {
                value = font,
            };
            fontField.RegisterValueChangedCallback(evt =>
            {
                font = evt.newValue as UniTextFont;
                OnFontChanged();
                dirtyShape = true;
                BuildToolkitControls();
                RefreshToolkitDiagnostics();
            });
            shaping.Add(fontField);

            var textField = new InspectorTextArea("Text")
            {
                value = text ?? string.Empty,
            };
            textField.AddToClassList("unitext-glyph-diagnostic__text");
            textField.RegisterValueChangedCallback(evt =>
            {
                text = evt.newValue;
                dirtyShape = true;
                RefreshToolkitDiagnostics();
            });
            shaping.Add(textField);

            var directionField = new EnumSelectorField("Direction", direction);
            directionField.RegisterValueChangedCallback(evt =>
            {
                direction = (Dir)evt.newValue;
                dirtyShape = true;
                RefreshToolkitDiagnostics();
            });
            var scriptNames = scripts.Select(script => script.name).ToList();
            var scriptField = new SelectorField<string>("Script", scriptNames, scriptIdx);
            scriptField.RegisterValueChangedCallback(evt =>
            {
                scriptIdx = scriptField.Index;
                dirtyShape = true;
                RefreshToolkitDiagnostics();
            });
            shaping.Add(directionField);
            shaping.Add(scriptField);
            toolkitControls.Add(shaping);

            var features = InspectorVisuals.CreateSection(
                "OpenType Features", true, showFeatures);
            features.Changed += value => showFeatures = value;
            var featureRow = InspectorVisuals.CreateCompactWrapRow();
            for (var i = 0; i < featureToggles.Length; i++)
            {
                var index = i;
                var toggle = new Button { text = featureToggles[i].tag };
                toggle.AddToClassList("lightside-choice-chip");
                toggle.EnableInClassList(
                    "lightside-choice-chip--selected", featureToggles[i].on);
                toggle.clicked += () =>
                {
                    featureToggles[index].on = !featureToggles[index].on;
                    toggle.EnableInClassList(
                        "lightside-choice-chip--selected", featureToggles[index].on);
                    dirtyShape = true;
                    RefreshToolkitDiagnostics();
                };
                featureRow.Add(toggle);
            }
            features.Add(featureRow);
            var custom = new TextField("Custom (tag=val,…)") { value = customFeatures };
            custom.RegisterValueChangedCallback(evt =>
            {
                customFeatures = evt.newValue;
                dirtyShape = true;
                RefreshToolkitDiagnostics();
            });
            features.Add(custom);
            toolkitControls.Add(features);

            if (axisInfos == null || axisInfos.Length == 0)
            {
                InspectorVisuals.AlignFields(toolkitControls);
                return;
            }
            var axes = InspectorVisuals.CreateSection(
                $"Variable Axes ({axisInfos.Length})", true, showAxes);
            axes.Changed += value => showAxes = value;
            for (var i = 0; i < axisInfos.Length; i++)
            {
                var index = i;
                var axis = axisInfos[i];
                var row = InspectorVisuals.CreateRow();
                var slider = new InspectorSlider(TagToString(axis.tag), axis.minValue, axis.maxValue)
                {
                    value = axisValues[i],
                };
                slider.AddToClassList("unitext-glyph-diagnostic__axis");
                slider.RegisterValueChangedCallback(evt =>
                {
                    axisValues[index] = evt.newValue;
                    InvalidateVariations();
                });
                row.Add(slider);
                row.Add(new Button(() =>
                {
                    axisValues[index] = axis.defaultValue;
                    slider.SetValueWithoutNotify(axis.defaultValue);
                    InvalidateVariations();
                })
                {
                    text = "↺",
                    tooltip = "Reset to the font default",
                });
                axes.Add(row);
            }
            toolkitControls.Add(axes);
            InspectorVisuals.AlignFields(toolkitControls);
        }

        private void InvalidateVariations()
        {
            extractCache.Clear();
            DropSelectedTextures();
            viewForceRefresh = true;
            dirtyShape = true;
            RefreshToolkitDiagnostics();
        }

        private VisualElement CreateWorkspace()
        {
            var workspace = new VisualElement();
            workspace.AddToClassList("unitext-glyph-diagnostic__workspace");
            var viewportColumn = InspectorVisuals.CreateCard();
            viewportColumn.AddToClassList("unitext-glyph-diagnostic__viewport-card");
            viewportColumn.Add(CreateViewportToolbar());
            var viewport = new VisualElement();
            viewport.AddToClassList("unitext-glyph-diagnostic__viewport");
            viewport.style.position = Position.Relative;
            viewport.style.backgroundColor = BgColor;
            viewportColumn.Add(viewport);

            toolkitFill = new Image
            {
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore,
            };
            toolkitFill.style.position = Position.Absolute;
            viewport.Add(toolkitFill);
            toolkitCanvas = new VisualElement { focusable = true };
            toolkitCanvas.AddToClassList(InspectorVisuals.WheelOwnerClass);
            FillParent(toolkitCanvas);
            toolkitCanvas.generateVisualContent += PaintToolkitViewport;
            viewport.Add(toolkitCanvas);
            toolkitIndices = new VisualElement { pickingMode = PickingMode.Ignore };
            FillParent(toolkitIndices);
            viewport.Add(toolkitIndices);
            toolkitStatus = CreateViewportLabel();
            toolkitStatus.style.bottom = 18f;
            viewport.Add(toolkitStatus);
            toolkitCursor = CreateViewportLabel();
            toolkitCursor.style.bottom = 2f;
            viewport.Add(toolkitCursor);
            RegisterViewportInput();
            toolkitCanvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                FrameToolkitViewportIfNeeded();
                RefreshToolkitCanvas();
            });

            toolkitSidePanel = InspectorVisuals.CreateStack();
            toolkitSidePanel.AddToClassList("unitext-glyph-diagnostic__side");
            toolkitSidePanel.Add(CreateSideToolbar());
            var sideCard = InspectorVisuals.CreateCard();
            sideCard.AddToClassList("unitext-glyph-diagnostic__side-card");
            sideCard.Add(toolkitSidePanel);
            workspace.Add(viewportColumn);
            workspace.Add(sideCard);
            return workspace;
        }

        private VisualElement CreateViewportToolbar()
        {
            var toolbar = InspectorVisuals.CreateCompactWrapRow();
            toolbar.AddToClassList("unitext-glyph-diagnostic__viewport-toolbar");
            AddExclusiveChoicePair(toolbar, "Single", "Line",
                () => viewMode == ViewMode.Single,
                single =>
                {
                    viewMode = single ? ViewMode.Single : ViewMode.Line;
                    frameQueued = true;
                    RefreshToolkitCanvas();
                });
            AddViewportToggle(toolbar, "RAW", () => showRaw, value => showRaw = value);
            AddViewportToggle(toolbar, "UNION", () => showResolved, value =>
            {
                showResolved = value;
                DropSelectedTextures();
            });
            AddViewportToggle(toolbar, "Fill", () => showFill, value => showFill = value);
            AddViewportToggle(toolbar, "Ctrl", () => showControlPts, value => showControlPts = value);
            AddViewportToggle(toolbar, "Pts", () => showOnCurve, value => showOnCurve = value);
            AddViewportToggle(toolbar, "Dir", () => showDirection, value => showDirection = value);
            AddViewportToggle(toolbar, "Idx", () => showIndices, value => showIndices = value);
            AddViewportToggle(toolbar, "Junc", () => showJunctions, value => showJunctions = value);
            AddViewportToggle(toolbar, "Grid", () => showMetrics, value => showMetrics = value);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);
            toolbar.Add(new Button(() =>
            {
                frameQueued = true;
                RefreshToolkitCanvas();
            }) { text = "Frame (F)" });
            return toolbar;
        }

        private void AddViewportToggle(VisualElement toolbar, string label,
            Func<bool> get, Action<bool> set)
        {
            var toggle = CreateChoiceButton(label, get());
            toggle.clicked += () =>
            {
                var value = !get();
                set(value);
                SetChoiceSelected(toggle, value);
                RefreshToolkitCanvas();
            };
            toolbar.Add(toggle);
        }

        private static void AddExclusiveChoicePair(VisualElement toolbar, string firstLabel,
            string secondLabel, Func<bool> firstSelected, Action<bool> selectFirst)
        {
            var first = CreateChoiceButton(firstLabel, firstSelected());
            var second = CreateChoiceButton(secondLabel, !firstSelected());
            first.clicked += () =>
            {
                if (firstSelected()) return;
                SetChoiceSelected(first, true);
                SetChoiceSelected(second, false);
                selectFirst(true);
            };
            second.clicked += () =>
            {
                if (!firstSelected()) return;
                SetChoiceSelected(first, false);
                SetChoiceSelected(second, true);
                selectFirst(false);
            };
            toolbar.Add(first);
            toolbar.Add(second);
        }

        private VisualElement CreateSideToolbar()
        {
            var toolbar = InspectorVisuals.CreateCompactRow();
            toolbar.AddToClassList("unitext-glyph-diagnostic__side-toolbar");
            var panels = new[] { "Data", "SDF", "Union" };
            var toggles = new List<Button>();
            for (var i = 0; i < panels.Length; i++)
            {
                var target = (Panel)i;
                var toggle = CreateChoiceButton(panels[i], panel == target);
                toggle.clicked += () =>
                {
                    if (panel == target) return;
                    panel = target;
                    for (var index = 0; index < toggles.Count; index++)
                        SetChoiceSelected(toggles[index], index == (int)target);
                    RefreshToolkitSidePanel();
                };
                toggles.Add(toggle);
                toolbar.Add(toggle);
            }
            return toolbar;
        }

        private static Button CreateChoiceButton(string text, bool selected)
        {
            var button = new Button { text = text };
            button.AddToClassList("lightside-choice-chip");
            SetChoiceSelected(button, selected);
            return button;
        }

        private static void SetChoiceSelected(Button button, bool selected)
            => button.EnableInClassList("lightside-choice-chip--selected", selected);

        private static void FillParent(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.right = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
        }

        private static Label CreateViewportLabel()
        {
            var label = new Label
            {
                pickingMode = PickingMode.Ignore,
            };
            label.style.position = Position.Absolute;
            label.style.left = 6f;
            label.style.right = 6f;
            label.style.color = new Color(0.8f, 0.85f, 0.9f);
            label.style.fontSize = 10f;
            return label;
        }

        private void RefreshToolkitDiagnostics()
        {
            if (toolkitMessage == null) return;
            InspectorVisuals.ClearContent(toolkitMessage);
            var valid = font != null && font.HasFontData;
            toolkitMessage.style.display = valid ? DisplayStyle.None : DisplayStyle.Flex;
            toolkitWorkspace.style.display = valid ? DisplayStyle.Flex : DisplayStyle.None;
            toolkitRunSection.style.display = valid ? DisplayStyle.Flex : DisplayStyle.None;
            if (!valid)
            {
                toolkitMessage.Add(new HelpBox(
                    "Assign a UniTextFont asset with loaded font data.",
                    HelpBoxMessageType.Info));
                return;
            }
            EnsurePrivateFace();
            if (dirtyShape)
            {
                Shape();
                dirtyShape = false;
            }
            BuildToolkitRunStrip();
            RefreshViewedGlyph();
            FrameToolkitViewportIfNeeded();
            RefreshToolkitCanvas();
            RefreshToolkitSidePanel();
        }

        private void BuildToolkitRunStrip()
        {
            InspectorVisuals.ClearContent(toolkitRunItems);
            if (!string.IsNullOrEmpty(shapeError))
            {
                toolkitRunItems.Add(new HelpBox(shapeError, HelpBoxMessageType.Error));
                return;
            }
            if (run.Length == 0)
            {
                toolkitRunItems.Add(new Label("Type text above to shape."));
                return;
            }
            for (var i = 0; i < run.Length; i++)
            {
                var index = i;
                var glyph = run[i];
                var button = new Button(() =>
                {
                    selected = index;
                    RefreshViewedGlyph();
                    RefreshToolkitCanvas();
                    RefreshToolkitSidePanel();
                    BuildToolkitRunStrip();
                })
                {
                    text = $"{i}\ng{glyph.glyphId}",
                    tooltip = $"'{SafeChar(glyph.srcCp)}'  cluster {glyph.cluster}  " +
                              $"U+{glyph.srcCp:X4}  adv {glyph.xAdvance}",
                };
                button.AddToClassList("unitext-glyph-diagnostic__glyph");
                button.EnableInClassList(
                    "unitext-glyph-diagnostic__glyph--selected", i == selected);
                toolkitRunItems.Add(button);
            }
        }

        private void RegisterViewportInput()
        {
            toolkitCanvas.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.F) return;
                frameQueued = true;
                RefreshToolkitCanvas();
                evt.StopPropagation();
            });
            toolkitCanvas.RegisterCallback<WheelEvent>(evt =>
            {
                var local = evt.localMousePosition;
                var factor = Mathf.Pow(1.1f, -evt.delta.y);
                var glyph = ToolkitInvMap(local);
                viewScale *= factor;
                viewOffset.x = local.x - glyph.x * viewScale;
                viewOffset.y = local.y + glyph.y * viewScale;
                RefreshToolkitCanvas();
                evt.StopPropagation();
            });
            toolkitCanvas.RegisterCallback<PointerDownEvent>(evt =>
            {
                toolkitCanvas.Focus();
                if (evt.button == 2 || evt.button == 0 && evt.altKey)
                {
                    capturedPointer = evt.pointerId;
                    lastMouse = evt.localPosition;
                    toolkitCanvas.CapturePointer(evt.pointerId);
                }
                else if (evt.button == 0 && viewMode == ViewMode.Line)
                {
                    PickLineGlyph(evt.localPosition);
                    RefreshViewedGlyph();
                    BuildToolkitRunStrip();
                    RefreshToolkitSidePanel();
                }
                RefreshToolkitCanvas();
                evt.StopPropagation();
            });
            toolkitCanvas.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (capturedPointer == evt.pointerId &&
                    toolkitCanvas.HasPointerCapture(evt.pointerId))
                {
                    var local = (Vector2)evt.localPosition;
                    viewOffset += local - lastMouse;
                    lastMouse = local;
                    RefreshToolkitCanvas();
                }
                if (viewMode == ViewMode.Single)
                {
                    var point = ToolkitInvMap(evt.localPosition);
                    toolkitCursor.text = $"cursor  x {point.x:F4}  y {point.y:F4}";
                }
            });
            toolkitCanvas.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (capturedPointer != evt.pointerId) return;
                toolkitCanvas.ReleasePointer(evt.pointerId);
                capturedPointer = -1;
            });
        }

        private void FrameToolkitViewportIfNeeded()
        {
            if (!frameQueued || run.Length == 0 || toolkitCanvas.contentRect.width <= 1f) return;
            var extract = GetExtract(run[selected].glyphId);
            if (!extract.valid) return;
            FrameView(toolkitCanvas.contentRect, extract);
            frameQueued = false;
        }

        private void RefreshToolkitCanvas()
        {
            if (toolkitCanvas == null) return;
            FrameToolkitViewportIfNeeded();
            UpdateToolkitFill();
            UpdateToolkitIndices();
            UpdateToolkitStatus();
            toolkitCanvas.MarkDirtyRepaint();
        }

        private void UpdateToolkitFill()
        {
            if (run.Length == 0 || viewMode != ViewMode.Single || !showFill)
            {
                toolkitFill.style.display = DisplayStyle.None;
                return;
            }
            var extract = GetExtract(run[selected].glyphId);
            if (!extract.valid)
            {
                toolkitFill.style.display = DisplayStyle.None;
                return;
            }
            if (fillTex == null || fillResolved != showResolved)
            {
                GenerateFillTexture(extract);
                fillResolved = showResolved;
            }
            var topLeft = ToolkitMap(new Vector2(0f, 1f));
            var bottomRight = ToolkitMap(new Vector2(AspectOf(extract), 0f));
            toolkitFill.image = fillTex;
            toolkitFill.style.display = DisplayStyle.Flex;
            toolkitFill.style.left = topLeft.x;
            toolkitFill.style.top = topLeft.y;
            toolkitFill.style.width = bottomRight.x - topLeft.x;
            toolkitFill.style.height = bottomRight.y - topLeft.y;
        }

        private void UpdateToolkitIndices()
        {
            InspectorVisuals.ClearContent(toolkitIndices);
            if (!showIndices || viewMode != ViewMode.Single || run.Length == 0) return;
            var extract = GetExtract(run[selected].glyphId);
            if (!extract.valid) return;
            var active = showResolved ? extract.res : extract.raw;
            var count = showResolved ? extract.resCount : extract.rawCount;
            for (var i = 0; i < count; i++)
            {
                var segment = active[i];
                var x = 0.25f * segment.p0x + 0.5f * segment.p1x + 0.25f * segment.p2x;
                var y = 0.25f * segment.p0y + 0.5f * segment.p1y + 0.25f * segment.p2y;
                var point = ToolkitMap(new Vector2(x, y));
                var label = new Label(i.ToString());
                label.style.position = Position.Absolute;
                label.style.left = point.x + 3f;
                label.style.top = point.y - 8f;
                label.style.fontSize = 9f;
                label.style.color = new Color(1f, 1f, 1f, 0.85f);
                toolkitIndices.Add(label);
            }
        }

        private void UpdateToolkitStatus()
        {
            if (run.Length == 0)
            {
                toolkitStatus.text = string.Empty;
                toolkitCursor.text = string.Empty;
                return;
            }
            var extract = GetExtract(run[selected].glyphId);
            if (!extract.valid)
            {
                toolkitStatus.text = "No outline";
                return;
            }
            var union = extract.unionResolved
                ? extract.unionChanged ? "UNION: changed" : "UNION: no-op"
                : "UNION: bailed";
            toolkitStatus.text = $"g{extract.glyphId}  segs raw " +
                                 $"{extract.rawCount}/{extract.rawContours}c → union " +
                                 $"{extract.resCount}/{extract.resContours}c   {union}   " +
                                 $"zoom {viewScale:F0}";
            if (viewMode != ViewMode.Single) toolkitCursor.text = string.Empty;
        }

        private Vector2 ToolkitMap(Vector2 glyph)
        {
            return new Vector2(
                viewOffset.x + glyph.x * viewScale,
                viewOffset.y - glyph.y * viewScale);
        }

        private Vector2 ToolkitInvMap(Vector2 local)
        {
            return new Vector2(
                (local.x - viewOffset.x) / viewScale,
                (viewOffset.y - local.y) / viewScale);
        }

        private void PaintToolkitViewport(MeshGenerationContext context)
        {
            if (run.Length == 0) return;
            var extract = GetExtract(run[selected].glyphId);
            if (!extract.valid) return;
            var painter = context.painter2D;
            if (viewMode == ViewMode.Single) PaintToolkitSingle(painter, extract);
            else PaintToolkitLine(painter);
        }

        private void PaintToolkitSingle(Painter2D painter, GlyphExtract extract)
        {
            if (showMetrics) PaintToolkitMetrics(painter, extract);
            if (showRaw)
                PaintToolkitContours(painter, extract.raw, extract.rawEnds,
                    extract.rawContours, showResolved ? RawColor : new Color(0.7f, 0.75f, 0.8f),
                    showResolved ? 1.4f : 2f, ToolkitMap);
            if (showResolved && extract.res != extract.raw)
            {
                for (var contour = 0; contour < extract.resContours; contour++)
                    PaintToolkitContour(painter, extract.res, extract.resEnds, contour,
                        segColors[contour % segColors.Length], 2f, ToolkitMap);
            }
            else if (showResolved)
            {
                PaintToolkitContours(painter, extract.res, extract.resEnds,
                    extract.resContours, ResColor, 2f, ToolkitMap);
            }
            var active = showResolved ? extract.res : extract.raw;
            var count = showResolved ? extract.resCount : extract.rawCount;
            if (showControlPts) PaintToolkitControls(painter, active, count);
            if (showOnCurve) PaintToolkitPoints(painter, active, count);
            if (showDirection) PaintToolkitDirections(painter, active, count);
            if (showJunctions && extract.junctions != null)
                PaintToolkitJunctions(painter, extract.junctions);
        }

        private void PaintToolkitLine(Painter2D painter)
        {
            var baseline = ToolkitMap(Vector2.zero).y;
            PaintToolkitLine(painter, new Vector2(0f, baseline),
                new Vector2(toolkitCanvas.contentRect.width, baseline),
                new Color(0.3f, 0.9f, 0.4f, 0.3f), 1f);
            var penX = 0f;
            var penY = 0f;
            var rtl = ResolveDirection(scripts[scriptIdx].tag) == HB.DIRECTION_RTL;
            for (var i = 0; i < run.Length; i++)
            {
                var glyph = run[i];
                var extract = GetExtract(glyph.glyphId);
                if (!extract.valid)
                {
                    penX += rtl ? -glyph.xAdvance : glyph.xAdvance;
                    continue;
                }
                var upem = extract.upem;
                var offsetX = (penX + glyph.xOffset) / upem;
                var offsetY = (penY + glyph.yOffset) / upem;
                Vector2 MapDesign(Vector2 point) => ToolkitMap(new Vector2(
                    offsetX + point.x / upem, offsetY + point.y / upem));
                PaintToolkitContours(painter, extract.design, extract.designEnds,
                    extract.designContours, i == selected ? ResColor :
                    new Color(0.75f, 0.8f, 0.85f, 0.95f),
                    i == selected ? 2.2f : 1.3f, MapDesign);
                penX += rtl ? -glyph.xAdvance : glyph.xAdvance;
                penY += glyph.yAdvance;
            }
        }

        private void PaintToolkitMetrics(Painter2D painter, GlyphExtract extract)
        {
            var inverseHeight = 1f / extract.bboxH;
            var baseline = -extract.minY * inverseHeight;
            var origin = -extract.minX * inverseHeight;
            var advance = (extract.advance - extract.minX) * inverseHeight;
            PaintToolkitRect(painter, ToolkitMap(Vector2.zero),
                ToolkitMap(new Vector2(AspectOf(extract), 1f)),
                new Color(1f, 1f, 1f, 0.06f));
            PaintToolkitLine(painter,
                new Vector2(0f, ToolkitMap(new Vector2(0f, baseline)).y),
                new Vector2(toolkitCanvas.contentRect.width,
                    ToolkitMap(new Vector2(0f, baseline)).y),
                new Color(0.3f, 0.9f, 0.4f, 0.5f), 1f);
            PaintToolkitLine(painter,
                new Vector2(ToolkitMap(new Vector2(origin, 0f)).x, 0f),
                new Vector2(ToolkitMap(new Vector2(origin, 0f)).x,
                    toolkitCanvas.contentRect.height),
                new Color(0.9f, 0.9f, 0.3f, 0.4f), 1f);
            PaintToolkitLine(painter,
                new Vector2(ToolkitMap(new Vector2(advance, 0f)).x, 0f),
                new Vector2(ToolkitMap(new Vector2(advance, 0f)).x,
                    toolkitCanvas.contentRect.height),
                new Color(0.9f, 0.6f, 0.2f, 0.4f), 1f);
        }

        private static void PaintToolkitContours(Painter2D painter, DiagSegment[] segments,
            int[] ends, int count, Color color, float width, Func<Vector2, Vector2> map)
        {
            for (var contour = 0; contour < count; contour++)
                PaintToolkitContour(painter, segments, ends, contour, color, width, map);
        }

        private static void PaintToolkitContour(Painter2D painter, DiagSegment[] segments,
            int[] ends, int contour, Color color, float width, Func<Vector2, Vector2> map)
        {
            var start = contour == 0 ? 0 : ends[contour - 1] + 1;
            var end = ends[contour];
            if (end < start || end >= segments.Length) return;
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(map(new Vector2(segments[start].p0x, segments[start].p0y)));
            for (var i = start; i <= end; i++)
            {
                var segment = segments[i];
                painter.QuadraticCurveTo(
                    map(new Vector2(segment.p1x, segment.p1y)),
                    map(new Vector2(segment.p2x, segment.p2y)));
            }
            painter.Stroke();
        }

        private void PaintToolkitControls(Painter2D painter, DiagSegment[] source, int count)
        {
            var color = new Color(1f, 0.6f, 0.2f, 0.7f);
            for (var i = 0; i < count; i++)
            {
                var segment = source[i];
                if (segment.isDegenerate) continue;
                var control = ToolkitMap(new Vector2(segment.p1x, segment.p1y));
                PaintToolkitLine(painter,
                    ToolkitMap(new Vector2(segment.p0x, segment.p0y)), control, color, 1f);
                PaintToolkitLine(painter, control,
                    ToolkitMap(new Vector2(segment.p2x, segment.p2y)), color, 1f);
                PaintToolkitSquare(painter, control, 3f, new Color(1f, 0.6f, 0.2f));
            }
        }

        private void PaintToolkitPoints(Painter2D painter, DiagSegment[] source, int count)
        {
            for (var i = 0; i < count; i++)
                PaintToolkitSquare(painter,
                    ToolkitMap(new Vector2(source[i].p0x, source[i].p0y)),
                    2.5f, Color.white);
        }

        private void PaintToolkitDirections(Painter2D painter, DiagSegment[] source, int count)
        {
            var color = new Color(0.6f, 0.9f, 1f, 0.8f);
            for (var i = 0; i < count; i++)
            {
                var segment = source[i];
                var midpoint = new Vector2(
                    0.25f * segment.p0x + 0.5f * segment.p1x + 0.25f * segment.p2x,
                    0.25f * segment.p0y + 0.5f * segment.p1y + 0.25f * segment.p2y);
                var tangent = new Vector2(
                    2f * (segment.p2x - segment.p1x),
                    2f * (segment.p2y - segment.p1y));
                if (tangent.sqrMagnitude < 1e-9f) continue;
                tangent.Normalize();
                var start = ToolkitMap(midpoint);
                var direction = new Vector2(tangent.x, -tangent.y);
                var tip = start + direction * 9f;
                var normal = new Vector2(-direction.y, direction.x);
                PaintToolkitLine(painter, start, tip, color, 1.5f);
                PaintToolkitLine(painter, tip,
                    tip - direction * 4f + normal * 3f, color, 1.5f);
                PaintToolkitLine(painter, tip,
                    tip - direction * 4f - normal * 3f, color, 1.5f);
            }
        }

        private void PaintToolkitJunctions(Painter2D painter, JunctionInfo[] source)
        {
            for (var i = 0; i < source.Length; i++)
            {
                var junction = source[i];
                var color = junction.posError > 1e-3f
                    ? Color.magenta
                    : junction.angleDeg > 2f
                        ? new Color(1f, 0.4f, 0.4f)
                        : new Color(0.4f, 1f, 0.5f);
                PaintToolkitSquare(painter, ToolkitMap(junction.posA), 4f, color);
            }
        }

        private static void PaintToolkitLine(Painter2D painter, Vector2 start,
            Vector2 end, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(start);
            painter.LineTo(end);
            painter.Stroke();
        }

        private static void PaintToolkitSquare(Painter2D painter, Vector2 center,
            float halfSize, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - halfSize, center.y - halfSize));
            painter.LineTo(new Vector2(center.x + halfSize, center.y - halfSize));
            painter.LineTo(new Vector2(center.x + halfSize, center.y + halfSize));
            painter.LineTo(new Vector2(center.x - halfSize, center.y + halfSize));
            painter.ClosePath();
            painter.Fill();
        }

        private static void PaintToolkitRect(Painter2D painter, Vector2 first,
            Vector2 second, Color color)
        {
            var min = Vector2.Min(first, second);
            var max = Vector2.Max(first, second);
            painter.strokeColor = color;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(min);
            painter.LineTo(new Vector2(max.x, min.y));
            painter.LineTo(max);
            painter.LineTo(new Vector2(min.x, max.y));
            painter.ClosePath();
            painter.Stroke();
        }

        private void RefreshToolkitSidePanel()
        {
            if (toolkitSidePanel == null) return;
            while (toolkitSidePanel.childCount > 1)
                toolkitSidePanel.RemoveAt(1);
            switch (panel)
            {
                case Panel.Data:
                    toolkitSidePanel.Add(CreateToolkitDataPanel());
                    break;
                case Panel.Sdf:
                    toolkitSidePanel.Add(CreateToolkitSdfPanel());
                    break;
                case Panel.Union:
                    toolkitSidePanel.Add(CreateToolkitUnionPanel());
                    break;
            }
        }

        private VisualElement CreateToolkitDataPanel()
        {
            var root = InspectorVisuals.CreateStack();
            root.AddToClassList("unitext-glyph-diagnostic__panel");
            if (run.Length == 0)
            {
                root.Add(new HelpBox("Select a glyph.", HelpBoxMessageType.Info));
                return root;
            }
            if (string.IsNullOrEmpty(dataText))
            {
                var extract = GetExtract(run[selected].glyphId);
                if (extract.valid) BuildDataText(extract);
            }
            if (string.IsNullOrEmpty(dataText))
            {
                root.Add(new HelpBox("No outline.", HelpBoxMessageType.Info));
                return root;
            }
            var data = new InspectorTextArea
            {
                value = dataText,
                isReadOnly = true,
            };
            data.AddToClassList("unitext-glyph-diagnostic__data");
            root.Add(data);
            root.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = dataText)
                { text = "Copy" });
            return root;
        }

        private VisualElement CreateToolkitSdfPanel()
        {
            var root = InspectorVisuals.CreateScrollStack();
            root.AddToClassList("unitext-glyph-diagnostic__panel");
            var controls = InspectorVisuals.CreateRow();
            var tile = new SelectorField<string>("Tile", tileSizeLabels, tileSizeIdx);
            tile.RegisterValueChangedCallback(evt =>
            {
                tileSizeIdx = tile.Index;
                DropSelectedTextures();
                RefreshToolkitSidePanel();
            });
            controls.Add(tile);
            AddExclusiveChoicePair(controls, "Union", "Raw",
                () => sdfSourceRes == 0,
                union =>
                {
                    sdfSourceRes = union ? 0 : 1;
                    RefreshViewedGlyph();
                    RefreshToolkitDiagnostics();
                });
            root.Add(controls);
            InspectorVisuals.AlignFields(root);
            if (run.Length == 0 || segments == null || segmentCount == 0)
            {
                root.Add(new HelpBox("No outline.", HelpBoxMessageType.Info));
                return root;
            }
            if (sdfGray == null) GenerateBruteForceSdf();
            var first = CreateSdfRow(sdfGray, "SDF", sdfContour, "0.5 contour");
            var second = CreateSdfRow(sdfSeg, "closest seg", sdfWinding, "winding");
            root.Add(first);
            root.Add(second);
            return root;
        }

        private static VisualElement CreateSdfRow(Texture2D first, string firstLabel,
            Texture2D second, string secondLabel)
        {
            var row = InspectorVisuals.CreateRow();
            row.Add(CreateSdfImage(first, firstLabel));
            row.Add(CreateSdfImage(second, secondLabel));
            return row;
        }

        private static VisualElement CreateSdfImage(Texture2D texture, string caption)
        {
            var root = InspectorVisuals.CreateStack();
            root.AddToClassList("unitext-glyph-diagnostic__sdf-image");
            root.Add(new Label(caption));
            var image = new Image
            {
                image = texture,
                scaleMode = ScaleMode.ScaleToFit,
            };
            image.AddToClassList("unitext-glyph-diagnostic__sdf-texture");
            root.Add(image);
            return root;
        }

        private VisualElement CreateToolkitUnionPanel()
        {
            var root = InspectorVisuals.CreateScrollStack();
            root.AddToClassList("unitext-glyph-diagnostic__panel");
            if (run.Length == 0)
            {
                root.Add(new HelpBox("Select a glyph.", HelpBoxMessageType.Info));
                return root;
            }
            var extract = GetExtract(run[selected].glyphId);
            if (!extract.valid)
            {
                root.Add(new HelpBox("No outline.", HelpBoxMessageType.Info));
                return root;
            }
            root.Add(CreateToolkitHeading("ContourUnion — before / after"));
            root.Add(CreateStatRow("Status", extract.unionResolved
                ? extract.unionChanged ? "resolved · rewritten" : "resolved · no-op"
                : "BAILED"));
            root.Add(CreateStatRow("Info", extract.unionInfo ?? string.Empty));
            root.Add(CreateStatRow("Segments",
                $"{extract.rawCount} → {extract.resCount} " +
                $"({extract.resCount - extract.rawCount:+0;-0;0})"));
            root.Add(CreateStatRow("Contours",
                $"{extract.rawContours} → {extract.resContours} " +
                $"({extract.resContours - extract.rawContours:+0;-0;0})"));
            root.Add(CreateToolkitHeading("Global stats (last frame)"));
#if UNITEXT_DEBUG
            root.Add(CreateStatRow("changed", ContourUnionBurst.statChanged.ToString()));
            root.Add(CreateStatRow("bailed", ContourUnionBurst.statBailed.ToString()));
            root.Add(CreateStatRow("promoted", ContourUnionBurst.statPromoted.ToString()));
            root.Add(CreateStatRow("threw", ContourUnionBurst.statThrew.ToString()));
#else
            root.Add(new HelpBox(
                "Rasterizer-wide union counters are compiled out. Enable UNITEXT_DEBUG to collect them; " +
                "the per-glyph figures above are always live.",
                HelpBoxMessageType.Info));
#endif
            root.Add(new HelpBox(
                "RAW is the FreeType outline. UNION is the crossing-free geometry consumed by " +
                "the SDF rasterizer.",
                HelpBoxMessageType.None));
            return root;
        }

        private static VisualElement CreateStatRow(string key, string value)
        {
            var row = InspectorVisuals.CreateRow();
            var name = new Label(key);
            name.AddToClassList("unitext-glyph-diagnostic__stat-key");
            var content = new TextField { value = value, isReadOnly = true };
            content.AddToClassList("unitext-glyph-diagnostic__stat-value");
            row.Add(name);
            row.Add(content);
            return row;
        }

        private static Label CreateToolkitHeading(string value)
            => InspectorVisuals.CreateSubheading(value);
    }
}
