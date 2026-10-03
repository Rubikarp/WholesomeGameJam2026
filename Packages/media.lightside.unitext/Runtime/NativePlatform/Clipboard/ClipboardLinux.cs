#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LightSide
{
    /// <summary>
    /// Linux clipboard implementation using external tools (<c>xclip</c>, <c>xsel</c>,
    /// or <c>wl-copy</c>/<c>wl-paste</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Linux has no single clipboard API — X11 uses an asynchronous selection protocol,
    /// Wayland has a different data-device protocol. Both are complex to use directly.
    /// The pragmatic approach is to shell out to standard clipboard utilities:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>xclip -selection clipboard</c> (X11, most common)</item>
    ///   <item><c>xsel --clipboard</c> (X11 alternative)</item>
    ///   <item><c>wl-copy</c> / <c>wl-paste</c> (Wayland)</item>
    /// </list>
    /// <para>
    /// The implementation probes for available tools at first use and caches the result.
    /// If no tool is found, it falls back to <c>GUIUtility.systemCopyBuffer</c>.
    /// </para>
    /// <para>
    /// Every call here spawns a blocking subprocess — the facade therefore routes Linux
    /// through the <see cref="IAsyncClipboardProvider"/> seam (reads run on a worker via
    /// <c>Task.Run</c>); the sync entry points remain for the provider-agnostic path but
    /// stall the caller for the subprocess round-trip.
    /// </para>
    /// </remarks>
    internal static class ClipboardLinux
    {

        private enum ClipTool
        {
            Unknown,
            XClip,
            XSel,
            WlClipboard,
            None, 
        }

        private static ClipTool detectedTool = ClipTool.Unknown;

        /// <summary>
        /// Whether a clipboard tool (xclip / xsel / wl-clipboard) is installed. Resolves
        /// detection on the CALLING thread and caches it — the async provider seam must
        /// call this on the main thread before dispatching to a worker, because the
        /// no-tool fallback reads <c>GUIUtility.systemCopyBuffer</c>, a main-thread-only
        /// Unity API.
        /// </summary>
        public static bool HasNativeTool => DetectTool() != ClipTool.None;

        private static ClipTool DetectTool()
        {
            if (detectedTool != ClipTool.Unknown)
                return detectedTool;

            string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            bool isWayland = sessionType.EqualsIgnoreCase("wayland");

            if (isWayland && IsToolAvailable("wl-copy"))
            {
                detectedTool = ClipTool.WlClipboard;
                return detectedTool;
            }

            if (IsToolAvailable("xclip"))
            {
                detectedTool = ClipTool.XClip;
                return detectedTool;
            }

            if (IsToolAvailable("xsel"))
            {
                detectedTool = ClipTool.XSel;
                return detectedTool;
            }

            if (!isWayland && IsToolAvailable("wl-copy"))
            {
                detectedTool = ClipTool.WlClipboard;
                return detectedTool;
            }

            Debug.LogWarning("[UniText] No clipboard tool found (xclip, xsel, or wl-clipboard). " +
                             "Falling back to GUIUtility.systemCopyBuffer.");
            detectedTool = ClipTool.None;
            return detectedTool;
        }

        private static bool IsToolAvailable(string tool)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = tool,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return false;
                    if (!proc.WaitForExit(2000))
                    {
                        try { proc.Kill(); } catch {  }
                        return false;
                    }
                    return proc.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string GetText()
        {
            var tool = DetectTool();
            switch (tool)
            {
                case ClipTool.XClip:
                    return RunProcess("xclip", "-selection clipboard -o");
                case ClipTool.XSel:
                    return RunProcess("xsel", "--clipboard --output");
                case ClipTool.WlClipboard:
                    return RunProcess("wl-paste", "--no-newline");
                default:
                    return GUIUtility.systemCopyBuffer;
            }
        }

        public static void SetText(string text)
        {
            var tool = DetectTool();
            switch (tool)
            {
                case ClipTool.XClip:
                    RunProcessWithInput("xclip", "-selection clipboard", text ?? string.Empty);
                    break;
                case ClipTool.XSel:
                    RunProcessWithInput("xsel", "--clipboard --input", text ?? string.Empty);
                    break;
                case ClipTool.WlClipboard:
                    RunProcessWithInput("wl-copy", "", text ?? string.Empty);
                    break;
                default:
                    GUIUtility.systemCopyBuffer = text ?? string.Empty;
                    break;
            }
        }

        /// <summary>
        /// Availability probe. xclip / wl-paste answer from the offered TARGETS list (one
        /// subprocess, no payload transfer); xsel cannot list targets, so it is the one
        /// backend that must read the payload to answer.
        /// </summary>
        public static bool HasText()
        {
            switch (DetectTool())
            {
                case ClipTool.XClip:
                case ClipTool.WlClipboard:
                    var list = ListTargets();
                    if (string.IsNullOrEmpty(list)) return false;
                    foreach (var raw in list.Split('\n'))
                    {
                        var target = raw.Trim().TrimEnd('\r');
                        if (target == "UTF8_STRING" || target == "STRING" || target == "TEXT"
                            || target.StartsWith("text/plain", StringComparison.Ordinal))
                            return true;
                    }
                    return false;
                case ClipTool.XSel:
                    return !string.IsNullOrEmpty(GetText());
                default:
                    return !string.IsNullOrEmpty(GUIUtility.systemCopyBuffer);
            }
        }

        public static bool HasContent()
        {
            switch (DetectTool())
            {
                case ClipTool.XClip:
                case ClipTool.WlClipboard:
                    return !string.IsNullOrWhiteSpace(ListTargets());
                default:
                    return HasText();
            }
        }

        /// <summary>
        /// Output is drained asynchronously so the timeout can actually fire: a wedged
        /// X11 selection owner keeps the pipe open forever, and a synchronous ReadToEnd
        /// would block ahead of WaitForExit.
        /// </summary>
        /// <summary>
        /// Fire-and-forget stderr drain: stderr is redirected on every tool invocation (to keep
        /// tool warnings out of the player log), and an unread redirected pipe back-pressures
        /// the child at ~64KB — a warning-happy tool would then stall until the kill timeout.
        /// </summary>
        private static void DrainStderr(Process proc)
            => _ = proc.StandardError.ReadToEndAsync();

        private static string RunProcess(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return null;

                    DrainStderr(proc);
                    var readTask = proc.StandardOutput.ReadToEndAsync();
                    if (!proc.WaitForExit(3000))
                    {
                        try { proc.Kill(); } catch {  }
                        return null;
                    }

                    if (proc.ExitCode != 0)
                        return null;

                    if (!readTask.Wait(3000))
                        return null;

                    string output = readTask.Result;
                    return string.IsNullOrEmpty(output) ? null : output;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Linux clipboard read failed ({fileName}): {e.Message}");
                return null;
            }
        }

        private static void RunProcessWithInput(string fileName, string arguments, string input)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardInputEncoding = new System.Text.UTF8Encoding(false),
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return;

                    DrainStderr(proc);
                    proc.StandardInput.Write(input);
                    proc.StandardInput.Close();
                    if (!proc.WaitForExit(3000))
                    {
                        try { proc.Kill(); } catch {  }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Linux clipboard write failed ({fileName}): {e.Message}");
            }
        }

        /// <summary>
        /// Rejects identifiers the subprocess argument string cannot carry safely —
        /// <c>ProcessStartInfo.Arguments</c> splits on whitespace, so a
        /// <see cref="ClipboardFormat.Custom"/> identifier with a space would become two
        /// argv entries and read the wrong target.
        /// </summary>
        private static bool IsSafeIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            for (int i = 0; i < identifier.Length; i++)
            {
                char c = identifier[i];
                if (c <= ' ' || c == '"' || c == '\'') return false;
            }
            return true;
        }

        /// <summary>
        /// Reads any offered target as text (<c>xclip -t</c> / <c>wl-paste --type</c>) —
        /// HTML, Markdown, UniText source, and custom MIMEs all go through here. xsel has
        /// no target support and returns <see langword="null"/> for rich formats.
        /// </summary>
        public static string GetFormat(string identifier)
        {
            if (!IsSafeIdentifier(identifier)) return null;
            switch (DetectTool())
            {
                case ClipTool.XClip: return RunProcess("xclip", $"-selection clipboard -t {identifier} -o");
                case ClipTool.WlClipboard: return RunProcess("wl-paste", $"--type {identifier} --no-newline");
                default: return null;
            }
        }

        public static byte[] GetData(string identifier)
        {
            if (!IsSafeIdentifier(identifier)) return null;
            switch (DetectTool())
            {
                case ClipTool.XClip: return RunProcessReadBytes("xclip", $"-selection clipboard -t {identifier} -o");
                case ClipTool.WlClipboard: return RunProcessReadBytes("wl-paste", $"--type {identifier}");
                default: return null;
            }
        }

        /// <summary>Availability probe via the TARGETS list — one subprocess, no payload transfer. Prefer calling through the async provider seam: even the probe spawns a process.</summary>
        public static bool HasFormat(string identifier)
            => IsSafeIdentifier(identifier) && TargetOffered(identifier);

        public static bool HasFiles() => TargetOffered("text/uri-list");

        public static string[] GetFiles()
        {
            string list = DetectTool() switch
            {
                ClipTool.XClip => RunProcess("xclip", "-selection clipboard -t text/uri-list -o"),
                ClipTool.WlClipboard => RunProcess("wl-paste", "--type text/uri-list --no-newline"),
                _ => null,
            };
            if (string.IsNullOrEmpty(list)) return null;

            var lines = list.Split('\n');
            var paths = new System.Collections.Generic.List<string>(lines.Length);
            foreach (var raw in lines)
            {
                var line = raw.Trim().TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("file://", StringComparison.Ordinal))
                {
                    try { paths.Add(new Uri(line).LocalPath); } catch { }
                }
                else paths.Add(line);
            }
            return paths.Count == 0 ? null : paths.ToArray();
        }

        /// <summary>
        /// PLATFORM LIMIT: the clipboard tools serve ONE target per invocation, so a
        /// multi-format write degrades to its single most useful item: the image wins,
        /// then plain text (the floor every consumer reads), then the first remaining
        /// text item as the richest single format — an <c>[Html]</c>-only write carries
        /// the HTML instead of writing nothing. A failed image write falls through to
        /// the text ladder.
        /// </summary>
        public static bool SetItems(ClipboardWrite write)
        {
            if (write.HasImage && WriteData(write.Image.Format.Identifier, write.Image.Data))
                return true;
            if (write.HasPlain)
            {
                SetText(write.Plain.Text);
                return true;
            }
            var texts = write.Texts;
            for (int i = 0; i < texts.Count; i++)
            {
                var id = texts[i].Format.Identifier;
                if (IsSafeIdentifier(id) && WriteData(id, texts[i].Data)) return true;
            }
            return false;
        }

        /// <summary>No batched read on this platform — the provider reads per-format (each read is its own subprocess regardless).</summary>
        public static System.Collections.Generic.IReadOnlyList<ClipboardItem> GetItems(
            System.Collections.Generic.IReadOnlyList<ClipboardFormat> formats) => null;

        private static bool WriteData(string identifier, byte[] data)
        {
            if (!IsSafeIdentifier(identifier) || data == null || data.Length == 0) return false;
            switch (DetectTool())
            {
                case ClipTool.XClip: return RunProcessWriteBytes("xclip", $"-selection clipboard -t {identifier}", data);
                case ClipTool.WlClipboard: return RunProcessWriteBytes("wl-copy", $"--type {identifier}", data);
                default: return false;
            }
        }

        private static string ListTargets() => DetectTool() switch
        {
            ClipTool.XClip => RunProcess("xclip", "-selection clipboard -t TARGETS -o"),
            ClipTool.WlClipboard => RunProcess("wl-paste", "--list-types"),
            _ => null,
        };

        private static bool TargetOffered(string identifier)
        {
            string list = ListTargets();
            if (string.IsNullOrEmpty(list)) return false;
            foreach (var raw in list.Split('\n'))
                if (string.Equals(raw.Trim().TrimEnd('\r'), identifier, StringComparison.Ordinal)) return true;
            return false;
        }

        private static byte[] RunProcessReadBytes(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName, Arguments = arguments,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return null;
                    DrainStderr(proc);
                    using (var ms = new System.IO.MemoryStream())
                    {
                        var copyTask = proc.StandardOutput.BaseStream.CopyToAsync(ms);
                        if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { } return null; }
                        if (proc.ExitCode != 0) return null;
                        if (!copyTask.Wait(5000)) return null;
                        return ms.Length == 0 ? null : ms.ToArray();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Linux clipboard binary read failed ({fileName}): {e.Message}");
                return null;
            }
        }

        private static bool RunProcessWriteBytes(string fileName, string arguments, byte[] data)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName, Arguments = arguments,
                    RedirectStandardInput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return false;
                    DrainStderr(proc);
                    using (var stdin = proc.StandardInput.BaseStream)
                    {
                        stdin.Write(data, 0, data.Length);
                        stdin.Flush();
                    }
                    if (!proc.WaitForExit(3000))
                    {
                        try { proc.Kill(); } catch {  }
                        return false;
                    }
                    return proc.ExitCode == 0;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Linux clipboard binary write failed ({fileName}): {e.Message}");
                return false;
            }
        }
    }
}
#endif
