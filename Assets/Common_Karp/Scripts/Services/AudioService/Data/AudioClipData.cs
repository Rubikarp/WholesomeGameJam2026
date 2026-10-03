using Alchemy.Inspector;
using UnityEngine;

[CreateAssetMenu(fileName = "AD_NewAudioFile", menuName = "AudioService/Audio Data", order = 1)]
public class AudioClipData : ScriptableObject
{
    [Header("Base Info")]
    [Required] public AudioClip clip;
    public bool isLooping = false;

    [Header("Volume")]
    [SerializeField] private bool hasVolumeVariation = false;
    [HideIf("hasVolumeVariation")]
    [SerializeField] private float volume = 1f;
    [ShowIf("hasVolumeVariation")]
    [SerializeField] private Vector2 volumeVariation = new Vector2(0.9f, 1.1f);

    [Header("Pitch")]
    [SerializeField] private bool hasPitchVariation = false;
    [HideIf("hasPitchVariation")]
    [SerializeField] private float pitch = 1f;
    [ShowIf("hasPitchVariation")]
    [SerializeField] private Vector2 pitchVariation = new Vector2(0.9f, 1.1f);

    public float GetPlayVolume()
    {
        if (hasVolumeVariation)
        {
            return Random.Range(volumeVariation.x, volumeVariation.y);
        }
        return volume;
    }
    public float GetPlayPitch()
    {
        if (hasPitchVariation)
        {
            return Random.Range(pitchVariation.x, pitchVariation.y);
        }
        return pitch;
    }
}
