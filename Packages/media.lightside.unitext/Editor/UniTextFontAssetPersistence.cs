using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class UniTextFontAssetPersistence
    {
        /// <summary>Saves a freshly created font as an asset with its payload carrier persisted as a hidden sub-asset. The caller flushes with <see cref="AssetDatabase.SaveAssets"/>.</summary>
        internal static void Create(UniTextFont font, string assetPath)
        {
            AssetDatabase.CreateAsset(font, assetPath);
            var payload = font.EditorPayload;
            if (payload == null) return;
            payload.name = font.name;
            payload.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(payload, font);
            EditorUtility.SetDirty(font);
        }
    }
}
