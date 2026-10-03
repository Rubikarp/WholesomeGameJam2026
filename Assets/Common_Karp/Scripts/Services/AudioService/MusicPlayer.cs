using Alchemy.Inspector;
using System.Threading;
using UnityEngine;

public class MusicPlayer : MonoBehaviour
{
    [SerializeField] private AudioPlayerService musicPlayerService;
    private CancellationTokenSource canceltokenSource;

    private void Awake()
    {
        musicPlayerService = AudioPlayerService.Instance;
    }

    [Button]

    //private void OnApplicationFocus(bool focus)
    private void OnApplicationPause(bool pause)
    {
        SetMusic(pause);
    }

    private void SetMusic(bool IsStop)
    {
        canceltokenSource?.Cancel();
        if (!IsStop)
        {
            canceltokenSource = new CancellationTokenSource();
            _ = musicPlayerService.PlayAudioAsync(EAudioEventType.Music_BG, EAudioChannel.Music, canceltokenSource.Token);
        }
    }
}
