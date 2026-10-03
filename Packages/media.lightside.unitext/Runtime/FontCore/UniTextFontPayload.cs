using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Serialized carrier of a font's compressed source bytes. Lives as an unreferenced hidden sub-asset
    /// of its font, so Player builds drop it by reachability while bundles pack it behind the
    /// <c>unitext/fonts/payload/&lt;token&gt;</c> container entry, deserialized only when that entry is
    /// loaded (scene-packed copies load with their scene). On player load it hands its bytes to
    /// <see cref="EmbeddedFontCatalog"/> keyed by <see cref="token"/> and clears <see cref="data"/>;
    /// the in-memory instance is empty from then on.
    /// </summary>
    internal sealed class UniTextFontPayload : ScriptableObject
    {
        [SerializeField] internal int token;
        [SerializeField] internal int rawLength;
        [SerializeField] internal byte[] sourceHash;
        [SerializeField] internal byte[] data;

        internal static UniTextFontPayload Create(int token, byte[] stored)
        {
            var payload = CreateInstance<UniTextFontPayload>();
            payload.token = token;
            payload.data = stored;
            payload.rawLength = Zstd.IsCompressed(stored)
                ? checked((int)Zstd.GetFrameContentSize(stored))
                : stored.Length;
#if UNITY_EDITOR
            using var sha = System.Security.Cryptography.SHA256.Create();
            payload.sourceHash = sha.ComputeHash(stored);
#endif
            return payload;
        }

#if !UNITY_EDITOR && !UNITY_WEBGL
        private void OnEnable()
        {
            if (data is not { Length: > 0 }) return;
            EmbeddedFontCatalog.RegisterPayload(token, sourceHash, rawLength, data);
            data = null;
        }
#endif
    }
}
