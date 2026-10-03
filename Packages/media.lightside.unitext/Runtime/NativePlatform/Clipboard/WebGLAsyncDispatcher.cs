#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace LightSide
{
    /// <summary>
    /// Receives the <c>.jslib</c>'s async clipboard results via <c>SendMessage</c> and
    /// completes the pending Tasks. Every pending request carries a deadline: a lost JS
    /// callback (dispatcher destroyed by scene-cleanup code, SendMessage throwing) resolves
    /// to null / the capture-cache fallback after <see cref="TimeoutSeconds"/> instead of
    /// hanging the awaiting paste forever and leaking the entry.
    /// </summary>
    internal sealed class WebGLAsyncDispatcher : MonoBehaviour
    {
        private const string GameObjectName = "UniTextWebGLAsyncDispatcher";
        private const float TimeoutSeconds = 10f;

        private struct PendingText
        {
            public TaskCompletionSource<string> tcs;
            public float deadline;
        }

        private struct PendingItems
        {
            public TaskCompletionSource<IReadOnlyList<ClipboardItem>> tcs;
            public IReadOnlyList<ClipboardFormat> formats;
            public float deadline;
        }

        private static WebGLAsyncDispatcher instance;
        private static readonly Dictionary<int, PendingText> pendingText = new();
        private static readonly Dictionary<int, PendingItems> pendingItems = new();
        private static readonly List<int> expiredScratch = new();
        private static int nextRequestId;
        private static System.Action tickCallback;
        private static TickHandle tickHandle;

        private static void RefreshTick() =>
            CoreLoop.Updating.Toggle(ref tickHandle, tickCallback ??= Tick,
                pendingText.Count + pendingItems.Count > 0);

        public static int RegisterText(TaskCompletionSource<string> tcs)
        {
            EnsureInstance();
            var id = ++nextRequestId;
            pendingText[id] = new PendingText { tcs = tcs, deadline = Time.realtimeSinceStartup + TimeoutSeconds };
            RefreshTick();
            return id;
        }

        /// <summary>
        /// Landing pad for the jslib read-all contract (<c>UniTextClipboard_RequestAsyncReadAll</c>,
        /// specified in <c>integration/webgl-native.md</c>) — <see cref="OnAsyncItemsResolved"/>
        /// completes these once the jslib entry point ships.
        /// </summary>
        public static int RegisterItems(TaskCompletionSource<IReadOnlyList<ClipboardItem>> tcs, IReadOnlyList<ClipboardFormat> formats)
        {
            EnsureInstance();
            var id = ++nextRequestId;
            pendingItems[id] = new PendingItems { tcs = tcs, formats = formats, deadline = Time.realtimeSinceStartup + TimeoutSeconds };
            RefreshTick();
            return id;
        }

        private static void EnsureInstance()
        {
            if (instance != null) return;
            var go = new GameObject(GameObjectName) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<WebGLAsyncDispatcher>();
        }

        private static void Tick()
        {
            float now = Time.realtimeSinceStartup;

            expiredScratch.Clear();
            foreach (var kv in pendingText)
                if (now >= kv.Value.deadline) expiredScratch.Add(kv.Key);
            for (int i = 0; i < expiredScratch.Count; i++)
            {
                var entry = pendingText[expiredScratch[i]];
                pendingText.Remove(expiredScratch[i]);
                entry.tcs.TrySetResult(null);
            }

            expiredScratch.Clear();
            foreach (var kv in pendingItems)
                if (now >= kv.Value.deadline) expiredScratch.Add(kv.Key);
            for (int i = 0; i < expiredScratch.Count; i++)
            {
                var entry = pendingItems[expiredScratch[i]];
                pendingItems.Remove(expiredScratch[i]);
                entry.tcs.TrySetResult(ClipboardWebGL.ReadCapturedItems(entry.formats));
            }

            RefreshTick();
        }

        [Preserve]
        private void OnAsyncTextResolved(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            int sep = message.IndexOf('|');
            if (sep < 0) return;
            if (!int.TryParse(message.Substring(0, sep), out var id)) return;

            string text = sep + 1 < message.Length ? message.Substring(sep + 1) : null;
            if (string.IsNullOrEmpty(text)) text = null;

            if (pendingText.TryGetValue(id, out var entry))
            {
                pendingText.Remove(id);
                RefreshTick();
                entry.tcs.TrySetResult(text);
            }
        }

        /// <summary>
        /// Wire format (see the read-all contract in the <c>.jslib</c>):
        /// <c>requestId|count|fmtLen|fmt payloadLen|payload …</c>, all lengths in UTF-16
        /// code units. Length-prefixed rather than delimiter-separated because payloads are
        /// arbitrary clipboard text. A recoverable id with a malformed body resolves to the
        /// capture-cache fallback rather than staying pending until the timeout.
        /// </summary>
        [Preserve]
        private void OnAsyncItemsResolved(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            int pos = 0;
            if (!ReadInt(message, ref pos, out var id)) return;
            if (!pendingItems.TryGetValue(id, out var entry)) return;
            pendingItems.Remove(id);
            RefreshTick();

            var items = new List<ClipboardItem>(4);
            if (ReadInt(message, ref pos, out var count) && count >= 0 && count <= 64)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!ReadChunk(message, ref pos, out var format)) { items = null; break; }
                    if (!ReadChunk(message, ref pos, out var payload)) { items = null; break; }
                    if (!string.IsNullOrEmpty(format) && !string.IsNullOrEmpty(payload))
                        items.Add(new ClipboardItem(ClipboardFormat.Custom(format), payload));
                }
            }
            else items = null;

            entry.tcs.TrySetResult(items != null && items.Count > 0
                ? items
                : ClipboardWebGL.ReadCapturedItems(entry.formats));
        }

        private static bool ReadInt(string message, ref int pos, out int value)
        {
            value = 0;
            int sep = message.IndexOf('|', pos);
            if (sep < 0 || sep == pos) return false;
            if (!int.TryParse(message.Substring(pos, sep - pos), out value)) return false;
            pos = sep + 1;
            return true;
        }

        private static bool ReadChunk(string message, ref int pos, out string chunk)
        {
            chunk = null;
            if (!ReadInt(message, ref pos, out var length)) return false;
            if (length < 0 || pos + length > message.Length) return false;
            chunk = message.Substring(pos, length);
            pos += length;
            return true;
        }
    }
}
#endif
