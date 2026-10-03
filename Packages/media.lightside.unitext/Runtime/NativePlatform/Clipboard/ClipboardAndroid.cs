#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Android clipboard implementation using the JNI bridge (<c>AndroidJavaObject</c>).
    /// Wraps <c>android.content.ClipboardManager</c> obtained via
    /// <c>getSystemService(Context.CLIPBOARD_SERVICE)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Threading: reads (<c>getPrimaryClip</c>, <c>getPrimaryClipDescription</c>) run as
    /// direct JNI on the Unity script thread (Android 10+ additionally gates them on window
    /// focus); writes relay through the Java bridge's <c>setPrimaryClip</c> to the Android
    /// main thread (blocking, 2 s timeout) — <c>ClipboardManager.setPrimaryClip</c> is
    /// main-thread-bound and Unity's script thread is not the Android main thread. The
    /// manager's CONSTRUCTION needs a Looper: on API 22 and below construction on the Unity
    /// thread throws, so <see cref="GetClipboardManager"/> falls back to obtaining the
    /// service once via <c>Activity.runOnUiThread</c> with a bounded wait and caches it.
    /// </para>
    /// <para>
    /// Uses <c>ClipData.newPlainText(label, text)</c> for write and
    /// <c>getPrimaryClip().getItemAt(0).getText()</c> for read.
    /// </para>
    /// </remarks>
    internal static class ClipboardAndroid
    {
        /// <summary>
        /// In-process payloads of the custom-MIME formats from our most recent write, keyed by
        /// MIME identifier. Android's <c>ClipData.Item</c> has no per-MIME binary slot (the
        /// platform idiom for that is a <c>content://</c> ContentProvider), and Intent-extras on
        /// the clip do not survive cross-process reliably — so a vendor format (UniText source,
        /// markdown) advertised in the <c>ClipDescription</c> cannot carry its payload through the
        /// system clip. This cache restores the UniText↔UniText round-trip within the app: the
        /// clip still advertising our MIME is necessarily ours (no other app speaks it), and
        /// <see cref="cachedPlain"/> guards against serving a stale cache after a later plain write.
        /// Cross-app paste of a custom format is unreachable by construction and falls back to
        /// plain / HTML.
        /// </summary>
        private static readonly Dictionary<string, string> customCache = new(StringComparer.Ordinal);
        private static string cachedPlain;

        /// <summary>Volatile: assigned on the Android UI thread by the fallback path, read on the Unity thread — a stale read only costs a redundant re-acquisition.</summary>
        private static volatile AndroidJavaObject clipboardManager;

        /// <summary>
        /// In the UI-thread fallback the queued runnable owns the activity's lifetime and
        /// disposes it itself: on a UI-thread stall the 2s wait times out and returns while
        /// the runnable is still queued — disposing on this thread would hand the runnable
        /// a dead AndroidJavaObject.
        /// </summary>
        private static AndroidJavaObject GetClipboardManager()
        {
            var cached = clipboardManager;
            if (cached != null)
                return cached;

            var activity = CurrentActivity();
            if (activity == null) return null;
            try
            {
                clipboardManager = activity.Call<AndroidJavaObject>("getSystemService", "clipboard");
                activity.Dispose();
            }
            catch (Exception)
            {
                var waiter = new System.Threading.ManualResetEventSlim(false);
                try
                {
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try { clipboardManager = activity.Call<AndroidJavaObject>("getSystemService", "clipboard"); }
                        catch (Exception e) { Debug.LogWarning($"[UniText] Android clipboard service unavailable: {e.Message}"); }
                        finally
                        {
                            waiter.Set();
                            activity.Dispose();
                        }
                    }));
                }
                catch (Exception e)
                {
                    activity.Dispose();
                    Debug.LogWarning($"[UniText] Android clipboard service unavailable: {e.Message}");
                    return null;
                }
                if (!waiter.Wait(2000))
                    Debug.LogWarning("[UniText] Android clipboard service acquisition timed out.");
            }

            return clipboardManager;
        }

        public static string GetText()
        {
            try
            {
                var cm = GetClipboardManager();
                if (cm == null)
                    return null;

                if (!cm.Call<bool>("hasPrimaryClip"))
                    return null;

                using (var clip = cm.Call<AndroidJavaObject>("getPrimaryClip"))
                {
                    if (clip == null)
                        return null;

                    int itemCount = clip.Call<int>("getItemCount");
                    if (itemCount == 0)
                        return null;

                    using (var item = clip.Call<AndroidJavaObject>("getItemAt", 0))
                    {
                        if (item == null)
                            return null;

                        using (var charSequence = item.Call<AndroidJavaObject>("getText"))
                        {
                            if (charSequence == null)
                                return null;

                            return charSequence.Call<string>("toString");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Android clipboard read failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes plain text. An empty write CLEARS the clip on API 28+ (other apps' paste
        /// menus disable — the Windows/macOS/iOS empty-write semantics); below API 28 the
        /// platform has no clear call and an empty clip is written instead.
        /// </summary>
        public static void SetText(string text)
        {
            try
            {
                if (!string.IsNullOrEmpty(text) || !TryClearPrimaryClip())
                {
                    using (var clipDataClass = new AndroidJavaClass("android.content.ClipData"))
                    using (var clip = clipDataClass.CallStatic<AndroidJavaObject>(
                        "newPlainText", "UniText", text ?? string.Empty))
                    {
                        SetPrimaryClipViaBridge(clip);
                    }
                }
                customCache.Clear();
                cachedPlain = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Android clipboard write failed: {e.Message}");
            }
        }

        private static bool TryClearPrimaryClip()
        {
            try
            {
                using (var bridge = new AndroidJavaClass(Bridge))
                using (var activity = CurrentActivity())
                    return activity != null && bridge.CallStatic<bool>("clearPrimaryClip", activity);
            }
            catch
            {
                return false;
            }
        }

        public static bool HasText()
        {
            try
            {
                var cm = GetClipboardManager();
                if (cm == null)
                    return false;

                if (!cm.Call<bool>("hasPrimaryClip"))
                    return false;

                using (var description = cm.Call<AndroidJavaObject>("getPrimaryClipDescription"))
                {
                    if (description == null)
                        return false;

                    return description.Call<bool>("hasMimeType", "text/*");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Android clipboard check failed: {e.Message}");
                return false;
            }
        }

        public static bool HasContent()
        {
            try
            {
                var cm = GetClipboardManager();
                return cm != null && cm.Call<bool>("hasPrimaryClip");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Android clipboard check failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads a non-plain-text format from the primary clip. HTML pulls from
        /// <c>Item.getHtmlText()</c> (senders that use <c>ClipData.newHtmlText</c>). Vendor
        /// formats (UniText source, markdown) come from <see cref="customCache"/> when the clip
        /// still advertises the MIME and matches our last write — the same-app round-trip the
        /// platform clip cannot carry itself; otherwise <see langword="null"/> (the caller falls
        /// back to plain / HTML).
        /// </summary>
        public static string GetFormat(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            try
            {
                var cm = GetClipboardManager();
                if (cm == null) return null;
                if (!cm.Call<bool>("hasPrimaryClip")) return null;

                using (var description = cm.Call<AndroidJavaObject>("getPrimaryClipDescription"))
                {
                    if (description == null) return null;
                    if (!description.Call<bool>("hasMimeType", identifier)) return null;
                }

                using (var clip = cm.Call<AndroidJavaObject>("getPrimaryClip"))
                {
                    if (clip == null || clip.Call<int>("getItemCount") == 0) return null;
                    using (var item = clip.Call<AndroidJavaObject>("getItemAt", 0))
                    {
                        if (item == null) return null;
                        if (identifier == "text/html")
                            return item.Call<string>("getHtmlText");

                        using (var cs = item.Call<AndroidJavaObject>("getText"))
                        {
                            var plain = cs?.Call<string>("toString");
                            if (customCache.TryGetValue(identifier, out var payload)
                                && string.Equals(plain, cachedPlain, StringComparison.Ordinal))
                                return payload;
                            return null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Android clipboard format read failed: {e.Message}");
                return null;
            }
        }

        private const string Bridge = "com.unitext.input.UniTextClipboardBridge";

        private static AndroidJavaObject CurrentActivity()
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return up.GetStatic<AndroidJavaObject>("currentActivity");
        }

        /// <summary>
        /// Writes through the Java bridge, which relays <c>ClipboardManager.setPrimaryClip</c>
        /// to the Android main thread (blocking, 2 s timeout). ClipboardManager writes are
        /// main-thread-bound; Unity's script thread is not the Android main thread.
        /// </summary>
        private static bool SetPrimaryClipViaBridge(AndroidJavaObject clip)
        {
            using (var bridge = new AndroidJavaClass(Bridge))
            using (var activity = CurrentActivity())
                return activity != null && bridge.CallStatic<bool>("setPrimaryClip", activity, clip);
        }

        private static string PrimaryUri(AndroidJavaObject cm)
        {
            using (var clip = cm.Call<AndroidJavaObject>("getPrimaryClip"))
            {
                if (clip == null || clip.Call<int>("getItemCount") == 0) return null;
                using (var item = clip.Call<AndroidJavaObject>("getItemAt", 0))
                using (var uri = item?.Call<AndroidJavaObject>("getUri"))
                    return uri?.Call<string>("toString");
            }
        }

        public static byte[] GetData(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            try
            {
                var cm = GetClipboardManager();
                if (cm == null || !cm.Call<bool>("hasPrimaryClip")) return null;
                using (var desc = cm.Call<AndroidJavaObject>("getPrimaryClipDescription"))
                    if (desc == null || !desc.Call<bool>("hasMimeType", identifier)) return null;

                string uri = PrimaryUri(cm);
                if (uri == null) return null;
                using (var bridge = new AndroidJavaClass(Bridge))
                using (var activity = CurrentActivity())
                    return bridge.CallStatic<byte[]>("readUri", activity, uri);
            }
            catch (Exception e) { Debug.LogWarning($"[UniText] Android data read failed: {e.Message}"); return null; }
        }

        /// <summary>Availability probe via <c>ClipDescription.hasMimeType</c> — no payload transfer.</summary>
        public static bool HasFormat(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            try
            {
                var cm = GetClipboardManager();
                if (cm == null || !cm.Call<bool>("hasPrimaryClip")) return false;
                using (var desc = cm.Call<AndroidJavaObject>("getPrimaryClipDescription"))
                    return desc != null && desc.Call<bool>("hasMimeType", identifier);
            }
            catch { return false; }
        }

        /// <summary>
        /// A "file" on Android is a clip item carrying a <c>content://</c> URI — MIME
        /// heuristics misreport image blobs and integrator custom formats as files, three
        /// different ways from the desktop platforms.
        /// </summary>
        public static bool HasFiles()
        {
            try
            {
                var cm = GetClipboardManager();
                if (cm == null || !cm.Call<bool>("hasPrimaryClip")) return false;
                return PrimaryUri(cm) != null;
            }
            catch { return false; }
        }

        public static string[] GetFiles()
        {
            try
            {
                var cm = GetClipboardManager();
                if (cm == null || !cm.Call<bool>("hasPrimaryClip")) return null;
                string uri = PrimaryUri(cm);
                return uri == null ? null : new[] { uri };
            }
            catch { return null; }
        }

        public static byte[] ReadFile(string fileReference)
        {
            if (string.IsNullOrEmpty(fileReference)) return null;
            try
            {
                using (var bridge = new AndroidJavaClass(Bridge))
                using (var activity = CurrentActivity())
                    return bridge.CallStatic<byte[]>("readUri", activity, fileReference);
            }
            catch (Exception e) { Debug.LogWarning($"[UniText] Android file read failed: {e.Message}"); return null; }
        }

        private static bool WriteImage(string mime, byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            try
            {

                using (var bridge = new AndroidJavaClass(Bridge))
                using (var activity = CurrentActivity())
                    return !string.IsNullOrEmpty(bridge.CallStatic<string>("writeImageToClip", activity, mime, data));
            }
            catch (Exception e) { Debug.LogWarning($"[UniText] Android image write failed: {e.Message}"); return false; }
        }

        /// <summary>
        /// Multi-format atomic write. Packs plain text + HTML into a single
        /// <c>ClipData</c> via <c>ClipData.newHtmlText</c> (sender's standard idiom on
        /// Android); additional MIMEs are advertised in the ClipDescription with payloads
        /// carried by <see cref="customCache"/>. PLATFORM LIMIT: the image slot wins the
        /// whole write (Android's clip carries the image as a content URI via the bridge;
        /// <c>ClipData</c> cannot attach text to the URI item) — text items alongside it
        /// are dropped; a failed image write falls through to the text path. The
        /// plain-text floor is promoted only from text-like (<c>text/*</c>) formats —
        /// vendor payloads (UniText fragment JSON) must never leak into other apps as
        /// plain text.
        /// </summary>
        public static bool SetItems(ClipboardWrite write)
        {
            if (write.HasImage && WriteImage(write.Image.Format.Identifier, write.Image.Data))
                return true;

            string plain = write.HasPlain ? write.Plain.Text : null;
            string html = write.HasHtml ? write.Html.Text : null;
            var customMimes = new List<string>();
            var customPayloads = new List<string>();

            var texts = write.Texts;
            for (int i = 0; i < texts.Count; i++)
            {
                var item = texts[i];
                if (item.Format == ClipboardFormat.PlainText || item.Format == ClipboardFormat.Html) continue;
                var id = item.Format.Identifier;
                if (string.IsNullOrEmpty(id)) continue;
                string text = item.Text;
                customMimes.Add(id);
                customPayloads.Add(text);
                if (plain == null && id.StartsWith("text/", StringComparison.Ordinal))
                    plain = text;
            }

            if (plain == null && html == null) return false;
            plain ??= html ?? string.Empty;

            try
            {
                AndroidJavaObject clip = customMimes.Count == 0
                    ? BuildSimpleClip(plain, html)
                    : BuildClipWithCustomMimes(plain, html, customMimes);

                if (clip == null) return false;
                using (clip)
                {
                    if (!SetPrimaryClipViaBridge(clip)) return false;
                }

                customCache.Clear();
                cachedPlain = plain;
                for (int i = 0; i < customMimes.Count; i++)
                    customCache[customMimes[i]] = customPayloads[i];
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniText] Android clipboard write failed: {e.Message}");
                return false;
            }
        }

        /// <summary>No batched read on this platform — the provider reads per-format.</summary>
        public static IReadOnlyList<ClipboardItem> GetItems(IReadOnlyList<ClipboardFormat> formats) => null;

        private static AndroidJavaObject BuildSimpleClip(string plain, string html)
        {
            using (var clipDataClass = new AndroidJavaClass("android.content.ClipData"))
            {
                return !string.IsNullOrEmpty(html)
                    ? clipDataClass.CallStatic<AndroidJavaObject>("newHtmlText", "UniText", plain, html)
                    : clipDataClass.CallStatic<AndroidJavaObject>("newPlainText", "UniText", plain);
            }
        }

        private static AndroidJavaObject BuildClipWithCustomMimes(string plain, string html, List<string> customMimes)
        {
            var mimes = new string[1 + (string.IsNullOrEmpty(html) ? 0 : 1) + customMimes.Count];
            int m = 0;
            mimes[m++] = "text/plain";
            if (!string.IsNullOrEmpty(html)) mimes[m++] = "text/html";
            for (int i = 0; i < customMimes.Count; i++) mimes[m++] = customMimes[i];

            using (var description = new AndroidJavaObject("android.content.ClipDescription", "UniText", mimes))
            using (var firstItem = !string.IsNullOrEmpty(html)
                ? new AndroidJavaObject("android.content.ClipData$Item", plain, html)
                : new AndroidJavaObject("android.content.ClipData$Item", plain))
            {
                return new AndroidJavaObject("android.content.ClipData", description, firstItem);
            }
        }
    }
}
#endif
