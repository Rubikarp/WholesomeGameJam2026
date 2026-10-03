var UniTextCursorPlugin = {

    $UniTextCursorState: {
        canvas: null
    },

    // Same canvas resolution as UniTextNativeInput.jslib: prefer #unity-canvas —
    // on multi-canvas pages (overlays, embeds) the first <canvas> in DOM order
    // may not be the Unity player. Setting the cursor on document.body would
    // fight the host page's own styling, so it is only the no-canvas fallback.
    UniTextSetCursor__deps: ['$UniTextCursorState'],
    UniTextSetCursor: function (cssValuePtr) {
        var css = UTF8ToString(cssValuePtr);
        var canvas = UniTextCursorState.canvas;
        if (!canvas || !canvas.isConnected) {
            canvas = document.querySelector('#unity-canvas') || document.querySelector('canvas');
            UniTextCursorState.canvas = canvas;
        }
        if (canvas) canvas.style.cursor = css;
        else document.body.style.cursor = css;
    }

};

autoAddDeps(UniTextCursorPlugin, '$UniTextCursorState');
mergeInto(LibraryManager.library, UniTextCursorPlugin);
