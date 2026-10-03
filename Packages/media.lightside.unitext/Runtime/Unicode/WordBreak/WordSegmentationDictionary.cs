using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Self-contained dictionary asset for word segmentation of a specific script.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each asset holds a compiled Unicode trie and the target Unicode script.
    /// Add to UniText Settings to enable word segmentation for that script.
    /// </para>
    /// <para>
    /// Create via Tools → UniText → Tools → Dictionary Builder tab.
    /// </para>
    /// </remarks>
    [StateSource]
    public sealed partial class WordSegmentationDictionary : ScriptableObject
    {
        [SerializeField, HideInInspector, StateField]
        internal UnicodeScript script;

        [SerializeField, HideInInspector, StateField]
        internal byte[] trieData;

        /// <summary>The Unicode script inferred from the dictionary contents.</summary>
        public UnicodeScript Script => script;

        /// <summary>Returns true if the compiled trie and inferred target script are valid.</summary>
        public bool IsValid => script != UnicodeScript.Unknown &&
                               script != UnicodeScript.Common &&
                               script != UnicodeScript.Inherited &&
                               trieData != null && trieData.Length >= 12;

        internal void SetCompiledData(UnicodeScript valueScript, byte[] valueTrieData)
        {
            SetScriptState(valueScript);
            SetTrieDataState(valueTrieData);
        }
    }
}
