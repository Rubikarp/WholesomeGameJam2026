using Alchemy.Inspector;
using UnityEngine;

public class AudioEventSource : MonoBehaviour
{
    [Header("Audio Data")]
    [SerializeField] private EAudioEventType Data;
    [SerializeField] private EAudioChannel Channel = EAudioChannel.Sfx;

    [Button]
    public void PlaySound()
    {
        AudioPlayerService.Instance.PlayAudio(Data, Channel);
    }
    public void PlaySound(EAudioEventType audioEventType)
    {
        AudioPlayerService.Instance.PlayAudio(audioEventType, Channel);
    }   
    public void PlaySound(EAudioEventType audioEventType, EAudioChannel channel)
    {
        AudioPlayerService.Instance.PlayAudio(audioEventType, channel);
    }
}
