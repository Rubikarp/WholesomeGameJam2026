using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace LightSide.Samples
{
    /// <summary>
    /// Sample <see cref="MediaInputBehavior"/>: a chat-style attachment bar. A received image — pasted or
    /// dropped — or a received file (image previewed, anything else labelled by name) spawns an
    /// <see cref="AttachmentCard"/> into <see cref="attachmentsBar"/> above the field, the way Telegram /
    /// Slack / Discord show pending attachments. The card look lives in a prefab; this only feeds it data. Your
    /// own handler subclasses the same base and does whatever the app needs with the bytes.
    /// </summary>
    [Serializable]
    [TypeDescription("Sample: received media becomes attachment cards above the field")]
    [TypeGroup("Samples", 99)]
    public sealed class MediaPasteAttachments : MediaInputBehavior
    {
        [SerializeField, StatePassive]
        [Tooltip("Container with a Horizontal Layout Group where attachment cards appear.")]
        private RectTransform attachmentsBar;

        [SerializeField, StatePassive]
        [Tooltip("Attachment-card prefab: a RawImage thumbnail, a UniText label, and a remove button wired to AttachmentCard.Remove.")]
        private AttachmentCard cardPrefab;

        protected override bool OnImage(byte[] data, ClipboardFormat format)
        {
            if (!CanSpawn() || !TryDecode(data, out var tex)) return false;
            Spawn().ShowImage(tex);
            return true;
        }

        protected override bool OnFiles(string[] paths)
        {
            if (!CanSpawn()) return false;
            foreach (var path in paths)
            {
                var bytes = ReadFile(path);
                if (bytes != null && ClipboardFormat.TryDetectImage(bytes, out _) && TryDecode(bytes, out var tex))
                    Spawn().ShowImage(tex);
                else
                    Spawn().ShowFile(System.IO.Path.GetFileName(path));
            }
            return paths.Length > 0;
        }

        private bool CanSpawn() => cardPrefab != null && attachmentsBar != null;

        private AttachmentCard Spawn() => UObject.Instantiate(cardPrefab, attachmentsBar);

        private static bool TryDecode(byte[] data, out Texture2D tex)
        {
            tex = new Texture2D(2, 2);
            if (tex.LoadImage(data)) return true;
            UObject.Destroy(tex);
            tex = null;
            return false;
        }
    }
}
