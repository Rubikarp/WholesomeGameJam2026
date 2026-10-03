// ============================================================================
// UniText Clipboard — WebGL (Browser Async Clipboard API + Web Custom Formats)
// ============================================================================
//
// Multi-format clipboard for the modern Web Async Clipboard API:
//   - WRITE: navigator.clipboard.write([new ClipboardItem({ "text/plain": blob,
//     "text/html": blob, "web text/markdown": blob })]) — atomic multi-format
//     write; paste consumer picks the richest format it understands.
//   - READ: paste event captures every format from e.clipboardData in one go;
//     subsequent synchronous reads from C# return the captured format payload.
//
// Web Custom Formats (Chrome 104+ / Edge 104+) carry markdown source via the
// "web text/markdown" prefix — Firefox / Safari ignore the prefix and the
// format is invisible there. Plain text fallback is always written so paste
// always works regardless of consumer.
//
// Async clipboard write requires HTTPS + a recent user gesture. The Sync
// document.execCommand('copy') fallback covers older browsers / non-secure
// contexts but only carries plain text.
//
// Read model (contract C11, async-first): the captured cache is only a fast
// path valid when a real DOM 'paste' event fed it. captureSequence increments
// on every capture; C# compares it against the last consumed value to decide
// between the sync cache and navigator.clipboard read. Writes still mirror
// into the cache (best effort for the sync GetText API) but never bump the
// sequence — self-written content must not masquerade as a fresh paste.
// ============================================================================

var UniTextClipboardPlugin = {

    $UniTextClipboardState: {
        listenerAttached: false,
        pasteHandler: null,
        // Format-keyed buffer pointers (UTF-8). Updated synchronously on each
        // paste event; freed before being replaced.
        captured: {},   // { "text/plain": ptr, "text/html": ptr, "web text/markdown": ptr }
        captureSequence: 0,  // increments on every real DOM paste capture
        capturedBinary: {},  // { "image/png": Uint8Array } resolved asynchronously on paste
        capturedFiles: [],   // names of files on the most recent paste
        capturedFileData: {} // { name: Uint8Array } file bytes, resolved asynchronously on paste
    },

    UniTextClipboard_storeCaptured__deps: ['$UniTextClipboardState'],
    UniTextClipboard_storeCaptured: function(key, text) {
        var existing = UniTextClipboardState.captured[key];
        if (existing) _free(existing);
        if (!text) {
            UniTextClipboardState.captured[key] = 0;
            return;
        }
        var bufferSize = lengthBytesUTF8(text) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(text, buffer, bufferSize);
        UniTextClipboardState.captured[key] = buffer;
    },

    UniTextClipboard_Init__deps: [
        '$UniTextClipboardState',
        'UniTextClipboard_storeCaptured'
    ],
    UniTextClipboard_Init: function() {
        if (UniTextClipboardState.listenerAttached) return;
        UniTextClipboardState.listenerAttached = true;

        // Capture every format the browser exposes on the DataTransfer, including
        // standard MIME types and Chromium "web "-prefixed custom formats. We
        // store each under its raw key; the GetFormat read path normalises
        // requested identifiers (with or without the "web " prefix) to the
        // matching captured slot.
        var handler = function(e) {
            if (!e.clipboardData || !e.clipboardData.getData) return;
            var types = e.clipboardData.types;
            if (!types) return;

            // The cache must mirror exactly ONE DataTransfer: free and drop every
            // stale slot first, or a rich format from an OLDER paste outranks the
            // current paste's plain text in the adapter priority chain.
            var stale = UniTextClipboardState.captured;
            for (var key in stale) {
                if (stale[key]) _free(stale[key]);
            }
            UniTextClipboardState.captured = {};
            UniTextClipboardState.capturedFiles = [];

            for (var i = 0; i < types.length; i++) {
                var t = types[i];
                if (!t) continue;
                var text = e.clipboardData.getData(t);
                _UniTextClipboard_storeCaptured(t, text || '');
            }
            UniTextClipboardState.captureSequence =
                (UniTextClipboardState.captureSequence + 1) | 0;

            // Binary / file items. arrayBuffer() is async, so the bytes land in
            // capturedBinary a microtask later — a synchronous C# read on this same
            // paste may miss them (browser async-clipboard reality). Resolutions
            // write into the objects captured HERE, so a slow arrayBuffer from an
            // older paste can never pollute a newer capture.
            var binTarget = UniTextClipboardState.capturedBinary = {};
            var fileTarget = UniTextClipboardState.capturedFileData = {};
            var dtItems = e.clipboardData.items;
            if (dtItems) {
                for (var k = 0; k < dtItems.length; k++) {
                    var it = dtItems[k];
                    if (it.kind !== 'file') continue;
                    var file = it.getAsFile();
                    if (!file) continue;
                    (function(type, f) {
                        f.arrayBuffer().then(function(buf) {
                            var bytes = new Uint8Array(buf);
                            binTarget[type] = bytes;
                            if (f.name) fileTarget[f.name] = bytes;
                        });
                    })(it.type, file);
                }
            }
            var dtFiles = e.clipboardData.files;
            if (dtFiles) for (var fi = 0; fi < dtFiles.length; fi++) UniTextClipboardState.capturedFiles.push(dtFiles[fi].name);
        };
        UniTextClipboardState.pasteHandler = handler;
        document.addEventListener('paste', handler, true);
    },

    // Player re-instantiated on the same page without a full reload: the old
    // listener's closure would call _malloc/_free against the PREVIOUS wasm
    // module's heap. Detach it and free every captured pointer.
    UniTextClipboard_Shutdown__deps: ['$UniTextClipboardState'],
    UniTextClipboard_Shutdown: function() {
        var st = UniTextClipboardState;
        if (st.pasteHandler) {
            document.removeEventListener('paste', st.pasteHandler, true);
            st.pasteHandler = null;
        }
        st.listenerAttached = false;
        for (var key in st.captured) {
            if (st.captured[key]) _free(st.captured[key]);
        }
        st.captured = {};
        st.capturedBinary = {};
        st.capturedFiles = [];
        st.capturedFileData = {};
        st.captureSequence = 0;
    },

    UniTextClipboard_GetText: function() {
        _UniTextClipboard_Init();
        return UniTextClipboardState.captured['text/plain'] || 0;
    },

    /// Synchronous read of an arbitrary format captured on the most recent paste.
    /// Returns 0 (null pointer) when no payload was captured for that format.
    UniTextClipboard_GetFormat__deps: [
        '$UniTextClipboardState',
        'UniTextClipboard_Init'
    ],
    UniTextClipboard_GetFormat: function(formatPtr) {
        _UniTextClipboard_Init();
        var format = UTF8ToString(formatPtr);
        if (!format) return 0;
        // Try the requested identifier first, then the Chromium-prefixed variant
        // (custom formats land under "web <mime>" when delivered by another tab),
        // then the unprefixed variant (in case caller passed the prefixed form).
        var direct = UniTextClipboardState.captured[format];
        if (direct) return direct;
        if (format.indexOf('web ') === 0) {
            return UniTextClipboardState.captured[format.substring(4)] || 0;
        }
        return UniTextClipboardState.captured['web ' + format] || 0;
    },

    UniTextClipboard_HasText: function() {
        _UniTextClipboard_Init();
        return UniTextClipboardState.captured['text/plain'] ? 1 : 0;
    },

    /// Counter incremented on every real DOM paste capture. C# compares it
    /// against the last consumed value: unchanged sequence means the sync
    /// cache is stale and the async clipboard read must be used instead (C11).
    UniTextClipboard_GetCaptureSequence__deps: ['$UniTextClipboardState'],
    UniTextClipboard_GetCaptureSequence: function() {
        return UniTextClipboardState.captureSequence;
    },

    UniTextClipboard_SetText__deps: [
        '$UniTextClipboardState',
        'UniTextClipboard_storeCaptured'
    ],
    UniTextClipboard_SetText: function(textPtr) {
        var text = UTF8ToString(textPtr);

        // Prefer the async API — it writes synchronously to the user's clipboard.
        // The execCommand fallback covers non-HTTPS / older browser cases.
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).catch(function() {
                _UniTextClipboard_execCommandCopy(text);
            });
        } else {
            _UniTextClipboard_execCommandCopy(text);
        }

        _UniTextClipboard_storeCaptured('text/plain', text);
    },

    /// Multi-format write entry. Receives parallel arrays of format identifiers and
    /// UTF-8 payloads (length = `count`) plus an optional binary image payload
    /// (imageMimePtr = 0 when absent). Builds ONE ClipboardItem carrying every text
    /// format AND the image blob together — Chromium's ClipboardItem accepts e.g.
    /// { "image/png": blob, "text/plain": blob } in one write, so text is never
    /// dropped when an image rides along. Web Custom Formats (markdown, vendor)
    /// are prefixed with "web ".
    ///
    /// Returns 0 when the async ClipboardItem API is unavailable (older
    /// Safari/Firefox): the plain-text item — when present — was already written
    /// via execCommand here; rich-only lists are lost on such browsers (no sync
    /// API can carry them). C# deliberately ignores the return value: its managed
    /// fallback writes only plain text, which this branch already covered.
    UniTextClipboard_SetItems__deps: [
        '$UniTextClipboardState',
        'UniTextClipboard_storeCaptured',
        'UniTextClipboard_execCommandCopy'
    ],
    UniTextClipboard_SetItems: function(formatPtrs, payloadPtrs, count, imageMimePtr, imageDataPtr, imageLen) {
        var imageMime = imageMimePtr ? UTF8ToString(imageMimePtr) : null;
        var hasImage = !!(imageMime && imageDataPtr && imageLen > 0);
        if (count <= 0 && !hasImage) return 0;
        if (!navigator.clipboard || !navigator.clipboard.write || typeof ClipboardItem === 'undefined') {
            // Async ClipboardItem unsupported (older Safari/Firefox) — pick the
            // plain-text item and fall back to execCommand.
            var fallbackText = '';
            var fallbackFound = false;
            for (var i = 0; i < count; i++) {
                var fmt = UTF8ToString(HEAP32[(formatPtrs >> 2) + i]);
                if (fmt === 'text/plain') {
                    fallbackText = UTF8ToString(HEAP32[(payloadPtrs >> 2) + i]);
                    fallbackFound = true;
                    break;
                }
            }
            if (fallbackFound) {
                _UniTextClipboard_execCommandCopy(fallbackText);
                _UniTextClipboard_storeCaptured('text/plain', fallbackText);
            }
            return 0;
        }

        // Chromium accepts these MIME types directly in ClipboardItem; everything
        // else needs the "web " prefix (Async Clipboard custom formats spec).
        // The list intentionally short — Chromium's allowlist for non-prefixed
        // MIMEs is itself short (text/plain, text/html, text/uri-list, image/png).
        function isStandardClipboardMime(m) {
            return m === 'text/plain' || m === 'text/html'
                || m === 'text/uri-list' || m === 'image/png';
        }

        var record = {};
        var capturedSnapshot = {};
        for (var j = 0; j < count; j++) {
            var format = UTF8ToString(HEAP32[(formatPtrs >> 2) + j]);
            var payload = UTF8ToString(HEAP32[(payloadPtrs >> 2) + j]);
            if (!format) continue;

            // Standard MIMEs go in as-is; everything else (markdown, vendor
            // custom, integrator ClipboardFormat.Custom) gets the "web " prefix
            // per Chromium Async Clipboard custom format requirement.
            var key = isStandardClipboardMime(format) ? format : ('web ' + format);

            record[key] = new Blob([payload], { type: format });
            capturedSnapshot[format] = payload;
        }

        var imageBytes = null;
        if (hasImage) {
            imageBytes = HEAPU8.slice(imageDataPtr, imageDataPtr + imageLen);
            var imageKey = isStandardClipboardMime(imageMime) ? imageMime : ('web ' + imageMime);
            record[imageKey] = new Blob([imageBytes], { type: imageMime });
        }

        try {
            var item = new ClipboardItem(record);
            navigator.clipboard.write([item]).catch(function(err) {
                // Browser-side write failed (permission, no user gesture, etc.).
                // Fall back to plain text via execCommand so the user is not left
                // with an empty clipboard.
                if (capturedSnapshot['text/plain']) {
                    _UniTextClipboard_execCommandCopy(capturedSnapshot['text/plain']);
                }
            });
        } catch (e) {
            if (capturedSnapshot['text/plain']) {
                _UniTextClipboard_execCommandCopy(capturedSnapshot['text/plain']);
            }
        }

        // Mirror the write into the local captured slots so a subsequent GetText /
        // GetFormat / GetData from C# returns what was just written (matches the
        // existing text-only behaviour where SetText also primed the read cache).
        // No captureSequence bump — self-writes never masquerade as a fresh paste.
        for (var k in capturedSnapshot) {
            _UniTextClipboard_storeCaptured(k, capturedSnapshot[k]);
        }
        if (hasImage) {
            UniTextClipboardState.capturedBinary[imageMime] = imageBytes;
        }
        return 1;
    },

    /// Asynchronous clipboard read. Calls navigator.clipboard.readText() for
    /// plain text; navigator.clipboard.read() for other formats (with the
    /// Chrome 120+ { unsanitized: ['text/html'] } option to preserve byte-exact
    /// HTML). On resolve / reject delivers the result back to C# via
    /// SendMessage('UniTextWebGLAsyncDispatcher', 'OnAsyncTextResolved',
    /// '<requestId>|<text>') — empty payload after the separator means null
    /// (no data, denied, or unsupported).
    ///
    /// Browser support requires HTTPS + user activation. Calling outside a
    /// recent user gesture rejects the Promise and the C# Task resolves to
    /// null.
    UniTextClipboard_RequestAsyncReadText__deps: ['$UniTextClipboardState'],
    UniTextClipboard_RequestAsyncReadText: function(formatPtr, requestId) {
        var format = UTF8ToString(formatPtr) || 'text/plain';

        function deliver(text) {
            var payload = requestId + '|' + (text == null ? '' : text);
            try { SendMessage('UniTextWebGLAsyncDispatcher', 'OnAsyncTextResolved', payload); }
            catch (e) { console.error('[UniText] Async dispatch failed:', e); }
        }

        if (!navigator.clipboard) { deliver(null); return; }

        if (format === 'text/plain') {
            if (!navigator.clipboard.readText) { deliver(null); return; }
            navigator.clipboard.readText()
                .then(function(text) { deliver(text); })
                .catch(function(err) { console.warn('[UniText] readText rejected:', err); deliver(null); });
            return;
        }

        if (!navigator.clipboard.read) { deliver(null); return; }

        // Chromium 120+ supports { unsanitized: ['text/html'] } to preserve
        // byte-exact HTML across copy/paste. Other browsers ignore the option
        // dictionary (per the Chrome status note); harmless to pass always.
        var readPromise;
        try {
            readPromise = (format === 'text/html')
                ? navigator.clipboard.read({ unsanitized: ['text/html'] })
                : navigator.clipboard.read();
        } catch (e) {
            // Some older browsers throw on the options dictionary argument
            // rather than ignoring it — retry without it.
            readPromise = navigator.clipboard.read();
        }

        readPromise.then(function(items) {
            if (!items || items.length === 0) { deliver(null); return; }
            var item = items[0];
            var targetType = format;

            if (item.types.indexOf(format) === -1) {
                // Chromium prefixes custom (non-MIME-registry) formats with "web ".
                // Markdown is delivered under "web text/markdown"; our vendor UTI is
                // delivered under "web application/vnd.lightside.unitext".
                var webPrefixed = 'web ' + format;
                if (item.types.indexOf(webPrefixed) !== -1) targetType = webPrefixed;
                else { deliver(null); return; }
            }

            item.getType(targetType)
                .then(function(blob) { return blob.text(); })
                .then(function(text) { deliver(text); })
                .catch(function(err) { console.warn('[UniText] getType rejected:', err); deliver(null); });
        }).catch(function(err) {
            console.warn('[UniText] clipboard.read rejected:', err);
            deliver(null);
        });
    },

    // One navigator.clipboard.read() serving EVERY requested format (formatsPtr =
    // UTF-8 canonical identifiers joined by '\n'). Programmatic paste (context menu,
    // paste control) has no DOM paste event feeding the capture cache, and on
    // Safari/Firefox a second read() rejects after the first consumes the transient
    // user activation — so the batch must be a single read. Missing / rejected
    // formats are omitted, never fail the batch. Delivery wire format
    // (length-prefixed — payloads are arbitrary clipboard text, no delimiter can be
    // trusted; lengths are JS string.length = UTF-16 code units = C# string.Length):
    //   requestId|count|fmt1Len|fmt1payload1Len|payload1fmt2Len|fmt2...
    // Failure / denial / nothing readable -> "requestId|0|" (C# falls back to the
    // capture cache). Formats are reported under their CANONICAL identifier, not the
    // "web "-prefixed wire type.
    UniTextClipboard_RequestAsyncReadAll__deps: ['$UniTextClipboardState'],
    UniTextClipboard_RequestAsyncReadAll: function(formatsPtr, requestId) {
        var formats = (UTF8ToString(formatsPtr) || '').split('\n').filter(function(f) { return f.length > 0; });

        function deliver(pairs) {
            var msg = requestId + '|' + pairs.length + '|';
            for (var i = 0; i < pairs.length; i++)
                msg += pairs[i][0].length + '|' + pairs[i][0] + pairs[i][1].length + '|' + pairs[i][1];
            try { SendMessage('UniTextWebGLAsyncDispatcher', 'OnAsyncItemsResolved', msg); }
            catch (e) { console.error('[UniText] Async dispatch failed:', e); }
        }
        function fail() { deliver([]); }

        if (formats.length === 0 || !navigator.clipboard || !navigator.clipboard.read) { fail(); return; }

        var readPromise;
        try {
            readPromise = (formats.indexOf('text/html') !== -1)
                ? navigator.clipboard.read({ unsanitized: ['text/html'] })
                : navigator.clipboard.read();
        } catch (e) {
            readPromise = navigator.clipboard.read();
        }

        readPromise.then(function(items) {
            if (!items || items.length === 0) { fail(); return; }
            var item = items[0];
            var reads = formats.map(function(format) {
                var targetType = null;
                if (item.types.indexOf(format) !== -1) targetType = format;
                else if (item.types.indexOf('web ' + format) !== -1) targetType = 'web ' + format;
                if (targetType === null) return Promise.resolve(null);
                return item.getType(targetType)
                    .then(function(blob) { return blob.text(); })
                    .then(function(text) { return text ? [format, text] : null; })
                    .catch(function(err) { console.warn('[UniText] getType rejected:', err); return null; });
            });
            Promise.all(reads).then(function(results) {
                deliver(results.filter(function(r) { return r !== null; }));
            });
        }).catch(function(err) {
            console.warn('[UniText] clipboard.read rejected:', err);
            fail();
        });
    },

    UniTextClipboard_HasFormatData__deps: ['$UniTextClipboardState'],
    UniTextClipboard_HasFormatData: function(formatPtr) {
        var f = UTF8ToString(formatPtr);
        return (f && UniTextClipboardState.capturedBinary[f]) ? 1 : 0;
    },

    UniTextClipboard_GetDataLength__deps: ['$UniTextClipboardState'],
    UniTextClipboard_GetDataLength: function(formatPtr) {
        var f = UTF8ToString(formatPtr);
        var b = f && UniTextClipboardState.capturedBinary[f];
        return b ? b.length : 0;
    },

    // maxLen caps the copy to the C#-allocated buffer: a pending arrayBuffer()
    // resolution can replace the payload with a LONGER array between the
    // GetDataLength call and this copy — unbounded set() would overwrite
    // adjacent wasm heap. (maxLen<=0 keeps payload length for old callers.)
    UniTextClipboard_GetDataCopy__deps: ['$UniTextClipboardState'],
    UniTextClipboard_GetDataCopy: function(formatPtr, dst, maxLen) {
        var f = UTF8ToString(formatPtr);
        var b = f && UniTextClipboardState.capturedBinary[f];
        if (!b) return;
        var cap = (maxLen > 0) ? maxLen : b.length;
        HEAPU8.set(b.length > cap ? b.subarray(0, cap) : b, dst);
    },

    UniTextClipboard_ReadFileLength__deps: ['$UniTextClipboardState'],
    UniTextClipboard_ReadFileLength: function(namePtr) {
        var n = UTF8ToString(namePtr);
        var b = n && UniTextClipboardState.capturedFileData[n];
        return b ? b.length : 0;
    },

    UniTextClipboard_ReadFileCopy__deps: ['$UniTextClipboardState'],
    UniTextClipboard_ReadFileCopy: function(namePtr, dst, maxLen) {
        var n = UTF8ToString(namePtr);
        var b = n && UniTextClipboardState.capturedFileData[n];
        if (!b) return;
        var cap = (maxLen > 0) ? maxLen : b.length;
        HEAPU8.set(b.length > cap ? b.subarray(0, cap) : b, dst);
    },

    UniTextClipboard_HasFiles__deps: ['$UniTextClipboardState'],
    UniTextClipboard_HasFiles: function() {
        return (UniTextClipboardState.capturedFiles && UniTextClipboardState.capturedFiles.length) ? 1 : 0;
    },

    UniTextClipboard_GetFiles__deps: ['$UniTextClipboardState', 'UniTextClipboard_storeCaptured'],
    UniTextClipboard_GetFiles: function() {
        var arr = UniTextClipboardState.capturedFiles;
        if (!arr || arr.length === 0) return 0;
        _UniTextClipboard_storeCaptured('__files__', arr.join('\n'));
        return UniTextClipboardState.captured['__files__'] || 0;
    },

    UniTextClipboard_WriteImage__deps: ['$UniTextClipboardState'],
    UniTextClipboard_WriteImage: function(mimePtr, dataPtr, len) {
        var mime = UTF8ToString(mimePtr);
        if (!mime || len <= 0) return 0;
        if (!navigator.clipboard || !navigator.clipboard.write || typeof ClipboardItem === 'undefined') return 0;
        var bytes = HEAPU8.slice(dataPtr, dataPtr + len);
        var key = (mime === 'image/png') ? mime : ('web ' + mime);
        var rec = {};
        rec[key] = new Blob([bytes], { type: mime });
        try { navigator.clipboard.write([new ClipboardItem(rec)]).catch(function(){}); }
        catch (e) { return 0; }
        // Mirror into the binary capture slot (no captureSequence bump — the
        // text-path convention) so self-read after self-write works for images
        // the same way GetText works after SetText.
        UniTextClipboardState.capturedBinary[mime] = bytes;
        return 1;
    },

    UniTextClipboard_execCommandCopy__deps: ['$UniTextClipboardState'],
    UniTextClipboard_execCommandCopy: function(text) {
        try {
            var textarea = document.createElement('textarea');
            textarea.value = text;
            textarea.style.position = 'fixed';
            textarea.style.left = '-9999px';
            textarea.style.top = '-9999px';
            document.body.appendChild(textarea);
            textarea.select();
            document.execCommand('copy');
            document.body.removeChild(textarea);
        } catch (e) {
            console.error('[UniText] Clipboard write failed:', e);
        }
    }
};

autoAddDeps(UniTextClipboardPlugin, '$UniTextClipboardState');
mergeInto(LibraryManager.library, UniTextClipboardPlugin);
