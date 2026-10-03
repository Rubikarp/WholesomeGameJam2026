#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX

using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace LightSide
{
    internal sealed class NativeInputMacOS : INativeInputBackend
    {
        const string PluginName = "UniTextNativeInputMacOS";

        [DllImport(PluginName)]
        static extern int UniTextNativeInput_Init();

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_Shutdown();

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_SetEnabled(int enabled);

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_SetCursorPos(float x, float y, float lineHeight);

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_SetProjection(float offsetX, float scaleX,
            float offsetY, float scaleY);

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_SetCaretScreenRect(int left, int top, int right, int bottom);

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_CompleteComposition();

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_DiscardComposition();

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_RegisterCallbacks(
            IntPtr keyDown,
            IntPtr textInput,
            IntPtr deleteBack,
            IntPtr composition,
            IntPtr compositionEnded);

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_RegisterContextQueries(
            IntPtr getSelectedRange,
            IntPtr getTextAtRange,
            IntPtr setSelectionCharRange);

        [DllImport(PluginName)]
        static extern void UniTextNativeInput_RegisterCharRangeRect(IntPtr getCharRangeRect);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void NativeKeyDownCallback(int keyCode, int modifiers);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void NativeTextInputCallback(IntPtr utf8Text);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void NativeCompositionCallback(
            IntPtr text, int textLength,
            IntPtr clauseOffsets, IntPtr clauseStyles, int clauseCount,
            int cursorPosition);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void NativeCompositionEndedCallback();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void NativeGetSelectedRangeCallback(out int outStart, out int outLength);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int NativeGetTextAtRangeCallback(int charStart, int charLength, IntPtr outBuffer, int outCapacity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void NativeSetSelectionCharRangeCallback(int charStart, int charLength);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int NativeGetCharRangeRectCallback(int charStart, int charLength,
            out float x, out float y, out float w, out float h);

        static char[] compositionTextBuffer = new char[128];

        static CompositionClause[] compositionClauseBuffer = new CompositionClause[16];

        static readonly NativeKeyDownCallback keyDownDelegate = OnNativeKeyDown;
        static readonly NativeTextInputCallback textInputDelegate = OnNativeTextInput;
        static readonly NativeCompositionCallback compositionDelegate = OnNativeComposition;
        static readonly NativeCompositionEndedCallback compositionEndedDelegate = OnNativeCompositionEnded;
        static readonly NativeGetSelectedRangeCallback getSelectedRangeDelegate = OnGetSelectedRange;
        static readonly NativeGetTextAtRangeCallback getTextAtRangeDelegate = OnGetTextAtRange;
        static readonly NativeSetSelectionCharRangeCallback setSelectionCharRangeDelegate = OnSetSelectionCharRange;
        static readonly NativeGetCharRangeRectCallback getCharRangeRectDelegate = OnGetCharRangeRect;

        static NativeInputReporter reporter;
        static NativeInputReporter quiescedReporter;
        static bool suppressEditingReports;

        bool disposed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            UniTextNativeInput.RegisterBackend(Create, 0);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterEditor()
        {
            if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                UniTextNativeInput.RegisterBackend(Create, 0);
        }
#endif

        static INativeInputBackend Create()
        {
            UniTextNativeInput_RegisterCallbacks(
                Marshal.GetFunctionPointerForDelegate(keyDownDelegate),
                Marshal.GetFunctionPointerForDelegate(textInputDelegate),
                IntPtr.Zero,
                Marshal.GetFunctionPointerForDelegate(compositionDelegate),
                Marshal.GetFunctionPointerForDelegate(compositionEndedDelegate));
            UniTextNativeInput_RegisterContextQueries(
                Marshal.GetFunctionPointerForDelegate(getSelectedRangeDelegate),
                Marshal.GetFunctionPointerForDelegate(getTextAtRangeDelegate),
                Marshal.GetFunctionPointerForDelegate(setSelectionCharRangeDelegate));
            UniTextNativeInput_RegisterCharRangeRect(
                Marshal.GetFunctionPointerForDelegate(getCharRangeRectDelegate));

            if (UniTextNativeInput_Init() == 0)
                throw new InvalidOperationException("The macOS native input producer could not initialize its input client.");

            UniTextNativeInput.imeCaretScreenRect = SetCaretScreenRect;
            return new NativeInputMacOS();
        }

        static void SetCaretScreenRect(RectInt rect)
        {
            UniTextNativeInput_SetCaretScreenRect(rect.xMin, rect.yMin, rect.xMax, rect.yMax);
        }

        public void OpenInput(in NativeInputOpenRequest request, NativeInputReporter value)
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeInputMacOS));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (reporter != null)
                throw new InvalidOperationException("The macOS input producer is already open.");
            var inherited = quiescedReporter;
            reporter = value;
            try
            {
                UniTextNativeInput_SetEnabled(1);
                quiescedReporter = null;
            }
            catch
            {
                reporter = null;
                quiescedReporter = inherited;
                throw;
            }
        }

        public void QuiesceInput(NativeInputReporter value,
            NativeCompositionDisposition disposition, Action quiesced)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (quiesced == null) throw new ArgumentNullException(nameof(quiesced));
            if (!ReferenceEquals(reporter, value))
                throw new InvalidOperationException("The reporter is not bound to the macOS producer.");

            if (disposition == NativeCompositionDisposition.Commit)
            {
                UniTextNativeInput_CompleteComposition();
            }
            else if (disposition == NativeCompositionDisposition.Cancel)
            {
                suppressEditingReports = true;
                try
                {
                    UniTextNativeInput_DiscardComposition();
                }
                finally
                {
                    suppressEditingReports = false;
                }
                value.ReportCompositionEnded();
            }

            reporter = null;
            quiescedReporter = value;
            if (disposition != NativeCompositionDisposition.Preserve)
                UniTextNativeInput_SetEnabled(0);
            quiesced();
        }

        public void CloseInput(NativeInputReporter value)
        {
            if (!ReferenceEquals(quiescedReporter, value))
                throw new InvalidOperationException("The reporter is not bound to a quiesced macOS producer.");
            UniTextNativeInput_SetEnabled(0);
            quiescedReporter = null;
        }

        public void AbortInput(NativeInputReporter value)
        {
            if (ReferenceEquals(reporter, value)) reporter = null;
            if (ReferenceEquals(quiescedReporter, value)) quiescedReporter = null;
            if (!disposed) UniTextNativeInput_SetEnabled(0);
        }

        public void SetCursorScreenPos(Vector2 screenPos, float lineHeight)
        {
            if (disposed) return;
            UniTextNativeInput.GetImeWindowProjection(out float offsetX, out float scaleX,
                out float offsetY, out float scaleY);
            UniTextNativeInput_SetProjection(offsetX, scaleX, offsetY, scaleY);
            UniTextNativeInput_SetCursorPos(screenPos.x, screenPos.y, lineHeight);
        }

        public void SetInputFieldRect(Rect screenRect)
        {
        }

        public void FlushPendingInput()
        {
        }

        public bool WantsTextContext => false;
        public void PushTextContext(string text, int windowStart, int selectionStart, int selectionEnd, bool forceRestart)
        {
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            reporter = null;
            quiescedReporter = null;
            if (UniTextNativeInput.imeCaretScreenRect == (Action<RectInt>)SetCaretScreenRect)
                UniTextNativeInput.imeCaretScreenRect = null;
            UniTextNativeInput_Shutdown();
        }

        [MonoPInvokeCallback(typeof(NativeKeyDownCallback))]
        static void OnNativeKeyDown(int keyCode, int modifiers)
        {
            var target = reporter;
            if (target == null || suppressEditingReports ||
                !NativeKeyCodeExtensions.IsDeliverable(keyCode)) return;
            target.ReportKeyDown((NativeKeyCode)keyCode, (NativeModifiers)modifiers);
        }

        [MonoPInvokeCallback(typeof(NativeTextInputCallback))]
        static void OnNativeTextInput(IntPtr utf8Text)
        {
            var target = reporter;
            if (target == null || suppressEditingReports || utf8Text == IntPtr.Zero) return;

            string text = Marshal.PtrToStringUTF8(utf8Text);
            target.ReportTextInput(text ?? string.Empty);
        }

        [MonoPInvokeCallback(typeof(NativeCompositionCallback))]
        static unsafe void OnNativeComposition(
            IntPtr textPtr, int textLength,
            IntPtr clauseOffsetsPtr, IntPtr clauseStylesPtr, int clauseCount,
            int cursorPosition)
        {
            var target = reporter;
            if (target == null || suppressEditingReports) return;
            if (textPtr == IntPtr.Zero || textLength <= 0)
            {
                target.ReportCompositionChanged(new CompositionData
                {
                    text = ReadOnlySpan<char>.Empty,
                    clauses = ReadOnlySpan<CompositionClause>.Empty,
                    cursorPosition = 0,
                });
                return;
            }

            if (compositionTextBuffer.Length < textLength)
            {
                int newSize = compositionTextBuffer.Length;
                while (newSize < textLength) newSize *= 2;
                compositionTextBuffer = new char[newSize];
            }

            ushort* src = (ushort*)textPtr;
            for (int i = 0; i < textLength; i++)
            {
                compositionTextBuffer[i] = (char)src[i];
            }

            if (clauseCount > 0 && clauseOffsetsPtr != IntPtr.Zero && clauseStylesPtr != IntPtr.Zero)
            {
                CompositionClause.EnsureCapacity(ref compositionClauseBuffer, clauseCount);

                int* offsets = (int*)clauseOffsetsPtr;
                int* styles = (int*)clauseStylesPtr;

                for (int i = 0; i < clauseCount; i++)
                {
                    compositionClauseBuffer[i] = new CompositionClause
                    {
                        startOffset = offsets[i * 2],
                        endOffset = offsets[i * 2 + 1],
                        style = (CompositionClauseStyle)styles[i],
                    };
                }
            }
            else
            {
                clauseCount = CompositionClause.FillFallback(ref compositionClauseBuffer, textLength);
            }

            var data = new CompositionData
            {
                text = new ReadOnlySpan<char>(compositionTextBuffer, 0, textLength),
                clauses = new ReadOnlySpan<CompositionClause>(compositionClauseBuffer, 0, clauseCount),
                cursorPosition = cursorPosition,
            };

            target.ReportCompositionChanged(data);
        }

        [MonoPInvokeCallback(typeof(NativeCompositionEndedCallback))]
        static void OnNativeCompositionEnded()
        {
            var target = reporter;
            if (target != null && !suppressEditingReports) target.ReportCompositionEnded();
        }

        [MonoPInvokeCallback(typeof(NativeGetSelectedRangeCallback))]
        static void OnGetSelectedRange(out int outStart, out int outLength)
        {
            var context = reporter?.Context;
            if (context == null)
            {
                outStart = 0;
                outLength = 0;
                return;
            }
            (outStart, outLength) = context.GetCharSelection();
        }

        [MonoPInvokeCallback(typeof(NativeGetTextAtRangeCallback))]
        static unsafe int OnGetTextAtRange(int charStart, int charLength, IntPtr outBuffer, int outCapacity)
        {
            var context = reporter?.Context;
            if (context == null) return 0;
            if (charLength <= 0 || outCapacity <= 0 || outBuffer == IntPtr.Zero) return 0;
            int requested = charLength < outCapacity ? charLength : outCapacity;
            var destination = new Span<char>((void*)outBuffer, requested);
            return context.CopyCharRange(charStart, requested, destination);
        }

        [MonoPInvokeCallback(typeof(NativeSetSelectionCharRangeCallback))]
        static void OnSetSelectionCharRange(int charStart, int charLength)
        {
            reporter?.Context.SelectCharRange(charStart, charLength);
        }

        [MonoPInvokeCallback(typeof(NativeGetCharRangeRectCallback))]
        static int OnGetCharRangeRect(int charStart, int charLength,
            out float x, out float y, out float w, out float h)
        {
            x = y = w = h = 0f;
            var context = reporter?.Context;
            if (context == null) return 0;
            if (!context.TryGetCharRangeRect(charStart, charLength, out var rect)) return 0;
            x = rect.x; y = rect.y; w = rect.width; h = rect.height;
            return 1;
        }
    }
}

#endif
