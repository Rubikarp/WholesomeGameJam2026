using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// Conditional debug logging wrapper that compiles out when LIGHTSIDE_DEBUG is not defined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All methods are marked with [Conditional("LIGHTSIDE_DEBUG")] so calls are completely
    /// removed from release builds with no runtime overhead.
    /// </para>
    /// <para>
    /// Mirrors the UnityEngine.Debug API for easy replacement of debug logging calls.
    /// </para>
    /// </remarks>
    public static class Cat
    {
        private static readonly Dictionary<string, int> dedupMap = new Dictionary<string, int>();

        /// <summary>Returns a zone handle for filtered logging. Without LIGHTSIDE_DEBUG it is an inert default — no registration.</summary>
        public static CatZone Zone(string name)
        {
#if LIGHTSIDE_DEBUG
            return new CatZone(CatZoneRegistry.Register(name));
#else
            return default;
#endif
        }

        /// <summary>Dedup gate shared by the <c>Once</c> overloads: true the first time <paramref name="text"/> differs from the last under <paramref name="key"/>.</summary>
        internal static bool OnceShouldLog(string key, string text)
        {
            int hash = text.GetHashCode();
            lock (dedupMap)
            {
                if (dedupMap.TryGetValue(key, out int prev) && prev == hash) return false;
                dedupMap[key] = hash;
            }
            return true;
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowOnce(string key, string format, params object[] args)
        {
            if (!CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) return;
            var text = string.Format(format, args);
            if (OnceShouldLog(key, text)) Debug.Log(text);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowWarnOnce(string key, string format, params object[] args)
        {
            if (!CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) return;
            var text = string.Format(format, args);
            if (OnceShouldLog(key, text)) Debug.LogWarning(text);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void Meow(object message)
        {
            if (CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) Debug.Log(message);
        }


        [Conditional("LIGHTSIDE_DEBUG")]
        public static void Meow(object message, Object context)
        {
            if (CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) Debug.Log(message, context);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowFormat(string format, params object[] args)
        {
            if (CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) Debug.LogFormat(format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowFormat(Object context, string format, params object[] args)
        {
            if (CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) Debug.LogFormat(context, format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowFormat(LogType logType, LogOption logOptions, Object context, string format, params object[] args)
        {
            if (CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) Debug.LogFormat(logType, logOptions, context, format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowWarn(object message)
        {
            if (CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) Debug.LogWarning(message);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowWarn(object message, Object context)
        {
            if (CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) Debug.LogWarning(message, context);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowWarnFormat(string format, params object[] args)
        {
            if (CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) Debug.LogWarningFormat(format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowWarnFormat(Object context, string format, params object[] args)
        {
            if (CatZoneRegistry.IsEnabled(CatZoneRegistry.generalIndex)) Debug.LogWarningFormat(context, format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowError(object message)
        {
            Debug.LogError(message);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowError(object message, Object context)
        {
            Debug.LogError(message, context);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowErrorFormat(string format, params object[] args)
        {
            Debug.LogErrorFormat(format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowErrorFormat(Object context, string format, params object[] args)
        {
            Debug.LogErrorFormat(context, format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowException(System.Exception exception)
        {
            Debug.LogException(exception);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowException(System.Exception exception, Object context)
        {
            Debug.LogException(exception, context);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowAssertion(object message)
        {
            Debug.LogAssertion(message);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowAssertion(object message, Object context)
        {
            Debug.LogAssertion(message, context);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowAssertionFormat(string format, params object[] args)
        {
            Debug.LogAssertionFormat(format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void MeowAssertionFormat(Object context, string format, params object[] args)
        {
            Debug.LogAssertionFormat(context, format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void Assert(bool condition)
        {
            Debug.Assert(condition);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void Assert(bool condition, Object context)
        {
            Debug.Assert(condition, context);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void Assert(bool condition, object message)
        {
            Debug.Assert(condition, message);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void Assert(bool condition, object message, Object context)
        {
            Debug.Assert(condition, message, context);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void AssertFormat(bool condition, string format, params object[] args)
        {
            Debug.AssertFormat(condition, format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void AssertFormat(bool condition, Object context, string format, params object[] args)
        {
            Debug.AssertFormat(condition, context, format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void DrawLine(Vector3 start, Vector3 end)
        {
            Debug.DrawLine(start, end);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            Debug.DrawLine(start, end, color);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void DrawLine(Vector3 start, Vector3 end, Color color, float duration)
        {
            Debug.DrawLine(start, end, color, duration);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void DrawLine(Vector3 start, Vector3 end, Color color, float duration, bool depthTest)
        {
            Debug.DrawLine(start, end, color, duration, depthTest);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void DrawRay(Vector3 start, Vector3 dir)
        {
            Debug.DrawRay(start, dir);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void DrawRay(Vector3 start, Vector3 dir, Color color)
        {
            Debug.DrawRay(start, dir, color);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void DrawRay(Vector3 start, Vector3 dir, Color color, float duration)
        {
            Debug.DrawRay(start, dir, color, duration);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void DrawRay(Vector3 start, Vector3 dir, Color color, float duration, bool depthTest)
        {
            Debug.DrawRay(start, dir, color, duration, depthTest);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void Break()
        {
            Debug.Break();
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public static void ClearDeveloperConsole()
        {
            Debug.ClearDeveloperConsole();
        }


        /// <summary>Size at which the active log rotates into <c>{name}.prev.log</c>. Only those two
        /// files are retained, so a session exceeding twice this size loses its earliest entries.</summary>
        private const long MaxBytes = 64 * 1024 * 1024;

        private static readonly object gate = new object();
        private static StreamWriter writer;
        private static string filePath;
        private static string prevPath;
        private static bool started;
        private static int mainThreadId;
        private static volatile int lastFrame;

        /// <summary>Absolute path of the active log file, or <see langword="null"/> before the file logger starts.</summary>
        public static string FilePath => filePath;

        private static string fileBaseName = "lightside";

        /// <summary>
        /// Base name of the log files in the Logs folder (<c>{name}.log</c> plus <c>{name}.prev.log</c>).
        /// A package claims its own name from its earliest load hook — an [InitializeOnLoad] static
        /// constructor in the editor, <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> in
        /// players — both of which run before the logger opens the file. A later change retargets the
        /// open log to the new path.
        /// </summary>
        public static string FileBaseName
        {
            get => fileBaseName;
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                lock (gate)
                {
                    if (value == fileBaseName) return;
                    fileBaseName = value;
                    if (filePath == null) return;
                    var dir = Path.GetDirectoryName(filePath);
                    filePath = Path.Combine(dir, fileBaseName + ".log");
                    prevPath = Path.Combine(dir, fileBaseName + ".prev.log");
                    if (writer != null)
                    {
                        try { Open(append: true); } catch { }
                    }
                }
            }
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void OnEditorLoad() => SetupFileLogger();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void OnRuntimeLoad() => SetupFileLogger();
        
        [Conditional("LIGHTSIDE_DEBUG")]
        private static void SetupFileLogger()
        {
            bool first = false;
            lock (gate)
            {
                if (!started)
                {
                    started = true;
                    first = true;
                    mainThreadId = Thread.CurrentThread.ManagedThreadId;
#if UNITY_EDITOR
                    captureExternal = UnityEditor.EditorPrefs.GetBool(CaptureExternalKey, true);
                    includeStack = UnityEditor.EditorPrefs.GetBool(IncludeStackKey, true);
#endif
                    try
                    {
                        var dir = Application.isEditor
                            ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs")
                            : Path.Combine(Application.persistentDataPath, "Logs");
                        Directory.CreateDirectory(dir);
                        filePath = Path.Combine(dir, fileBaseName + ".log");
                        prevPath = Path.Combine(dir, fileBaseName + ".prev.log");
                        Open(append: true);
                    }
                    catch { }

                    Application.logMessageReceivedThreaded -= OnLog;
                    Application.logMessageReceivedThreaded += OnLog;
#if UNITY_EDITOR
                    EditorLifecycle.ReloadCleanupCompleted -= OnBeforeReload;
                    EditorLifecycle.ReloadCleanupCompleted += OnBeforeReload;
                    EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                    EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
                }
                Session(Application.isPlaying ? "play" : "edit");
            }

            if (first && filePath != null)
                Debug.Log($"[CatLog] file logging active -> {filePath}");
        }

        /// <summary>Writes a distinctive marker line. Bracket a test run with it for one-grep retrieval.</summary>
        public static void Mark(string label)
        {
            lock (gate)
            {
                try { writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} | {FrameTag()} | M | ---- {label} ----"); }
                catch { }
            }
        }

        /// <summary>Full-width, immediately-flushed session-boundary banner (play enter/exit, recompile).
        /// The file accumulates runs across reloads, so a reader greps the last banner to isolate
        /// the current session.</summary>
        private static void Banner(string text)
        {
            lock (gate)
            {
                try { writer?.WriteLine($"======== {DateTime.Now:HH:mm:ss.fff} | {FrameTag()} | {text} ========"); writer?.Flush(); }
                catch { }
            }
        }

        /// <summary>Truncates the log and starts a fresh session. Call before a clean test run.</summary>
        public static void Clear()
        {
            lock (gate)
            {
                try { Open(append: false); Session("cleared"); }
                catch { }
            }
        }

        public static void Flush()
        {
            lock (gate) { try { writer?.Flush(); } catch { } }
        }

        private static void Open(bool append)
        {
            writer?.Dispose();
            var stream = new FileStream(filePath, append ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.ReadWrite);
            writer = new StreamWriter(stream) { AutoFlush = true };
        }

        private static void Session(string phase)
        {
            try { writer?.WriteLine($"==== SESSION {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {phase} ===="); }
            catch { }
        }

        /// <summary>Time.frameCount is main-thread-only; off-thread logs reuse the last value seen on the
        /// main thread, marked with "~". The read is guarded because Unity forbids engine-API calls during
        /// serialization — and the callback can fire there (e.g. an exception logged from OnAfterDeserialize).
        /// An unguarded throw here would abort the whole write under OnLog's catch and silently drop the entry;
        /// instead we fall back to the last frame, marked "!".</summary>
        private static string FrameTag()
        {
            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                try { lastFrame = Time.frameCount; }
                catch { return "F!" + lastFrame; }
                return "F" + lastFrame;
            }
            return "F~" + lastFrame;
        }

        private static volatile bool captureExternal = true;

        /// <summary>When true the file logger also records logs that don't originate in LightSide code (any
        /// <c>Debug.Log</c> in the project), not just first-party ones. On by default. Read off the logging
        /// thread, so it is mirrored to a plain field rather than read from EditorPrefs there.</summary>
        internal static bool CaptureExternal
        {
            get => captureExternal;
            set
            {
                captureExternal = value;
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetBool(CaptureExternalKey, value);
#endif
            }
        }

        private static volatile bool includeStack = true;

        /// <summary>When true the file logger appends a cleaned stack trace under every entry, not just
        /// errors. The logging frames (<c>Debug</c>, <see cref="Cat"/>, <see cref="CatZone"/>) are stripped
        /// so the first frame is the real call site. Errors/asserts always carry a stack regardless.</summary>
        internal static bool IncludeStack
        {
            get => includeStack;
            set
            {
                includeStack = value;
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetBool(IncludeStackKey, value);
#endif
            }
        }

#if UNITY_EDITOR
        private const string CaptureExternalKey = "LightSide.Cat.CaptureExternal";
        private const string IncludeStackKey = "LightSide.Cat.IncludeStack";
#endif

        private static bool IsOurs(string stack) =>
            !string.IsNullOrEmpty(stack) && stack.Contains("LightSide");

        private static bool IsLoggerFrame(string frame)
        {
            var f = frame.TrimStart();
            return f.StartsWith("UnityEngine.Debug:", StringComparison.Ordinal)
                || f.StartsWith("LightSide.Cat:", StringComparison.Ordinal)
                || f.StartsWith("LightSide.CatZone:", StringComparison.Ordinal);
        }

        /// <summary>Drops the leading logger frames and indents the remainder, leaving the call site on top.
        /// Empty when the stack is absent (stack-trace log type set to None) or only logger frames remain.</summary>
        private static string CleanStack(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return string.Empty;
            var lines = stack.Split('\n');
            int start = 0;
            while (start < lines.Length && IsLoggerFrame(lines[start])) start++;

            var sb = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();
                if (line.Length == 0) continue;
                if (sb.Length != 0) sb.Append('\n');
                sb.Append("    ").Append(line);
            }
            return sb.ToString();
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (!captureExternal && !IsOurs(stack)) return;
            lock (gate)
            {
                if (writer == null) return;
                try
                {
                    var lvl = type switch
                    {
                        LogType.Warning => 'W',
                        LogType.Error => 'E',
                        LogType.Exception => 'E',
                        LogType.Assert => 'A',
                        _ => 'I',
                    };
                    writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} | {FrameTag()} | {lvl} | {message}");
                    if (includeStack || lvl == 'E' || lvl == 'A')
                    {
                        var clean = CleanStack(stack);
                        if (clean.Length != 0) writer.WriteLine(clean);
                    }
                    if (writer.BaseStream.Length >= MaxBytes) Rotate();
                }
                catch { }
            }
        }

        private static void Rotate()
        {
            try
            {
                writer.Dispose();
                writer = null;
                if (File.Exists(prevPath)) File.Delete(prevPath);
                File.Move(filePath, prevPath);
                Open(append: false);
                Session("rotated");
            }
            catch { try { Open(append: true); } catch { } }
        }

        private static void OnBeforeReload()
        {
            Banner("RELOAD (recompile / domain reload)");
            Teardown();
        }

#if UNITY_EDITOR
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode) Banner("PLAY-ENTER");
            else if (change == PlayModeStateChange.ExitingPlayMode) Banner("PLAY-EXIT");
        }
#endif

        private static void Teardown()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            lock (gate)
            {
                try { writer?.Flush(); writer?.Dispose(); } catch { }
                writer = null;
                started = false;
            }
        }
    }
}
