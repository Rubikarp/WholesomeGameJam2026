using Alchemy.Inspector;
using UnityEngine;

[CreateAssetMenu(fileName = "Horse", menuName = "Horse Data")]
public class HorseData : ScriptableObject
{
    public string horseName;
    [Preview]
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
}