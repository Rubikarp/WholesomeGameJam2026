using System.Threading;
using UnityEngine;

public interface IAudioPlayerService
{
	public void PlayAudio(EAudioEventType audioClipType, EAudioChannel audioChannel);
	public Awaitable PlayAudioAsync(EAudioEventType audioClipType, EAudioChannel audioChannel, CancellationToken cancellationTokenSource);
	public void StopAllAudio();

	public void LoadAudioBank(AudioBank audioBank);
	public void UnloadAudioBank(AudioBank audioBank);
}