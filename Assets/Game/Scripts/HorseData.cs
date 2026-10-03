using UnityEngine;

[CreateAssetMenu(fileName = "Horse", menuName = "Horse Data")]
public class HorseData : ScriptableObject
{
    public enum RarityTier 
    {
        Common, 
        Uncommon, 
        Rare, 
        Epic, 
        Legendary 
    }
    public string horseName;
    public RarityTier rarity;
    [TextArea] public string horseDescription;
    public Sprite Visual;
    public Sprite VisualBestiary;
    public AnimationCurve approchePattern;
    public AnimationCurve tolerancePattern;
    public float distanceInitial;
    public float stressSensibility;
}