using UnityEngine;

[CreateAssetMenu(fileName = "Horse", menuName = "Horse Data")]
public class HorseData : ScriptableObject
{
    [SerializeField] private string horseName;
    [SerializeField] private Sprite horseSprite;
    [SerializeField] private Sprite horseSpriteBestiary; //on pourra juste cropper genre la tête du cheval pour la mettre dans le bestiaire, ou l'enlever, je l'ai mis juste au cas où
    [SerializeField, Range(0f, 100f)] private float captureDifficulty;

    public string HorseName => horseName;
    public Sprite HorseSprite => horseSprite;
    public Sprite HorseSpriteBestiary => horseSpriteBestiary;
    public float CaptureDifficulty => captureDifficulty;
}
