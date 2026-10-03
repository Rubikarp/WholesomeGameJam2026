var UniTextNativeInputPlugin = {

    $UniTextNativeInputState: {
        keyboardCallback: 0,
        keyboardListenerAttached: false,
        keyboardDispatch: null,
        keyboardThreshold: 100,
        lastVisible: false,
        lastKeyboardX: 0,
        lastKeyboardY: 0,
        lastKeyboardWidth: 0,
        lastKeyboardHeight: 0,
        referenceInnerHeight: 0,
        referenceInnerWidth: 0,

        nativeFieldElement: null,
        nativeFieldContext: null,
        presenterRegistry: null,
        systemPresenter: null,
        fieldEditCallback: 0,
        fieldCompositionCallback: 0,
        fieldSelectionCallback: 0,
        fieldActionCallback: 0,
        fieldQuiescedCallback: 0,
        inputFaultCallback: 0,

        hasPendingRect: false,
        pendingUnityX: 0,
        pendingUnityY: 0,
        pendingUnityW: 0,
        pendingUnityH: 0,

        transparentInputElement: null,
        transparentProducer: null,
        transparentSessionId: 0,
        transparentRevision: 0,
        transparentFrozen: false,
        transparentQuiesce: null,
        compositionCallback: 0,
        compositionEndedCallback: 0,
        textInputCallback: 0,
        textReplacementCallback: 0,
        keyDownCallback: 0,
        composingActive: false,
        cancelTransparentComposition: false,
        compositionText: '',
        compositionCursor: -1,
        caretUnityX: 0,
        caretUnityY: 0,
        caretLineHeight: 16,
        hasCaretGeometry: false,

        pendingPasteCode: -1,
        pendingPasteMods: 0,
        pasteFallbackTimer: 0,
        pasteFallbackAt: 0,

        lastPointerType: '',
        pointerTypeListener: null,
        // No backslash literals in DATA members: the WebGL link re-serializes this object
        // (functions pass through verbatim) and drops string escapes, producing an
        // unterminated-string SyntaxError. Backslash (code 33) is matched in code instead.
        letterCodes: {
            'a': 0, 'b': 1, 'c': 2, 'd': 3, 'e': 4, 'f': 5, 'h': 6, 'k': 7,
            'n': 8, 'p': 9, 't': 10, 'v': 11, 'x': 12, 'y': 13, 'z': 14,
            'i': 31, 'u': 32,
            'g': 34, 'j': 35, 'l': 36, 'm': 37, 'o': 38, 'q': 39, 'r': 40,
            's': 41, 'w': 42
        },

        appleDetected: undefined,
        isApple: function() {
            if (this.appleDetected === undefined) {
                var p = navigator.platform || '';
                if (!p && navigator.userAgentData) p = navigator.userAgentData.platform || '';
                this.appleDetected = /Mac|iPhone|iPad|iPod/i.test(p);
            }
            return this.appleDetected;
        },

        supportsNativeFieldOverlay: function() {
            var current = window.event;
            if (current && current.pointerType) this.lastPointerType = current.pointerType;
            var mobile = (navigator.userAgentData && navigator.userAgentData.mobile) ||
                /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent || '') ||
                (navigator.platform === 'MacIntel' && (navigator.maxTouchPoints || 0) > 1);
            if (mobile) return true;
            if (this.lastPointerType)
                return this.lastPointerType === 'touch' || this.lastPointerType === 'pen';
            var coarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
            var hover = window.matchMedia && window.matchMedia('(hover: hover)').matches;
            return (navigator.maxTouchPoints || 0) > 0 && coarse && !hover;
        },

        // Cached canvas element — queried per caret move / keyboard dispatch
        // otherwise; invalidated when a template swaps the canvas node out.
        canvasCache: null,
        unityCanvas: function() {
            var c = this.canvasCache;
            if (c && c.isConnected) return c;
            c = document.querySelector('#unity-canvas') || document.querySelector('canvas');
            this.canvasCache = c;
            return c;
        },

        // Unity-pixel → CSS-pixel scale. Unity's Screen dimensions equal the
        // canvas BACKING STORE, which templates may decouple from
        // devicePixelRatio (dpr caps) — always derive the scale from the canvas
        // itself; dpr is only the no-canvas fallback.
        unityToCssScaleX: function() {
            var canvas = this.unityCanvas();
            return (canvas && canvas.clientWidth > 0)
                ? canvas.width / canvas.clientWidth
                : (window.devicePixelRatio || 1);
        },

        unityToCssScaleY: function() {
            var canvas = this.unityCanvas();
            return (canvas && canvas.clientHeight > 0)
                ? canvas.height / canvas.clientHeight
                : (window.devicePixelRatio || 1);
        },

        // The one Unity-rect (Y-up bottom-left, backing-store px) -> CSS-viewport
        // rect (Y-down top-left, CSS px) projection. Both DOM overlays (native
        // field, transparent caret) position through here so the coordinate
        // convention cannot drift between them. Returns null without a canvas.
        unityToCssRect: function(unityX, unityY, unityW, unityH) {
            var canvas = this.unityCanvas();
            if (!canvas) return null;
            var scaleX = this.unityToCssScaleX();
            var scaleY = this.unityToCssScaleY();
            var rect = canvas.getBoundingClientRect();
            var cssW = unityW / scaleX;
            var cssH = unityH / scaleY;
            return {
                left: rect.left + unityX / scaleX,
                top: rect.top + canvas.clientHeight - (unityY / scaleY) - cssH,
                width: cssW,
                height: cssH
            };
        }
    },

    // ------------------------------------------------------------------------
    //  Keyboard area detection (VisualViewport)
    // ------------------------------------------------------------------------

    UniTextNativeInput_focusElement: function(element) {
        if (!element || !element.isConnected) return;
        try { element.focus({ preventScroll: true }); }
        catch (error) { try { element.focus(); } catch (ignored) { } }
    },

    UniTextNativeInput_RegisterKeyboardCallback__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_applyPendingRect',
        'UniTextNativeInput_applyCaretPos'
    ],
    UniTextNativeInput_RegisterKeyboardCallback: function(callbackPtr) {
        var state = UniTextNativeInputState;
        state.keyboardCallback = callbackPtr;
        if (state.keyboardListenerAttached) return;
        state.keyboardListenerAttached = true;
        state.referenceInnerHeight = window.innerHeight;
        state.referenceInnerWidth = window.innerWidth;

        var current = window.event;
        if (current && current.pointerType) state.lastPointerType = current.pointerType;
        state.pointerTypeListener = function(e) {
            state.lastPointerType = e.pointerType || '';
        };
        document.addEventListener('pointerdown', state.pointerTypeListener, true);

        var dispatch = function() {
            var st = UniTextNativeInputState;
            _UniTextNativeInput_applyPendingRect();
            _UniTextNativeInput_applyCaretPos();
            if (st.keyboardCallback === 0) return;

            var vv = window.visualViewport;
            var innerH = window.innerHeight;
            var innerW = window.innerWidth;

            var active = document.activeElement;
            var focused = active !== null &&
                (active === st.transparentInputElement || active === st.nativeFieldElement);

            // The software keyboard can only be up while one of our elements
            // holds focus, so the idle innerHeight is the keyboard-free
            // reference (survives orientation changes and window resizes).
            if (!focused || Math.abs(innerW - st.referenceInnerWidth) > 1) {
                st.referenceInnerHeight = innerH;
                st.referenceInnerWidth = innerW;
            } else if (innerH > st.referenceInnerHeight) {
                st.referenceInnerHeight = innerH;
            }

            // Two keyboard-height signals: resizes-visual browsers shrink only
            // the visual viewport (classic); resizes-content browsers (Firefox
            // Android, interactive-widget meta) shrink the layout viewport too,
            // so diff against the pre-keyboard reference — gated to focused +
            // touch so desktop window resizes never read as a keyboard.
            var touch = st.supportsNativeFieldOverlay();
            var visualBottom = vv ? vv.offsetTop + vv.height : innerH;
            var classic = (focused && touch && vv) ? innerH - visualBottom : 0;
            var legacy = (focused && touch) ? st.referenceInnerHeight - innerH : 0;
            var diffCss = Math.max(0, Math.max(classic, legacy));

            var visible = diffCss > st.keyboardThreshold;
            var keyboardX = 0;
            var keyboardY = 0;
            var keyboardWidth = 0;
            var keyboardHeight = 0;

            if (visible) {
                var canvas = st.unityCanvas();
                var keyboardTop = legacy > classic ? innerH : visualBottom;
                var keyboardBottom = keyboardTop + diffCss;
                if (canvas) {
                    var canvasRect = canvas.getBoundingClientRect();
                    var scaleX = st.unityToCssScaleX();
                    var scaleY = st.unityToCssScaleY();
                    keyboardX = -canvasRect.left * scaleX;
                    keyboardY = canvas.height - (keyboardBottom - canvasRect.top) * scaleY;
                    keyboardWidth = innerW * scaleX;
                    keyboardHeight = diffCss * scaleY;
                } else {
                    var fallbackScale = window.devicePixelRatio || 1;
                    keyboardWidth = innerW * fallbackScale;
                    keyboardHeight = diffCss * fallbackScale;
                }
            }

            var changed = (visible !== st.lastVisible) ||
                          Math.abs(keyboardX - st.lastKeyboardX) > 1 ||
                          Math.abs(keyboardY - st.lastKeyboardY) > 1 ||
                          Math.abs(keyboardWidth - st.lastKeyboardWidth) > 1 ||
                          Math.abs(keyboardHeight - st.lastKeyboardHeight) > 1;
            if (!changed) return;

            var wasVisible = st.lastVisible;
            st.lastVisible = visible;
            st.lastKeyboardX = keyboardX;
            st.lastKeyboardY = keyboardY;
            st.lastKeyboardWidth = keyboardWidth;
            st.lastKeyboardHeight = keyboardHeight;

            var duration = 0.0;
            var easing = 3;
            var frameSynced = 0;

            var cb = st.keyboardCallback;
            var emit = function(phase, x, y, width, height) {
                {{{ makeDynCall('vifffffifi', 'cb') }}}(
                    phase, x, y, width, height, duration, easing, 0.0, frameSynced);
            };

            if (visible && !wasVisible) {
                emit(0, keyboardX, keyboardY, keyboardWidth, keyboardHeight);
                emit(2, keyboardX, keyboardY, keyboardWidth, keyboardHeight);
            } else if (!visible && wasVisible) {
                emit(3, 0, 0, 0, 0);
                emit(4, 0, 0, 0, 0);
            } else if (visible) {
                emit(5, keyboardX, keyboardY, keyboardWidth, keyboardHeight);
            }
        };

        UniTextNativeInputState.keyboardDispatch = dispatch;
        if (window.visualViewport) {
            window.visualViewport.addEventListener('resize', dispatch);
            window.visualViewport.addEventListener('scroll', dispatch);
        }
        // Layout-viewport shrink (resizes-content browsers) only fires window
        // resize; attach both — the changed-check dedupes double dispatch.
        window.addEventListener('resize', dispatch);
        window.addEventListener('scroll', dispatch);
    },

    // ------------------------------------------------------------------------
    //  Native field callbacks (registered once at startup)
    // ------------------------------------------------------------------------

    UniTextNativeInput_RegisterNativeFieldCallbacks: function(
        edit, composition, selection, action, quiesced, fault
    ) {
        var st = UniTextNativeInputState;
        st.fieldEditCallback = edit;
        st.fieldCompositionCallback = composition;
        st.fieldSelectionCallback = selection;
        st.fieldActionCallback = action;
        st.fieldQuiescedCallback = quiesced;
        st.inputFaultCallback = fault;
    },

    UniTextNativeInput_emitQuiesced: function(sessionId, nativeRevision, authorityRevision, requestId) {
        var cb = UniTextNativeInputState.fieldQuiescedCallback;
        if (cb) {{{ makeDynCall('viiii', 'cb') }}}(
            sessionId, nativeRevision, authorityRevision, requestId);
    },

    UniTextNativeInput_failInput__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_hideNativeField',
        'UniTextNativeInput_HideKeyboard'
    ],
    UniTextNativeInput_failInput: function(context, transparentProducer, sessionId, error) {
        var st = UniTextNativeInputState;
        var nativeField = context && st.nativeFieldContext === context;
        var transparent = !context && transparentProducer &&
            st.transparentProducer === transparentProducer &&
            st.transparentSessionId === sessionId;
        if (!nativeField && !transparent) {
            if (window.console && typeof window.console.error === 'function') {
                try { window.console.error('UniText input failure followed a retired producer.', error); }
                catch (ignored) { }
            }
            return;
        }
        if (nativeField) {
            context.frozen = true;
            context.quiesce = null;
            _UniTextNativeInput_hideNativeField();
        } else {
            st.transparentFrozen = true;
            st.transparentQuiesce = null;
            _UniTextNativeInput_HideKeyboard();
        }
        var message = error && error.message
            ? ((error.name ? error.name + ': ' : '') + error.message)
            : String(error || 'The WebGL native input producer failed without detail.');
        var cb = st.inputFaultCallback;
        if (!cb) {
            if (window.console && typeof window.console.error === 'function') {
                try { window.console.error(message); } catch (ignored) { }
            }
            return;
        }
        var size = lengthBytesUTF8(message) + 1;
        var buffer = _malloc(size);
        stringToUTF8(message, buffer, size);
        try {
            {{{ makeDynCall('vii', 'cb') }}}(sessionId, buffer);
        } catch (reportError) {
            if (window.console && typeof window.console.error === 'function') {
                try { window.console.error('UniText input fault reporting failed.', reportError); }
                catch (ignored) { }
            }
        } finally {
            _free(buffer);
        }
    },

    UniTextNativeInput_advanceRevision__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_failInput'
    ],
    UniTextNativeInput_advanceRevision: function(context, transparentProducer, sessionId) {
        var st = UniTextNativeInputState;
        var revision = context ? context.nativeRevision : st.transparentRevision;
        if (revision < 2147483647) {
            revision++;
            if (context) context.nativeRevision = revision;
            else st.transparentRevision = revision;
            return revision;
        }
        _UniTextNativeInput_failInput(
            context, transparentProducer, sessionId,
            context
                ? 'The WebGL native field revision space is exhausted.'
                : 'The WebGL transparent input revision space is exhausted.');
        return 0;
    },

    UniTextNativeInput_finishFieldQuiesce__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_emitFieldEdit',
        'UniTextNativeInput_emitFieldComposition',
        'UniTextNativeInput_emitQuiesced',
        'UniTextNativeInput_layoutField',
        'UniTextNativeInput_applyDeferredFieldSettings',
        'UniTextNativeInput_advanceRevision'
    ],
    UniTextNativeInput_finishFieldQuiesce: function(context) {
        if (UniTextNativeInputState.nativeFieldContext !== context || !context.quiesce) return;
        var pending = context.quiesce;
        context.quiesce = null;
        if (context.composing && pending.disposition !== 0) {
            var base = context.compositionBaseValue || '';
            var replacementLength = context.element.value.length -
                (base.length - context.compositionLength);
            var valid = replacementLength >= 0 &&
                context.compositionStart + replacementLength <= context.element.value.length;
            if (!valid) throw new DOMException(
                'The native field composition no longer matches its source range.', 'InvalidStateError');
            context.cancelComposition = true;
            context.composing = false;
            context.pendingAction = '';
            if (pending.disposition === 1) {
                context.text = context.element.value;
                context.selectionStart = context.element.selectionStart || 0;
                context.selectionEnd = context.element.selectionEnd || 0;
                _UniTextNativeInput_layoutField(context);
                var replacement = context.element.value.slice(context.compositionStart,
                    context.compositionStart + replacementLength);
                if (!_UniTextNativeInput_emitFieldEdit(context, context.compositionStart,
                    context.compositionLength, replacement,
                    context.element.selectionStart || 0, context.element.selectionEnd || 0)) return;
                if (!_UniTextNativeInput_emitFieldComposition(context, 1, '', -1)) return;
            } else {
                if (pending.disposition === 2) {
                    context.suppress = true;
                    try {
                        context.element.value = base;
                        var start = Math.max(0, Math.min(base.length, context.compositionStart));
                        var end = Math.max(start, Math.min(base.length,
                            context.compositionStart + context.compositionLength));
                        context.element.setSelectionRange(start, end);
                        context.valueSnapshot = base;
                        context.text = base;
                        context.selectionStart = start;
                        context.selectionEnd = end;
                        _UniTextNativeInput_layoutField(context);
                    } finally {
                        context.suppress = false;
                    }
                }
                if (!_UniTextNativeInput_emitFieldComposition(context, 2, '', -1)) return;
            }
        }
        _UniTextNativeInput_applyDeferredFieldSettings(context);
        var revision = _UniTextNativeInput_advanceRevision(context, null, context.sessionId);
        if (revision)
            _UniTextNativeInput_emitQuiesced(context.sessionId, revision,
                context.authorityRevision, pending.requestId);
    },

    UniTextNativeInput_finishTransparentQuiesce__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_deliverString',
        'UniTextNativeInput_armSentinel',
        'UniTextNativeInput_emitQuiesced',
        'UniTextNativeInput_advanceRevision'
    ],
    UniTextNativeInput_finishTransparentQuiesce: function(producer) {
        var st = UniTextNativeInputState;
        var pending = st.transparentQuiesce;
        if (!pending || pending.sessionId !== st.transparentSessionId ||
            producer !== st.transparentProducer) return;
        st.transparentQuiesce = null;
        if (st.composingActive && pending.disposition !== 0) {
            var data = st.compositionText;
            st.cancelTransparentComposition = true;
            st.composingActive = false;
            st.compositionText = '';
            st.compositionCursor = -1;
            if (pending.disposition === 1)
                _UniTextNativeInput_deliverString(st.textInputCallback, data);
            var ended = st.compositionEndedCallback;
            if (ended) {{{ makeDynCall('v', 'ended') }}}();
            if (st.transparentInputElement)
                _UniTextNativeInput_armSentinel(st.transparentInputElement);
        }
        var revision = _UniTextNativeInput_advanceRevision(
            null, producer, st.transparentSessionId);
        if (revision)
            _UniTextNativeInput_emitQuiesced(
                st.transparentSessionId, revision, 1, pending.requestId);
    },

    UniTextNativeInput_QuiesceInput__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_finishFieldQuiesce',
        'UniTextNativeInput_finishTransparentQuiesce',
        'UniTextNativeInput_failInput'
    ],
    UniTextNativeInput_QuiesceInput: function(sessionId, disposition, requestId) {
        if (sessionId <= 0 || disposition < 0 || disposition > 2 || requestId <= 0)
            throw new RangeError('Invalid native input quiescence request.');
        var st = UniTextNativeInputState;
        var context = st.nativeFieldContext;
        if (context && context.sessionId === sessionId) {
            if (context.quiesce) throw new DOMException(
                'A native input quiescence request is already active.', 'InvalidStateError');
            context.frozen = true;
            context.quiesce = { disposition: disposition, requestId: requestId };
            Promise.resolve().then(function() {
                _UniTextNativeInput_finishFieldQuiesce(context);
            }).catch(function(error) {
                _UniTextNativeInput_failInput(context, null, sessionId, error);
            });
            return;
        }
        if (st.transparentSessionId !== sessionId)
            throw new DOMException('Native input session is not active.', 'InvalidStateError');
        if (st.transparentQuiesce) throw new DOMException(
            'A native input quiescence request is already active.', 'InvalidStateError');
        st.transparentFrozen = true;
        st.transparentQuiesce = {
            sessionId: sessionId,
            disposition: disposition,
            requestId: requestId
        };
        var producer = st.transparentProducer;
        Promise.resolve().then(function() {
            _UniTextNativeInput_finishTransparentQuiesce(producer);
        }).catch(function(error) {
            _UniTextNativeInput_failInput(null, producer, sessionId, error);
        });
    },

    UniTextNativeInput_AbortInput__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_hideNativeField',
        'UniTextNativeInput_HideKeyboard'
    ],
    UniTextNativeInput_AbortInput: function(sessionId) {
        var st = UniTextNativeInputState;
        var context = st.nativeFieldContext;
        if (context && context.sessionId === sessionId) {
            context.frozen = true;
            context.quiesce = null;
            _UniTextNativeInput_hideNativeField();
            return;
        }
        if (st.transparentSessionId !== sessionId) return;
        st.transparentFrozen = true;
        st.transparentQuiesce = null;
        st.cancelTransparentComposition = true;
        st.composingActive = false;
        _UniTextNativeInput_HideKeyboard();
    },

    UniTextNativeInput_ensurePresenters: function() {
        var st = UniTextNativeInputState;
        var registry = st.presenterRegistry;
        if (!registry) {
            var source = window.UniTextNativeFieldPresenters;
            if (source != null && typeof source !== 'object' && typeof source !== 'function')
                throw new TypeError('The native field presenter registry must be an object.');
            var target = Object.create(null);
            if (source) Object.defineProperties(target, Object.getOwnPropertyDescriptors(source));
            if (Object.prototype.hasOwnProperty.call(target, 'system'))
                throw new Error("The native field presenter id 'system' is reserved.");
            var guardActive = function(property) {
                var current = st.nativeFieldContext;
                if (current && typeof property === 'string' && current.presenterId === property)
                    throw new DOMException(
                        'An active native field presenter cannot be replaced or removed.',
                        'InvalidStateError');
            };
            registry = new Proxy(target, {
                set: function(registryTarget, property, next, receiver) {
                    guardActive(property);
                    return Reflect.set(registryTarget, property, next, receiver);
                },
                defineProperty: function(registryTarget, property, descriptor) {
                    guardActive(property);
                    return Reflect.defineProperty(registryTarget, property, descriptor);
                },
                deleteProperty: function(registryTarget, property) {
                    guardActive(property);
                    return Reflect.deleteProperty(registryTarget, property);
                }
            });
            try {
                Object.defineProperty(window, 'UniTextNativeFieldPresenters', {
                    value: registry,
                    writable: false,
                    configurable: false
                });
            } catch (error) {
                throw new TypeError(
                    'The native field presenter registry must permit controller installation.');
            }
            st.presenterRegistry = registry;
        }
        if (window.UniTextNativeFieldPresenters !== registry)
            throw new TypeError('The native field presenter registry was replaced.');
        var existing = Object.prototype.hasOwnProperty.call(registry, 'system')
            ? registry.system
            : null;
        if (existing && existing !== st.systemPresenter)
            throw new Error("The native field presenter id 'system' is reserved.");
        if (!existing) {
            var system = {
                create: function(context) {
                    var element = document.createElement(context.multiLine ? 'textarea' : 'input');
                    if (context.multiLine && !context.wraps) element.wrap = 'off';
                    element.style.position = 'fixed';
                    element.style.zIndex = '2147483647';
                    element.style.boxSizing = 'border-box';
                    element.style.margin = '0';
                    context.element = element;
                    context.root = element;
                },
                update: function(context) {
                    if (context.multiLine)
                        context.element.wrap = context.wraps ? 'soft' : 'off';
                },
                layout: function(context) {
                    var rect = context.rect;
                    if (!rect) return;
                    var root = context.root;
                    var left = rect.left + 'px';
                    var top = rect.top + 'px';
                    var width = rect.width + 'px';
                    var height = rect.height + 'px';
                    if (root.style.left !== left) root.style.left = left;
                    if (root.style.top !== top) root.style.top = top;
                    if (root.style.width !== width) root.style.width = width;
                    if (root.style.height !== height) root.style.height = height;
                },
                destroy: function(context) {
                    var root = context.root;
                    if (root && root.parentNode) root.parentNode.removeChild(root);
                }
            };
            Object.freeze(system);
            st.systemPresenter = system;
            Object.defineProperty(registry, 'system', {
                value: system,
                writable: false,
                configurable: false,
                enumerable: true
            });
        }
        return registry;
    },

    UniTextNativeInput_requireFieldHierarchy: function(context, connected, expectedElement, expectedRoot) {
        var element = context.element;
        var hierarchyChanged = false;
        if (expectedElement && (element !== expectedElement || context.root !== expectedRoot)) {
            context.element = expectedElement;
            context.root = expectedRoot;
            element = expectedElement;
            hierarchyChanged = true;
        }
        var hierarchy = context.hierarchy;
        if (expectedElement && hierarchy) {
            for (var i = hierarchy.length - 1; i >= 0; i--) {
                var child = hierarchy[i][0];
                var parent = hierarchy[i][1];
                if (child.parentNode !== parent) {
                    hierarchyChanged = true;
                    if (child.parentNode) child.parentNode.removeChild(child);
                    parent.appendChild(child);
                }
            }
        }
        if (hierarchyChanged)
            throw new TypeError('A native field presenter replaced its input hierarchy.');
        if (context.multiLine
                ? !(element instanceof HTMLTextAreaElement)
                : !(element instanceof HTMLInputElement))
            throw new TypeError('A native field presenter created an incompatible input element.');
        var root = context.root;
        if (!(root instanceof Node) || root === document.body || root === document.documentElement ||
            (root !== element && !root.contains(element)))
            throw new TypeError('A native field presenter root must own the input element.');
        if (connected && (!root.isConnected || !element.isConnected))
            throw new TypeError('A native field presenter detached its input element.');
    },

    UniTextNativeInput_captureFieldHierarchy: function(context) {
        var hierarchy = [];
        var child = context.element;
        while (child !== context.root) {
            hierarchy.push([child, child.parentNode]);
            child = child.parentNode;
        }
        hierarchy.push([context.root, context.root.parentNode]);
        context.hierarchy = hierarchy;
    },

    UniTextNativeInput_setFieldAttribute: function(element, name, value) {
        if (value) {
            if (element.getAttribute(name) !== value) element.setAttribute(name, value);
        } else if (element.hasAttribute(name)) {
            element.removeAttribute(name);
        }
    },

    UniTextNativeInput_trackFieldMutations: function(context) {
        var element = context.element;
        var prototype = context.multiLine
            ? HTMLTextAreaElement.prototype
            : HTMLInputElement.prototype;
        var trackProperty = function(name, epochName) {
            if (Object.prototype.hasOwnProperty.call(element, name))
                throw new TypeError('A native field input overrides a controlled property.');
            var descriptor = Object.getOwnPropertyDescriptor(prototype, name);
            if (!descriptor || typeof descriptor.get !== 'function' ||
                typeof descriptor.set !== 'function')
                throw new TypeError('A native field input lacks a controlled property.');
            Object.defineProperty(element, name, {
                enumerable: descriptor.enumerable,
                configurable: false,
                get: function() { return descriptor.get.call(element); },
                set: function(value) {
                    context[epochName]++;
                    descriptor.set.call(element, value);
                }
            });
        };
        trackProperty('value', 'valueMutationEpoch');
        trackProperty('selectionStart', 'selectionMutationEpoch');
        trackProperty('selectionEnd', 'selectionMutationEpoch');
        var trackMethod = function(name, valueEpoch, selectionEpoch) {
            var method = element[name];
            if (typeof method !== 'function') return;
            Object.defineProperty(element, name, {
                configurable: false,
                writable: false,
                value: function() {
                    if (valueEpoch) context.valueMutationEpoch++;
                    if (selectionEpoch) context.selectionMutationEpoch++;
                    return method.apply(element, arguments);
                }
            });
        };
        trackMethod('setRangeText', true, true);
        trackMethod('setSelectionRange', false, true);
        trackMethod('select', false, true);
    },

    UniTextNativeInput_configureField__deps: ['UniTextNativeInput_setFieldAttribute'],
    UniTextNativeInput_configureField: function(context, restoreContent) {
        var element = context.element;
        if (!context.multiLine) {
            var type = context.secureEntry ? 'password' : 'text';
            if (element.type !== type) {
                element.type = type;
                restoreContent = true;
            }
        }
        var placeholder = context.placeholder || '';
        var identifier = context.identifier || '';
        if (element.placeholder !== placeholder) element.placeholder = placeholder;
        if (element.id !== identifier) element.id = identifier;
        if (element.readOnly !== !!context.readOnly) element.readOnly = !!context.readOnly;
        _UniTextNativeInput_setFieldAttribute(element, 'inputmode', context.inputMode);
        _UniTextNativeInput_setFieldAttribute(element, 'enterkeyhint', context.enterKeyHint);
        _UniTextNativeInput_setFieldAttribute(element, 'autocapitalize', context.autocapitalize);
        _UniTextNativeInput_setFieldAttribute(element, 'autocorrect', context.autocorrect);
        _UniTextNativeInput_setFieldAttribute(element, 'spellcheck', context.spellcheck);
        _UniTextNativeInput_setFieldAttribute(element, 'autocomplete', context.autocomplete);
        if (restoreContent !== false) {
            if (element.value !== context.text) element.value = context.text;
            if (element.selectionStart !== context.selectionStart ||
                element.selectionEnd !== context.selectionEnd)
                element.setSelectionRange(context.selectionStart, context.selectionEnd);
        }
    },

    UniTextNativeInput_applyDeferredFieldSettings__deps: [
        'UniTextNativeInput_configureField'
    ],
    UniTextNativeInput_applyDeferredFieldSettings: function(context) {
        var settings = context.pendingSettings;
        if (!settings || context.composing) return;
        context.pendingSettings = null;
        Object.assign(context, settings);
        _UniTextNativeInput_configureField(context, false);
    },

    UniTextNativeInput_createPresenterContext__deps: [
        'UniTextNativeInput_performFieldAction'
    ],
    UniTextNativeInput_createPresenterContext: function(context) {
        var presenterContext = {};
        Object.defineProperties(presenterContext, {
            sessionId: { enumerable: true, get: function() { return context.sessionId; } },
            multiLine: { enumerable: true, get: function() { return context.multiLine; } },
            wraps: { enumerable: true, get: function() { return context.wraps; } },
            acceptsNewlines: {
                enumerable: true, get: function() { return context.acceptsNewlines; }
            },
            returnKey: { enumerable: true, get: function() { return context.returnKey; } },
            text: { enumerable: true, get: function() { return context.text; } },
            selectionStart: { enumerable: true, get: function() { return context.selectionStart; } },
            selectionEnd: { enumerable: true, get: function() { return context.selectionEnd; } },
            presenterId: { enumerable: true, get: function() { return context.presenterId; } },
            placeholder: { enumerable: true, get: function() { return context.placeholder; } },
            identifier: { enumerable: true, get: function() { return context.identifier; } },
            presenterData: { enumerable: true, get: function() { return context.presenterData; } },
            inputMode: { enumerable: true, get: function() { return context.inputMode; } },
            enterKeyHint: { enumerable: true, get: function() { return context.enterKeyHint; } },
            autocapitalize: { enumerable: true, get: function() { return context.autocapitalize; } },
            autocorrect: { enumerable: true, get: function() { return context.autocorrect; } },
            spellcheck: { enumerable: true, get: function() { return context.spellcheck; } },
            autocomplete: { enumerable: true, get: function() { return context.autocomplete; } },
            secureEntry: { enumerable: true, get: function() { return context.secureEntry; } },
            readOnly: { enumerable: true, get: function() { return context.readOnly; } },
            copyAllowed: { enumerable: true, get: function() { return context.copyAllowed; } },
            rect: { enumerable: true, get: function() { return context.rect; } },
            element: {
                enumerable: true,
                get: function() { return context.element; },
                set: function(value) { context.element = value; }
            },
            root: {
                enumerable: true,
                get: function() { return context.root; },
                set: function(value) { context.root = value; }
            },
            performAction: {
                enumerable: true,
                value: function(action) { _UniTextNativeInput_performFieldAction(context, action, 0); }
            }
        });
        return presenterContext;
    },

    UniTextNativeInput_layoutField__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_requireFieldHierarchy',
        'UniTextNativeInput_configureField'
    ],
    UniTextNativeInput_layoutField: function(context) {
        if (!context.rect) {
            _UniTextNativeInput_configureField(context, false);
            return;
        }
        if (context.presenter === UniTextNativeInputState.systemPresenter &&
            context.lastSystemLayoutRect === context.rect) {
            _UniTextNativeInput_configureField(context, false);
            return;
        }
        var element = context.element;
        var root = context.root;
        var valueMutationEpoch = context.valueMutationEpoch;
        var selectionMutationEpoch = context.selectionMutationEpoch;
        try {
            context.presenter.layout(context.presenterContext);
            _UniTextNativeInput_requireFieldHierarchy(context, true, element, root);
            if (context.presenter === UniTextNativeInputState.systemPresenter)
                context.lastSystemLayoutRect = context.rect;
        } finally {
            context.element = element;
            context.root = root;
            _UniTextNativeInput_configureField(context,
                valueMutationEpoch !== context.valueMutationEpoch ||
                selectionMutationEpoch !== context.selectionMutationEpoch);
        }
    },

    UniTextNativeInput_destroyField: function(context) {
        var element = context.element;
        var root = context.root;
        var presenter = context.presenter;
        var listeners = context.listeners || [];
        context.listeners = null;
        for (var i = 0; element && i < listeners.length; i++)
            element.removeEventListener(listeners[i][0], listeners[i][1]);
        if (element) {
            try { element.blur(); } catch (e) { }
        }
        var cleanupError = null;
        try {
            if (presenter && typeof presenter.destroy === 'function')
                presenter.destroy(context.presenterContext);
        } catch (error) {
            cleanupError = error;
        }
        try {
            if (root && root !== document.body && root !== document.documentElement && root.parentNode)
                root.parentNode.removeChild(root);
        } catch (error) {
            if (!cleanupError) cleanupError = error;
        }
        try {
            if (element && element !== root && element.parentNode)
                element.parentNode.removeChild(element);
        } catch (error) {
            if (!cleanupError) cleanupError = error;
        }
        if (cleanupError && window.console && typeof window.console.error === 'function') {
            try { window.console.error('UniText native field presenter cleanup failed.', cleanupError); }
            catch (ignored) { }
        }
    },

    UniTextNativeInput_isSupportedFieldAction: function(action) {
        switch (action) {
            case 'submit':
            case 'return':
            case 'done':
            case 'next':
            case 'previous':
            case 'cancel':
            case 'undo':
            case 'redo':
            case 'copy':
            case 'cut':
            case 'paste':
            case 'pastePlain':
                return true;
            default:
                return false;
        }
    },

    UniTextNativeInput_emitFieldEdit__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_advanceRevision'
    ],
    UniTextNativeInput_emitFieldEdit: function(context, start, length, text, selectionStart, selectionEnd) {
        var cb = UniTextNativeInputState.fieldEditCallback;
        if (!cb) return false;
        var revision = _UniTextNativeInput_advanceRevision(context, null, context.sessionId);
        if (!revision) return false;
        var size = lengthBytesUTF8(text) + 1;
        var buffer = _malloc(size);
        stringToUTF8(text, buffer, size);
        try {
            {{{ makeDynCall('viiiiiiii', 'cb') }}}(
                context.sessionId, revision, context.authorityRevision,
                start, length, buffer, selectionStart, selectionEnd);
        } finally {
            _free(buffer);
        }
        return UniTextNativeInputState.nativeFieldContext === context;
    },

    UniTextNativeInput_emitFieldComposition__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_advanceRevision'
    ],
    UniTextNativeInput_emitFieldComposition: function(context, phase, text, cursor) {
        var cb = UniTextNativeInputState.fieldCompositionCallback;
        if (!cb) return false;
        var revision = _UniTextNativeInput_advanceRevision(context, null, context.sessionId);
        if (!revision) return false;
        var size = lengthBytesUTF8(text) + 1;
        var buffer = _malloc(size);
        stringToUTF8(text, buffer, size);
        try {
            {{{ makeDynCall('viiiiiiii', 'cb') }}}(
                context.sessionId, revision, context.authorityRevision,
                phase, context.compositionStart, context.compositionLength, buffer, cursor);
        } finally {
            _free(buffer);
        }
        return UniTextNativeInputState.nativeFieldContext === context;
    },

    UniTextNativeInput_emitFieldSelection__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_advanceRevision'
    ],
    UniTextNativeInput_emitFieldSelection: function(context) {
        var cb = UniTextNativeInputState.fieldSelectionCallback;
        var element = context.element;
        if (!cb || element.selectionStart === null) return false;
        var revision = _UniTextNativeInput_advanceRevision(context, null, context.sessionId);
        if (!revision) return false;
        {{{ makeDynCall('viiiii', 'cb') }}}(
            context.sessionId, revision, context.authorityRevision,
            element.selectionStart, element.selectionEnd);
        return UniTextNativeInputState.nativeFieldContext === context;
    },

    UniTextNativeInput_emitFieldAction__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_advanceRevision'
    ],
    UniTextNativeInput_emitFieldAction: function(context, action, modifiers) {
        var cb = UniTextNativeInputState.fieldActionCallback;
        if (!cb) return false;
        var revision = _UniTextNativeInput_advanceRevision(context, null, context.sessionId);
        if (!revision) return false;
        var size = lengthBytesUTF8(action) + 1;
        var buffer = _malloc(size);
        stringToUTF8(action, buffer, size);
        try {
            {{{ makeDynCall('viiiii', 'cb') }}}(
                context.sessionId, revision, context.authorityRevision, buffer, modifiers | 0);
        } finally {
            _free(buffer);
        }
        return UniTextNativeInputState.nativeFieldContext === context;
    },

    /**
     * The editor action the return key stands for. A field that accepts line breaks always spends
     * the key on one, so it reports the plain key and lets the editor key bindings resolve it.
     */
    UniTextNativeInput_returnKeyAction: function(context) {
        if (context.acceptsNewlines) return 'return';
        switch (context.returnKey) {
            // Go, Search, Send and Done all commit the field.
            case 1: case 2: case 3: case 6: return 'submit';
            case 4: return 'next';
            case 5: return 'previous';
            default: return 'return';
        }
    },

    UniTextNativeInput_keyModifiers: function(event) {
        var modifiers = 0;
        if (event.shiftKey) modifiers |= 1;
        if (event.ctrlKey) modifiers |= 2;
        if (event.altKey) modifiers |= 4;
        if (event.metaKey) modifiers |= 8;
        return modifiers;
    },

    UniTextNativeInput_performFieldAction__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_emitFieldAction',
        'UniTextNativeInput_focusElement',
        'UniTextNativeInput_isSupportedFieldAction'
    ],
    UniTextNativeInput_performFieldAction: function(context, action, modifiers) {
        if (UniTextNativeInputState.nativeFieldContext !== context)
            throw new DOMException('The native field session has ended.', 'InvalidStateError');
        if (context.frozen)
            throw new DOMException('The native field session is quiescing.', 'InvalidStateError');
        if (typeof action !== 'string' || !action)
            throw new TypeError('A native field action is required.');
        if (!_UniTextNativeInput_isSupportedFieldAction(action))
            throw new TypeError("Unknown native field action '" + action + "'.");
        if (context.composing) {
            context.pendingAction = action;
            context.pendingActionModifiers = modifiers | 0;
            try { context.element.blur(); } finally { _UniTextNativeInput_focusElement(context.element); }
            return;
        }
        _UniTextNativeInput_emitFieldAction(context, action, modifiers);
    },

    UniTextNativeInput_diffFieldValue: function(before, after) {
        var start = 0;
        var beforeLength = before.length;
        var afterLength = after.length;
        while (start < beforeLength && start < afterLength && before.charCodeAt(start) === after.charCodeAt(start))
            start++;
        var beforeEnd = beforeLength;
        var afterEnd = afterLength;
        while (beforeEnd > start && afterEnd > start &&
               before.charCodeAt(beforeEnd - 1) === after.charCodeAt(afterEnd - 1)) {
            beforeEnd--;
            afterEnd--;
        }
        return { start: start, length: beforeEnd - start, text: after.slice(start, afterEnd) };
    },

    UniTextNativeInput_bindField__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_emitFieldEdit',
        'UniTextNativeInput_emitFieldComposition',
        'UniTextNativeInput_emitFieldSelection',
        'UniTextNativeInput_performFieldAction',
        'UniTextNativeInput_returnKeyAction',
        'UniTextNativeInput_keyModifiers',
        'UniTextNativeInput_diffFieldValue',
        'UniTextNativeInput_layoutField',
        'UniTextNativeInput_applyDeferredFieldSettings'
    ],
    UniTextNativeInput_bindField: function(context) {
        var element = context.element;
        var listeners = context.listeners = [];
        var add = function(name, handler) {
            element.addEventListener(name, handler);
            listeners.push([name, handler]);
        };
        add('beforeinput', function(event) {
            if (context.frozen) {
                event.preventDefault();
                return;
            }
            if (context.suppress || context.composing) return;
            var type = event.inputType || '';
            if (type === 'historyUndo' || type === 'historyRedo') {
                event.preventDefault();
                _UniTextNativeInput_performFieldAction(context,
                    type === 'historyUndo' ? 'undo' : 'redo', 0);
                return;
            }
            if (type === 'insertFromPaste' || type === 'insertFromPasteAsQuotation') {
                event.preventDefault();
                _UniTextNativeInput_performFieldAction(context,
                    context.pastePlain ? 'pastePlain' : 'paste', 0);
                context.pastePlain = false;
                return;
            }
            context.beforeInput = {
                start: element.selectionStart || 0,
                end: element.selectionEnd || 0,
                length: element.value.length,
                type: type
            };
        });
        add('input', function(event) {
            if (context.frozen) return;
            if (context.suppress || context.composing) return;
            if (context.ignoreCompositionInput) {
                context.ignoreCompositionInput = false;
                context.valueSnapshot = element.value;
                context.beforeInput = null;
                return;
            }
            var before = context.beforeInput;
            var change;
            if (!before) {
                change = _UniTextNativeInput_diffFieldValue(context.valueSnapshot, element.value);
            } else {
                var removed = before.end - before.start;
                var start = before.start;
                var inserted = element.value.length - before.length + removed;
                if (before.type.indexOf('delete') === 0 && before.start === before.end) {
                    removed = before.length - element.value.length;
                    inserted = 0;
                    if (before.type.indexOf('Backward') >= 0)
                        start = Math.max(0, before.start - removed);
                }
                if (inserted < 0 || removed < 0) {
                    change = _UniTextNativeInput_diffFieldValue(context.valueSnapshot, element.value);
                } else {
                    change = { start: start, length: removed, text: element.value.slice(start, start + inserted) };
                }
            }
            context.beforeInput = null;
            context.valueSnapshot = element.value;
            context.text = element.value;
            context.selectionStart = element.selectionStart || 0;
            context.selectionEnd = element.selectionEnd || 0;
            _UniTextNativeInput_layoutField(context);
            _UniTextNativeInput_emitFieldEdit(context, change.start, change.length, change.text,
                element.selectionStart || 0, element.selectionEnd || 0);
        });
        add('compositionstart', function() {
            if (context.frozen) return;
            context.cancelComposition = false;
            context.composing = true;
            context.compositionStart = element.selectionStart || 0;
            context.compositionLength = (element.selectionEnd || 0) - context.compositionStart;
            context.compositionBaseValue = context.valueSnapshot;
        });
        add('compositionupdate', function(event) {
            if (context.frozen) return;
            var text = event.data || '';
            var cursor = element.selectionStart === null
                ? text.length
                : Math.max(0, Math.min(text.length, element.selectionStart - context.compositionStart));
            context.text = element.value;
            context.selectionStart = element.selectionStart || 0;
            context.selectionEnd = element.selectionEnd || 0;
            _UniTextNativeInput_layoutField(context);
            _UniTextNativeInput_emitFieldComposition(context, 0, text, cursor);
        });
        add('compositionend', function(event) {
            if (context.cancelComposition) {
                context.cancelComposition = false;
                context.composing = false;
                context.ignoreCompositionInput = true;
                context.beforeInput = null;
                context.valueSnapshot = element.value;
                _UniTextNativeInput_applyDeferredFieldSettings(context);
                return;
            }
            if (context.frozen) return;
            var base = context.compositionBaseValue || '';
            var replacementLength = element.value.length - (base.length - context.compositionLength);
            var valid = replacementLength >= 0 &&
                context.compositionStart + replacementLength <= element.value.length;
            if (!valid) throw new DOMException(
                'The native field composition no longer matches its source range.', 'InvalidStateError');
            var replacement = element.value.slice(context.compositionStart,
                context.compositionStart + replacementLength);
            context.text = element.value;
            context.selectionStart = element.selectionStart || 0;
            context.selectionEnd = element.selectionEnd || 0;
            _UniTextNativeInput_layoutField(context);
            if (!_UniTextNativeInput_emitFieldEdit(context, context.compositionStart,
                context.compositionLength, replacement,
                element.selectionStart || 0, element.selectionEnd || 0)) return;
            if (!_UniTextNativeInput_emitFieldComposition(context, 1, '', -1)) return;
            context.composing = false;
            context.ignoreCompositionInput = true;
            context.valueSnapshot = element.value;
            _UniTextNativeInput_applyDeferredFieldSettings(context);
            Promise.resolve().then(function() {
                if (UniTextNativeInputState.nativeFieldContext === context)
                    context.ignoreCompositionInput = false;
            });
            var action = context.pendingAction;
            var pendingModifiers = context.pendingActionModifiers | 0;
            context.pendingAction = '';
            context.pendingActionModifiers = 0;
            if (action && UniTextNativeInputState.nativeFieldContext === context)
                _UniTextNativeInput_performFieldAction(context, action, pendingModifiers);
        });
        add('select', function() {
            if (context.frozen) return;
            if (!context.suppress && !context.composing && !context.beforeInput) {
                context.selectionStart = element.selectionStart || 0;
                context.selectionEnd = element.selectionEnd || 0;
                _UniTextNativeInput_emitFieldSelection(context);
            }
        });
        add('keydown', function(event) {
            if (context.frozen) {
                event.preventDefault();
                return;
            }
            if (event.isComposing || context.composing) return;
            if (event.key === 'Enter') {
                event.preventDefault();
                _UniTextNativeInput_performFieldAction(context,
                    _UniTextNativeInput_returnKeyAction(context),
                    _UniTextNativeInput_keyModifiers(event));
            } else if (event.key === 'Escape') {
                event.preventDefault();
                _UniTextNativeInput_performFieldAction(context, 'cancel', 0);
            } else if (event.key === 'Tab') {
                event.preventDefault();
                _UniTextNativeInput_performFieldAction(context,
                    event.shiftKey ? 'previous' : 'next', 0);
            } else if ((event.ctrlKey || event.metaKey) && event.shiftKey &&
                       String(event.key).toLowerCase() === 'v') {
                context.pastePlain = true;
            }
        });
        add('copy', function(event) {
            event.preventDefault();
            if (context.frozen) return;
            if (context.copyAllowed)
                _UniTextNativeInput_performFieldAction(context, 'copy', 0);
        });
        add('cut', function(event) {
            event.preventDefault();
            if (context.frozen) return;
            if (context.copyAllowed && !context.readOnly)
                _UniTextNativeInput_performFieldAction(context, 'cut', 0);
        });
        add('paste', function(event) {
            event.preventDefault();
            if (context.frozen) return;
            _UniTextNativeInput_performFieldAction(context,
                context.pastePlain ? 'pastePlain' : 'paste', 0);
            context.pastePlain = false;
        });
    },

    UniTextNativeInput_createField__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_ensurePresenters',
        'UniTextNativeInput_bindField',
        'UniTextNativeInput_createPresenterContext',
        'UniTextNativeInput_requireFieldHierarchy',
        'UniTextNativeInput_captureFieldHierarchy',
        'UniTextNativeInput_trackFieldMutations',
        'UniTextNativeInput_configureField',
        'UniTextNativeInput_layoutField',
        'UniTextNativeInput_destroyField'
    ],
    UniTextNativeInput_createField: function(context) {
        var registry = _UniTextNativeInput_ensurePresenters();
        var presenter = Object.prototype.hasOwnProperty.call(registry, context.presenterId)
            ? registry[context.presenterId]
            : null;
        if (!presenter) throw new Error("Unknown native field presenter '" + context.presenterId + "'.");
        ['create', 'update', 'layout', 'destroy'].forEach(function(name) {
            if (typeof presenter[name] !== 'function')
                throw new TypeError("Native field presenter '" + context.presenterId + "' requires " + name + "().");
        });
        context.presenter = presenter;
        context.element = null;
        context.root = null;
        context.lastSystemLayoutRect = null;
        context.presenterContext = _UniTextNativeInput_createPresenterContext(context);
        try {
            var result = presenter.create(context.presenterContext);
            if (result instanceof HTMLElement) context.element = result;
            else if (result) {
                if (result.element) context.element = result.element;
                if (result.root) context.root = result.root;
            }
            if (!context.root) context.root = context.element;
            _UniTextNativeInput_requireFieldHierarchy(context, false);
            if (!context.root.parentNode) document.body.appendChild(context.root);
            _UniTextNativeInput_requireFieldHierarchy(context, true);
            _UniTextNativeInput_captureFieldHierarchy(context);
            _UniTextNativeInput_trackFieldMutations(context);
            context.suppress = true;
            context.element.value = context.text;
            context.valueSnapshot = context.text;
            context.element.setSelectionRange(context.selectionStart, context.selectionEnd);
            var element = context.element;
            var root = context.root;
            presenter.update(context.presenterContext);
            _UniTextNativeInput_requireFieldHierarchy(context, true, element, root);
            _UniTextNativeInput_configureField(context);
            context.suppress = false;
            _UniTextNativeInput_bindField(context);
            _UniTextNativeInput_layoutField(context);
            return context;
        } catch (error) {
            context.suppress = false;
            _UniTextNativeInput_destroyField(context);
            throw error;
        }
    },

    UniTextNativeInput_updateExistingField__deps: [
        'UniTextNativeInput_requireFieldHierarchy',
        'UniTextNativeInput_configureField',
        'UniTextNativeInput_layoutField'
    ],
    UniTextNativeInput_updateExistingField: function(current, settings) {
        var element = current.element;
        var root = current.root;
        var value = element.value;
        var selectionStart = element.selectionStart || 0;
        var selectionEnd = element.selectionEnd || 0;
        var previous = {};
        Object.keys(settings).forEach(function(name) { previous[name] = current[name]; });
        var succeeded = false;
        current.suppress = true;
        try {
            Object.assign(current, settings);
            current.presenter.update(current.presenterContext);
            _UniTextNativeInput_requireFieldHierarchy(current, true, element, root);
            current.valueSnapshot = value;
            current.text = value;
            current.selectionStart = selectionStart;
            current.selectionEnd = selectionEnd;
            _UniTextNativeInput_configureField(current);
            _UniTextNativeInput_layoutField(current);
            succeeded = true;
        } finally {
            current.element = element;
            current.root = root;
            current.valueSnapshot = value;
            current.text = value;
            current.selectionStart = selectionStart;
            current.selectionEnd = selectionEnd;
            if (!succeeded) Object.assign(current, previous);
            _UniTextNativeInput_configureField(current);
            current.suppress = false;
        }
    },

    UniTextNativeInput_applyFieldUpdate__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_createField',
        'UniTextNativeInput_focusElement',
        'UniTextNativeInput_updateExistingField',
        'UniTextNativeInput_destroyField'
    ],
    UniTextNativeInput_applyFieldUpdate: function(current, settings) {
        var recreate = settings.presenterId !== current.presenterId ||
            settings.multiLine !== current.multiLine ||
            settings.returnKey !== current.returnKey;
        if (current.composing) {
            if (recreate)
                throw new DOMException(
                    'A native field presenter or element type cannot change during composition.',
                    'InvalidStateError');
            var immediate = Object.assign({}, settings, {
                inputMode: current.inputMode,
                enterKeyHint: current.enterKeyHint,
                autocapitalize: current.autocapitalize,
                autocorrect: current.autocorrect,
                spellcheck: current.spellcheck,
                autocomplete: current.autocomplete,
                secureEntry: current.secureEntry,
                readOnly: current.readOnly,
                copyAllowed: current.copyAllowed
            });
            _UniTextNativeInput_updateExistingField(current, immediate);
            current.pendingSettings = settings;
            return;
        }
        current.pendingSettings = null;
        if (!recreate) {
            _UniTextNativeInput_updateExistingField(current, settings);
            return;
        }
        var next = Object.assign({}, current, settings);
        next.text = current.element.value;
        next.selectionStart = current.element.selectionStart || 0;
        next.selectionEnd = current.element.selectionEnd || 0;
        next.listeners = null;
        _UniTextNativeInput_createField(next);
        UniTextNativeInputState.nativeFieldContext = next;
        UniTextNativeInputState.nativeFieldElement = next.element;
        _UniTextNativeInput_destroyField(current);
        _UniTextNativeInput_focusElement(next.element);
    },

    UniTextNativeInput_ShowNativeField__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_HideKeyboard',
        'UniTextNativeInput_createField',
        'UniTextNativeInput_destroyField',
        'UniTextNativeInput_focusElement'
    ],
    UniTextNativeInput_ShowNativeField: function(sessionId, authorityRevision, wraps,
        acceptsNewlines, returnKey,
        textPtr, selectionStart, selectionEnd, presenterIdPtr, placeholderPtr, identifierPtr,
        presenterDataPtr, inputModePtr, enterKeyHintPtr, autocapitalizePtr, autocorrectPtr,
        spellcheckPtr, autocompletePtr, secureEntry, readOnly, copyAllowed) {
        var st = UniTextNativeInputState;
        var multiLine = wraps || acceptsNewlines;
        if (sessionId <= 0 || authorityRevision <= 0 || selectionStart < 0 ||
            selectionEnd < selectionStart)
            throw new RangeError('Invalid native field open request.');
        if (multiLine && secureEntry)
            throw new TypeError('Web native fields do not support secure multiline input.');
        var text = UTF8ToString(textPtr) || '';
        var presenterId = presenterIdPtr ? UTF8ToString(presenterIdPtr) : '';
        if (!presenterId)
            throw new TypeError('A native field presenter id is required.');
        if (selectionEnd > text.length)
            throw new RangeError('The native field selection exceeds its text.');
        var previous = st.nativeFieldContext;
        if (previous && (!previous.frozen || previous.quiesce))
            throw new DOMException(
                'The current native field producer is not quiesced.', 'InvalidStateError');
        if (st.transparentSessionId > 0 &&
            (!st.transparentFrozen || st.transparentQuiesce))
            throw new DOMException(
                'The current transparent input producer is not quiesced.', 'InvalidStateError');
        var context = {
            sessionId: sessionId,
            nativeRevision: previous && previous.sessionId === sessionId
                ? previous.nativeRevision
                : 0,
            authorityRevision: authorityRevision,
            multiLine: !!multiLine,
            wraps: !!wraps,
            acceptsNewlines: !!acceptsNewlines,
            returnKey: returnKey | 0,
            text: text,
            selectionStart: selectionStart,
            selectionEnd: selectionEnd,
            presenterId: presenterId,
            placeholder: UTF8ToString(placeholderPtr) || '',
            identifier: UTF8ToString(identifierPtr) || '',
            presenterData: UTF8ToString(presenterDataPtr) || '',
            inputMode: UTF8ToString(inputModePtr) || '',
            enterKeyHint: UTF8ToString(enterKeyHintPtr) || '',
            autocapitalize: UTF8ToString(autocapitalizePtr) || '',
            autocorrect: UTF8ToString(autocorrectPtr) || '',
            spellcheck: UTF8ToString(spellcheckPtr) || '',
            autocomplete: UTF8ToString(autocompletePtr) || '',
            secureEntry: !!secureEntry,
            readOnly: !!readOnly,
            copyAllowed: !!copyAllowed,
            composing: false,
            cancelComposition: false,
            compositionStart: 0,
            compositionLength: 0,
            pendingAction: '',
            pastePlain: false,
            frozen: false,
            quiesce: null,
            pendingSettings: null,
            valueMutationEpoch: 0,
            selectionMutationEpoch: 0,
            rect: null
        };
        if (st.hasPendingRect)
            context.rect = st.unityToCssRect(
                st.pendingUnityX, st.pendingUnityY, st.pendingUnityW, st.pendingUnityH);
        _UniTextNativeInput_createField(context);
        _UniTextNativeInput_HideKeyboard(1);
        st.nativeFieldContext = context;
        st.nativeFieldElement = context.element;
        _UniTextNativeInput_focusElement(context.element);
    },

    UniTextNativeInput_hideNativeField__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_destroyField'
    ],
    UniTextNativeInput_hideNativeField: function() {
        var st = UniTextNativeInputState;
        var context = st.nativeFieldContext;
        if (!context) return;
        st.nativeFieldContext = null;
        st.nativeFieldElement = null;
        _UniTextNativeInput_destroyField(context);
    },

    UniTextNativeInput_CloseNativeField__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_hideNativeField'
    ],
    UniTextNativeInput_CloseNativeField: function(sessionId) {
        var context = UniTextNativeInputState.nativeFieldContext;
        if (!context || context.sessionId !== sessionId)
            throw new DOMException('The native field close session is not active.', 'InvalidStateError');
        if (!context.frozen || context.quiesce)
            throw new DOMException('The native field is not quiesced.', 'InvalidStateError');
        _UniTextNativeInput_hideNativeField();
    },

    UniTextNativeInput_FocusNativeField__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_requireFieldHierarchy',
        'UniTextNativeInput_focusElement'
    ],
    UniTextNativeInput_FocusNativeField: function(sessionId) {
        var context = UniTextNativeInputState.nativeFieldContext;
        if (!context || context.sessionId !== sessionId)
            throw new DOMException('The native field focus session is not active.', 'InvalidStateError');
        if (context.quiesce)
            throw new DOMException('The native field is still quiescing.', 'InvalidStateError');
        _UniTextNativeInput_requireFieldHierarchy(
            context, true, context.element, context.root);
        context.frozen = false;
        _UniTextNativeInput_focusElement(context.element);
    },

    // The transparent input is blurred but kept in the DOM (listeners and node
    // are reused across focus cycles); Shutdown removes it for good.
    UniTextNativeInput_HideKeyboard__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_hideNativeField'
    ],
    UniTextNativeInput_HideKeyboard: function() {
        _UniTextNativeInput_hideNativeField();

        var st = UniTextNativeInputState;
        st.transparentProducer = null;
        st.hasPendingRect = false;
        if (st.pasteFallbackTimer) {
            clearTimeout(st.pasteFallbackTimer);
            st.pasteFallbackTimer = 0;
        }

        var transparent = st.transparentInputElement;
        if (transparent) {
            st.composingActive = false;
            st.compositionText = '';
            st.compositionCursor = -1;
            // Blur BEFORE clearing value: blur lets the browser commit an
            // in-flight composition (compositionend delivers the text);
            // touching .value first aborts it and drops the user's input.
            try { transparent.blur(); } catch (e) { /* ignore */ }
            transparent.value = '';
        }
        st.transparentSessionId = 0;
        st.transparentRevision = 0;
        st.transparentFrozen = false;
        st.transparentQuiesce = null;
    },

    // Full teardown for Application.Quit / player re-instantiation on the same
    // page — dangling listeners would dynCall into a dead wasm module.
    UniTextNativeInput_Shutdown__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_HideKeyboard'
    ],
    UniTextNativeInput_Shutdown: function() {
        _UniTextNativeInput_HideKeyboard();

        var st = UniTextNativeInputState;
        var transparent = st.transparentInputElement;
        if (transparent) {
            st.transparentInputElement = null;
            if (transparent.parentNode) transparent.parentNode.removeChild(transparent);
        }

        var dispatch = st.keyboardDispatch;
        if (dispatch) {
            if (window.visualViewport) {
                window.visualViewport.removeEventListener('resize', dispatch);
                window.visualViewport.removeEventListener('scroll', dispatch);
            }
            window.removeEventListener('resize', dispatch);
            window.removeEventListener('scroll', dispatch);
            st.keyboardDispatch = null;
            st.keyboardListenerAttached = false;
        }

        if (st.pointerTypeListener) {
            document.removeEventListener('pointerdown', st.pointerTypeListener, true);
            st.pointerTypeListener = null;
        }

        st.keyboardCallback = 0;
        st.fieldEditCallback = 0;
        st.fieldCompositionCallback = 0;
        st.fieldSelectionCallback = 0;
        st.fieldActionCallback = 0;
        st.fieldQuiescedCallback = 0;
        st.inputFaultCallback = 0;
        st.compositionCallback = 0;
        st.compositionEndedCallback = 0;
        st.textInputCallback = 0;
        st.textReplacementCallback = 0;
        st.keyDownCallback = 0;
        st.transparentProducer = null;
        st.lastVisible = false;
        st.lastKeyboardX = 0;
        st.lastKeyboardY = 0;
        st.lastKeyboardWidth = 0;
        st.lastKeyboardHeight = 0;
        st.lastPointerType = '';
    },

    UniTextNativeInput_ReconcileNativeField__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_requireFieldHierarchy',
        'UniTextNativeInput_configureField',
        'UniTextNativeInput_layoutField'
    ],
    UniTextNativeInput_ReconcileNativeField: function(sessionId, sourceNativeRevision,
        authorityRevision, textPtr, selectionStart, selectionEnd) {
        var context = UniTextNativeInputState.nativeFieldContext;
        if (!context || context.sessionId !== sessionId)
            throw new DOMException(
                'The native field reconciliation session is not active.', 'InvalidStateError');
        if (sourceNativeRevision >= 0 && sourceNativeRevision !== context.nativeRevision)
            throw new Error('Native field reconciliation revision mismatch.');
        if (authorityRevision < context.authorityRevision)
            throw new Error('Native field reconciliation authority is stale.');
        var replaceText = !!textPtr;
        var previousText = context.text;
        var element = context.element;
        var root = context.root;
        var replacementText = replaceText ? (UTF8ToString(textPtr) || '') : null;
        var targetLength = replaceText ? replacementText.length : element.value.length;
        if (sourceNativeRevision < -1 || authorityRevision <= 0 || selectionStart < 0 ||
            selectionEnd < selectionStart || selectionEnd > targetLength)
            throw new RangeError('Invalid native field reconciliation state.');
        context.suppress = true;
        var configured = false;
        try {
            if (replaceText) element.value = replacementText;
            element.setSelectionRange(selectionStart, selectionEnd);
            context.authorityRevision = authorityRevision;
            context.valueSnapshot = element.value;
            context.text = element.value;
            context.selectionStart = selectionStart;
            context.selectionEnd = selectionEnd;
            if (sourceNativeRevision < 0) {
                context.composing = false;
                context.beforeInput = null;
            }
            if (replaceText) context.presenter.update(context.presenterContext);
            if (replaceText) {
                _UniTextNativeInput_requireFieldHierarchy(context, true, element, root);
                _UniTextNativeInput_configureField(context);
                configured = true;
            }
            if (context.text !== previousText) _UniTextNativeInput_layoutField(context);
        } finally {
            context.element = element;
            context.root = root;
            if (replaceText && !configured) _UniTextNativeInput_configureField(context);
            context.suppress = false;
        }
    },

    UniTextNativeInput_UpdateNativeField__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_applyFieldUpdate'
    ],
    UniTextNativeInput_UpdateNativeField: function(sessionId, authorityRevision, wraps,
        acceptsNewlines, returnKey,
        presenterIdPtr, placeholderPtr, identifierPtr, presenterDataPtr,
        inputModePtr, enterKeyHintPtr, autocapitalizePtr, autocorrectPtr,
        spellcheckPtr, autocompletePtr, secureEntry, readOnly, copyAllowed) {
        var st = UniTextNativeInputState;
        var multiLine = wraps || acceptsNewlines;
        var current = st.nativeFieldContext;
        if (!current || current.sessionId !== sessionId)
            throw new DOMException(
                'The native field update session is not active.', 'InvalidStateError');
        if (authorityRevision < current.authorityRevision)
            throw new Error('Native field update authority is stale.');
        if (multiLine && secureEntry)
            throw new TypeError('Web native fields do not support secure multiline input.');
        var presenterId = presenterIdPtr ? UTF8ToString(presenterIdPtr) : '';
        if (!presenterId)
            throw new TypeError('A native field presenter id is required.');
        _UniTextNativeInput_applyFieldUpdate(current, {
            authorityRevision: authorityRevision,
            multiLine: !!multiLine,
            wraps: !!wraps,
            acceptsNewlines: !!acceptsNewlines,
            returnKey: returnKey | 0,
            presenterId: presenterId,
            placeholder: UTF8ToString(placeholderPtr) || '',
            identifier: UTF8ToString(identifierPtr) || '',
            presenterData: UTF8ToString(presenterDataPtr) || '',
            inputMode: UTF8ToString(inputModePtr) || '',
            enterKeyHint: UTF8ToString(enterKeyHintPtr) || '',
            autocapitalize: UTF8ToString(autocapitalizePtr) || '',
            autocorrect: UTF8ToString(autocorrectPtr) || '',
            spellcheck: UTF8ToString(spellcheckPtr) || '',
            autocomplete: UTF8ToString(autocompletePtr) || '',
            secureEntry: !!secureEntry,
            readOnly: !!readOnly,
            copyAllowed: !!copyAllowed
        });
    },

    // ------------------------------------------------------------------------
    //  Overlay positioning
    // ------------------------------------------------------------------------

    UniTextNativeInput_SetInputFieldRect__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_applyPendingRect'
    ],
    UniTextNativeInput_SetInputFieldRect: function(unityX, unityY, unityW, unityH) {
        var st = UniTextNativeInputState;
        if (st.hasPendingRect && st.pendingUnityX === unityX && st.pendingUnityY === unityY &&
            st.pendingUnityW === unityW && st.pendingUnityH === unityH) return;
        st.pendingUnityX = unityX;
        st.pendingUnityY = unityY;
        st.pendingUnityW = unityW;
        st.pendingUnityH = unityH;
        st.hasPendingRect = true;
        _UniTextNativeInput_applyPendingRect();
    },

    UniTextNativeInput_applyPendingRect__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_layoutField'
    ],
    UniTextNativeInput_applyPendingRect: function() {
        if (!UniTextNativeInputState.hasPendingRect) return;
        var context = UniTextNativeInputState.nativeFieldContext;
        if (!context) return;

        var css = UniTextNativeInputState.unityToCssRect(
            UniTextNativeInputState.pendingUnityX, UniTextNativeInputState.pendingUnityY,
            UniTextNativeInputState.pendingUnityW, UniTextNativeInputState.pendingUnityH);
        if (!css) return;

        var previous = context.rect;
        if (previous && previous.left === css.left && previous.top === css.top &&
            previous.width === css.width && previous.height === css.height) return;

        context.rect = css;
        _UniTextNativeInput_layoutField(context);
    },

    // ------------------------------------------------------------------------
    //  Transparent-keyboard mode: hidden input bridging IME composition
    //  + keydown to C# (D-013 fallback path, works in all browsers).
    // ------------------------------------------------------------------------

    UniTextNativeInput_RegisterCompositionCallbacks: function(
        compositionPtr, compositionEndedPtr, textInputPtr, textReplacementPtr, keyDownPtr
    ) {
        UniTextNativeInputState.compositionCallback      = compositionPtr;
        UniTextNativeInputState.compositionEndedCallback = compositionEndedPtr;
        UniTextNativeInputState.textInputCallback        = textInputPtr;
        UniTextNativeInputState.textReplacementCallback  = textReplacementPtr;
        UniTextNativeInputState.keyDownCallback          = keyDownPtr;
    },

    UniTextNativeInput_IsApplePlatform__deps: ['$UniTextNativeInputState'],
    UniTextNativeInput_IsApplePlatform: function() {
        return UniTextNativeInputState.isApple() ? 1 : 0;
    },

    // Maps a keydown to { code, mods, prevent, paste } or null (key flows to
    // the browser untouched). Contract C15:
    //  - printables are mapped ONLY when Ctrl or Meta is held — bare and
    //    Shift-only letters must reach the 'input' event (uppercase typing);
    //  - AltGr never maps: real AltGraph state where the browser reports it,
    //    plus the Ctrl+Alt aliasing Windows browsers use for AltGr, so
    //    European-layout printables (ą, €, …) type text instead of shortcuts;
    //  - primary-modifier letters belong to the active editor, including
    //    custom behavior bindings, so the embedding page never also handles them.
    UniTextNativeInput_keyEventToNative__deps: ['$UniTextNativeInputState'],
    UniTextNativeInput_keyEventToNative: function(e) {
        var st = UniTextNativeInputState;
        var mods = 0;
        if (e.shiftKey) mods |= 1;
        if (e.ctrlKey)  mods |= 2;
        if (e.altKey)   mods |= 4;
        if (e.metaKey)  mods |= 8;

        var k = e.key;
        var code = -1;
        switch (k) {
            case 'ArrowLeft':  code = 15; break;
            case 'ArrowRight': code = 16; break;
            case 'ArrowUp':    code = 17; break;
            case 'ArrowDown':  code = 18; break;
            case 'Home':       code = 19; break;
            case 'End':        code = 20; break;
            case 'PageUp':     code = 21; break;
            case 'PageDown':   code = 22; break;
            case 'Backspace':  code = 23; break;
            case 'Delete':     code = 24; break;
            case 'Enter':      code = (e.location === 3) ? 26 : 25; break; // KeypadEnter vs Return
            case 'Tab':        code = 27; break;
            case 'Escape':     code = 28; break;
        }
        if (code >= 0) return { code: code, mods: mods, prevent: true, paste: false };

        if (k === 'Insert') {
            // Shift+Insert is the legacy paste chord — same deferred-paste
            // treatment as Ctrl+V; Ctrl+Insert = copy.
            if (e.shiftKey && !e.ctrlKey && !e.metaKey && !e.altKey)
                return { code: 29, mods: mods, prevent: false, paste: true };
            return { code: 29, mods: mods, prevent: e.ctrlKey && !e.altKey, paste: false };
        }

        if (!(e.ctrlKey || e.metaKey)) return null;
        if (e.getModifierState && e.getModifierState('AltGraph')) return null;
        if (e.ctrlKey && e.altKey && !e.metaKey) return null;

        var physicalCode = e.code || '';
        var lower;
        if (physicalCode.length === 4 && physicalCode.indexOf('Key') === 0)
            lower = physicalCode.charAt(3).toLowerCase();
        else if (physicalCode === 'Backslash')
            lower = String.fromCharCode(92);
        else {
            if (k.length !== 1) return null;
            lower = k.toLowerCase();
        }
        var letter = lower === String.fromCharCode(92) ? 33 : st.letterCodes[lower];
        if (letter === undefined) return null;

        var apple = st.isApple();
        var action = apple ? (e.metaKey && !e.altKey) : (e.ctrlKey && !e.altKey);

        if (lower === 'v' && action)
            return { code: 11, mods: mods, prevent: false, paste: true };

        var prevent = action;
        if (!action && apple && e.ctrlKey && !e.metaKey) {
            // NSText Emacs bindings — system-wide on macOS, expected in fields.
            if ('aefbpnktdh'.indexOf(lower) !== -1) prevent = true;
        }
        return { code: letter, mods: mods, prevent: prevent, paste: false };
    },

    UniTextNativeInput_deliverString__deps: ['$UniTextNativeInputState'],
    UniTextNativeInput_deliverString: function(cbPtr, str) {
        if (cbPtr === 0 || str == null) return;
        var bufSize = lengthBytesUTF8(str) + 1;
        var buf = _malloc(bufSize);
        stringToUTF8(str, buf, bufSize);
        try {
            {{{ makeDynCall('vi', 'cbPtr') }}}(buf);
        } finally {
            _free(buf);
        }
    },

    UniTextNativeInput_deliverReplacement: function(cbPtr, direction, str) {
        if (cbPtr === 0 || str == null) return;
        var bufSize = lengthBytesUTF8(str) + 1;
        var buffer = _malloc(bufSize);
        stringToUTF8(str, buffer, bufSize);
        try {
            {{{ makeDynCall('vii', 'cbPtr') }}}(direction, buffer);
        } finally {
            _free(buffer);
        }
    },

    // Fires when the paste combo produced no DOM 'paste' event within a frame
    // (permission-blocked or browser quirk). Deliver the pending combo anyway —
    // the C# side falls back to the async clipboard read (still inside the
    // user-activation window of the key gesture).
    UniTextNativeInput_flushPendingPaste__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_failInput'
    ],
    UniTextNativeInput_flushPendingPaste: function() {
        var st = UniTextNativeInputState;
        var producer = st.transparentProducer;
        st.pasteFallbackTimer = 0;
        st.pasteFallbackAt = performance.now();
        var cb = st.keyDownCallback;
        if (!producer || cb === 0) return;
        try {
            {{{ makeDynCall('vii', 'cb') }}}(st.pendingPasteCode, st.pendingPasteMods);
        } catch (error) {
            _UniTextNativeInput_failInput(null, producer, producer.sessionId, error);
        }
    },

    // One-char sentinel (ZWSP) kept in the transparent input so keyCode-229
    // keyboards (GBoard-class) that express backspace ONLY through
    // input/deleteContentBackward events have something to delete — a
    // permanently empty element produces no deletion event at all. Re-armed
    // after every delivered event; caret parked after the sentinel.
    UniTextNativeInput_armSentinel__deps: ['$UniTextNativeInputState'],
    UniTextNativeInput_armSentinel: function(el) {
        el.value = '​';
        try { el.setSelectionRange(1, 1); } catch (e) { /* ignore */ }
    },

    UniTextNativeInput_emitComposition__deps: ['$UniTextNativeInputState'],
    UniTextNativeInput_emitComposition: function(el) {
        var st = UniTextNativeInputState;
        var cb = st.compositionCallback;
        if (cb === 0 || !st.composingActive) return;

        var data = st.compositionText;
        var cursor = data.length;
        var value = el.value || '';
        var compositionStart = value.indexOf(data);
        if (compositionStart >= 0 && el.selectionStart !== null) {
            cursor = Math.max(0, Math.min(data.length, el.selectionStart - compositionStart));
        }
        if (cursor === st.compositionCursor) return;
        st.compositionCursor = cursor;

        var bufSize = lengthBytesUTF8(data) + 1;
        var buf = _malloc(bufSize);
        stringToUTF8(data, buf, bufSize);
        try {
            {{{ makeDynCall('viii', 'cb') }}}(buf, data.length, cursor);
        } finally {
            _free(buf);
        }
    },

    UniTextNativeInput_applyCaretPos__deps: ['$UniTextNativeInputState'],
    UniTextNativeInput_applyCaretPos: function() {
        var el = UniTextNativeInputState.transparentInputElement;
        if (!el) return;
        var css = UniTextNativeInputState.unityToCssRect(
            UniTextNativeInputState.caretUnityX, UniTextNativeInputState.caretUnityY,
            0, UniTextNativeInputState.caretLineHeight);
        if (!css) return;

        var left = css.left + 'px';
        var top = css.top + 'px';
        var height = css.height + 'px';
        if (el.style.left !== left) el.style.left = left;
        if (el.style.top !== top) el.style.top = top;
        if (el.style.height !== height) el.style.height = height;
    },

    UniTextNativeInput_ShowTransparentInput__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_hideNativeField',
        'UniTextNativeInput_keyEventToNative',
        'UniTextNativeInput_deliverString',
        'UniTextNativeInput_deliverReplacement',
        'UniTextNativeInput_flushPendingPaste',
        'UniTextNativeInput_failInput',
        'UniTextNativeInput_applyCaretPos',
        'UniTextNativeInput_armSentinel',
        'UniTextNativeInput_emitComposition',
        'UniTextNativeInput_focusElement'
    ],
    UniTextNativeInput_ShowTransparentInput: function(sessionId, secureEntry, enterKeyHintPtr,
        showSoftwareKeyboard) {
        var st = UniTextNativeInputState;
        if (sessionId <= 0)
            throw new RangeError('Invalid transparent input session.');
        var previousField = st.nativeFieldContext;
        if (previousField && (!previousField.frozen || previousField.quiesce))
            throw new DOMException(
                'The current native field producer is not quiesced.', 'InvalidStateError');
        if (st.transparentSessionId > 0 &&
            (!st.transparentFrozen || st.transparentQuiesce))
            throw new DOMException(
                'The current transparent input producer is not quiesced.', 'InvalidStateError');
        var el = st.transparentInputElement;
        if (el === null) {
            el = document.createElement('input');
            el.setAttribute('autocomplete', 'off');
            el.setAttribute('autocorrect', 'off');
            el.setAttribute('autocapitalize', 'off');
            el.setAttribute('spellcheck', 'false');
            // Transparent input is an IME / keyboard delivery vehicle, not the
            // text the user sees — UniText draws the actual content on the
            // Unity canvas. Hide from assistive tech to prevent screen readers
            // from announcing an empty input next to the visible canvas text.
            // Accessibility-critical fields should set useNativeField=true so
            // the visible <input>/<textarea> path is taken, where the browser
            // owns rendering and AT integration is automatic.
            el.setAttribute('aria-hidden', 'true');
            el.setAttribute('tabindex', '-1');

            el.style.position = 'fixed';
            el.style.opacity = '0';
            el.style.zIndex = '2147483647';
            el.style.width = '1px';
            el.style.padding = '0';
            el.style.border = '0';
            el.style.margin = '0';
            el.style.outline = 'none';
            // Avoid iOS Safari page-zoom on focus.
            el.style.fontSize = '16px';
            // Pass-through pointer events: clicks land on the Unity canvas, not
            // here. The element receives focus programmatically.
            el.style.pointerEvents = 'none';

            el.addEventListener('compositionstart', function(e) {
                var st = UniTextNativeInputState;
                if (st.transparentFrozen) return;
                st.cancelTransparentComposition = false;
                st.composingActive = true;
                st.compositionText = '';
                st.compositionCursor = -1;
                _UniTextNativeInput_emitComposition(el);
            });

            el.addEventListener('compositionupdate', function(e) {
                var st = UniTextNativeInputState;
                if (st.transparentFrozen) return;
                st.compositionText = e.data || '';
                st.compositionCursor = -1;
                _UniTextNativeInput_emitComposition(el);
                var producer = st.transparentProducer;
                setTimeout(function() {
                    if (!producer || UniTextNativeInputState.transparentProducer !== producer) return;
                    try {
                        _UniTextNativeInput_emitComposition(el);
                    } catch (error) {
                        _UniTextNativeInput_failInput(
                            null, producer, producer.sessionId, error);
                    }
                }, 0);
            });

            el.addEventListener('compositionend', function(e) {
                if (UniTextNativeInputState.cancelTransparentComposition) {
                    UniTextNativeInputState.cancelTransparentComposition = false;
                    _UniTextNativeInput_armSentinel(el);
                    return;
                }
                if (UniTextNativeInputState.transparentFrozen) return;
                UniTextNativeInputState.composingActive = false;
                UniTextNativeInputState.compositionText = '';
                UniTextNativeInputState.compositionCursor = -1;

                var data = e.data || '';
                _UniTextNativeInput_deliverString(
                    UniTextNativeInputState.textInputCallback, data);
                var endCb = UniTextNativeInputState.compositionEndedCallback;
                if (endCb !== 0) {{{ makeDynCall('v', 'endCb') }}}();
                _UniTextNativeInput_armSentinel(el);
            });

            el.addEventListener('input', function(e) {
                if (UniTextNativeInputState.transparentFrozen) {
                    _UniTextNativeInput_armSentinel(el);
                    return;
                }
                if (UniTextNativeInputState.composingActive) return;
                if (e.isComposing) return;
                // Firefox fires a trailing 'input' AFTER compositionend with
                // isComposing=false and the committed string — compositionend
                // already delivered it; letting this through commits twice.
                var t = e.inputType;
                if (t === 'insertCompositionText' || t === 'insertFromComposition') return;

                if (t && t.indexOf('delete') === 0) {
                    _UniTextNativeInput_armSentinel(el);
                    _UniTextNativeInput_deliverReplacement(
                        UniTextNativeInputState.textReplacementCallback,
                        t.indexOf('Forward') >= 0 ? 1 : -1, '');
                    return;
                }

                var data = e.data;
                if (!data || data.length === 0) {
                    // data:null inserts (some 229 keyboards) — recover the
                    // committed text from the element value, minus the sentinel.
                    data = (el.value || '').split('​').join('');
                }
                _UniTextNativeInput_armSentinel(el);
                if (data && data.length > 0) {
                    _UniTextNativeInput_deliverReplacement(
                        UniTextNativeInputState.textReplacementCallback, 0, data);
                }
            });

            el.addEventListener('keydown', function(e) {
                if (UniTextNativeInputState.transparentFrozen) {
                    e.preventDefault();
                    return;
                }
                // Don't swallow keys during IME composition — the IME owns them.
                if (UniTextNativeInputState.composingActive || e.isComposing) {
                    var producer = UniTextNativeInputState.transparentProducer;
                    setTimeout(function() {
                        if (!producer || UniTextNativeInputState.transparentProducer !== producer) return;
                        try {
                            _UniTextNativeInput_emitComposition(el);
                        } catch (error) {
                            _UniTextNativeInput_failInput(
                                null, producer, producer.sessionId, error);
                        }
                    }, 0);
                    return;
                }
                var mapped = _UniTextNativeInput_keyEventToNative(e);
                if (mapped === null) return;

                if (mapped.paste) {
                    // C15: never preventDefault the paste combo — the browser's
                    // default action dispatches the DOM 'paste' event that feeds
                    // the capture cache in UniTextClipboard.jslib. The key combo
                    // is delivered from the paste listener below, AFTER the
                    // cache is fresh; the fallback timer covers the case where
                    // no paste event ever fires.
                    var st = UniTextNativeInputState;
                    st.pendingPasteCode = mapped.code;
                    st.pendingPasteMods = mapped.mods;
                    if (st.pasteFallbackTimer) clearTimeout(st.pasteFallbackTimer);
                    st.pasteFallbackTimer =
                        setTimeout(_UniTextNativeInput_flushPendingPaste, 50);
                    return;
                }

                if (mapped.prevent) e.preventDefault();
                var cb = UniTextNativeInputState.keyDownCallback;
                if (cb !== 0) {{{ makeDynCall('vii', 'cb') }}}(mapped.code, mapped.mods);
            });

            el.addEventListener('paste', function(e) {
                // Block insertion into the hidden input — C# owns the document
                // (otherwise the pasted text would also arrive via 'input').
                // The capture-phase document listener in UniTextClipboard.jslib
                // has already read e.clipboardData by the time this target-phase
                // listener runs, so the sync cache is fresh for the C# read.
                e.preventDefault();
                var st = UniTextNativeInputState;
                if (st.transparentFrozen) return;
                if (st.composingActive) return;
                // A late paste event after the fallback already delivered this
                // combo must not paste twice.
                if (st.pasteFallbackAt &&
                    (performance.now() - st.pasteFallbackAt) < 400) return;

                var code, mods;
                if (st.pasteFallbackTimer) {
                    clearTimeout(st.pasteFallbackTimer);
                    st.pasteFallbackTimer = 0;
                    code = st.pendingPasteCode;
                    mods = st.pendingPasteMods;
                } else {
                    // Browser menu Edit > Paste (no preceding key combo).
                    code = 11;
                    mods = st.isApple() ? 8 : 2;
                }
                var cb = st.keyDownCallback;
                if (cb !== 0) {{{ makeDynCall('vii', 'cb') }}}(code, mods);
            });

            // Browser menu Edit > Copy / Cut — synthesize the combo so the C#
            // pipeline (which owns the real selection) performs the operation.
            // Keyboard-driven copy/cut never lands here: those keydowns are
            // preventDefault'ed, which cancels the clipboard event dispatch.
            el.addEventListener('copy', function(e) {
                e.preventDefault();
                var st = UniTextNativeInputState;
                if (st.transparentFrozen) return;
                if (st.composingActive) return;
                var cb = st.keyDownCallback;
                if (cb !== 0) {{{ makeDynCall('vii', 'cb') }}}(2, st.isApple() ? 8 : 2);
            });

            el.addEventListener('cut', function(e) {
                e.preventDefault();
                var st = UniTextNativeInputState;
                if (st.transparentFrozen) return;
                if (st.composingActive) return;
                var cb = st.keyDownCallback;
                if (cb !== 0) {{{ makeDynCall('vii', 'cb') }}}(12, st.isApple() ? 8 : 2);
            });

            document.body.appendChild(el);
            st.transparentInputElement = el;
        }

        var enterKeyHint = enterKeyHintPtr ? UTF8ToString(enterKeyHintPtr) : '';
        if (enterKeyHint) el.setAttribute('enterkeyhint', enterKeyHint);
        else el.removeAttribute('enterkeyhint');
        if (showSoftwareKeyboard) el.removeAttribute('inputmode');
        else el.setAttribute('inputmode', 'none');
        el.type = secureEntry !== 0 ? 'password' : 'text';
        _UniTextNativeInput_armSentinel(el);

        _UniTextNativeInput_hideNativeField();
        st.composingActive = false;
        st.compositionText = '';
        st.compositionCursor = -1;
        if (st.transparentSessionId !== sessionId) st.transparentRevision = 0;
        st.transparentSessionId = sessionId;
        st.transparentProducer = { sessionId: sessionId };
        st.transparentFrozen = false;
        st.transparentQuiesce = null;
        _UniTextNativeInput_applyCaretPos();

        _UniTextNativeInput_focusElement(el);
    },

    UniTextNativeInput_SetCaretScreenPos__deps: [
        '$UniTextNativeInputState',
        'UniTextNativeInput_applyCaretPos'
    ],
    UniTextNativeInput_SetCaretScreenPos: function(unityX, unityY, lineHeight) {
        var st = UniTextNativeInputState;
        var height = lineHeight > 0 ? lineHeight : 16;
        if (st.hasCaretGeometry && st.caretUnityX === unityX &&
            st.caretUnityY === unityY && st.caretLineHeight === height) return;
        st.hasCaretGeometry = true;
        st.caretUnityX = unityX;
        st.caretUnityY = unityY;
        st.caretLineHeight = height;
        _UniTextNativeInput_applyCaretPos();
    }
};

autoAddDeps(UniTextNativeInputPlugin, '$UniTextNativeInputState');
mergeInto(LibraryManager.library, UniTextNativeInputPlugin);
