using Alchemy.Inspector;
using UnityEngine;

[CreateAssetMenu(fileName = "Horse", menuName = "Horse Data")]
public class HorseData : ScriptableObject
{
    public string horseName;
    [Preview(size:200)]
    public Sprite Visual;
    [Preview]
    public Sprite VisualBestiary;
    public RarityTier rarity;
    [TextArea] public string horseDescription;
    [Space] 
    public float distanceInitial = 30f;
    [Range(0f, 2f)] public float stressSensibility = .5f;
    [Space] 
    public AnimationCurve approchePattern = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public AnimationCurve tolerancePattern = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    
    [Button]
    public void GeneratePatternRandomCurve()
    {
        approchePattern = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        
        int nbPoint = Random.Range(3, 13);
        Keyframe[] times = new Keyframe[nbPoint];
        for (int i = 0; i < nbPoint; i++)
        {
            times[i] = new Keyframe(i / (float)(nbPoint - 1), Random.Range(0f, 1f));
        }

        times[nbPoint - 1].value = times[0].value;
        approchePattern = new AnimationCurve(times);
    }
    
#if UNITY_EDITOR
    [Button()]
    private void Rename()
    {
        if (string.IsNullOrWhiteSpace(horseName))
        {
            Debug.LogWarning("horseName est vide, renommage annulé.", this);
            return;
        }
        
        string newName = $"HorseData_{horseName.Trim()}";
        if (name == newName) return;
        
        string path = UnityEditor.AssetDatabase.GetAssetPath(this);
        string error = UnityEditor.AssetDatabase.RenameAsset(path, newName);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"Renommage impossible : {error}", this);
            return;
        }
        UnityEditor.AssetDatabase.SaveAssets();
    }
#endif
}