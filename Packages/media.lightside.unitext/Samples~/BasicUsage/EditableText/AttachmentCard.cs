using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Samples
{
    /// <summary>
    /// One card in the <see cref="MediaPasteAttachments"/> bar. The prefab owns the look; this binds a pasted
    /// image into <see cref="image"/> or a file name into <see cref="label"/>, and frees the runtime texture
    /// when the card is destroyed. Wire the remove button to <see cref="Remove"/> in the prefab.
    /// </summary>
    public sealed class AttachmentCard : MonoBehaviour
    {
        [SerializeField] private RawImage image;
        [SerializeField] private UniText label;

        private Texture2D owned;

        public void ShowImage(Texture2D texture)
        {
            owned = texture;
            if (image != null) { image.texture = texture; image.gameObject.SetActive(true); }
            if (label != null) label.gameObject.SetActive(false);
        }

        public void ShowFile(string name)
        {
            if (label != null) { label.SetText(name); label.gameObject.SetActive(true); }
            if (image != null) image.gameObject.SetActive(false);
        }

        public void Remove() => Destroy(gameObject);

        private void OnDestroy()
        {
            if (owned != null) Destroy(owned);
        }
    }
}
