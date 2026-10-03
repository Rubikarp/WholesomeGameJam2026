using System.Collections.Generic;
using System.Threading;
using System;
using UnityEngine.Audio;
using UnityEngine;

public class AudioPlayerService : Singleton<AudioPlayerService>, IAudioPlayerService
{
    [Header("Audio Mixers")]
    [SerializeField] private AudioMixerGroup _masterAudioMixer;
    [SerializeField] private AudioMixerGroup _musicAudioMixer;
    [SerializeField] private AudioMixerGroup _sfxAudioMixer;
    private Dictionary<EAudioChannel, AudioMixerGroup> _channelMixers = new();

    [Header("Audio Files")]
    [SerializeField] private List<AudioBank> _audioBanks = new();

    [Header("Audio Configuration")]
    [SerializeField] private AudioSourceFactory _audioSourceFactory;

    protected override void Awake()
    {
        base.Awake();
        _channelMixers[EAudioChannel.Master] = _masterAudioMixer;
        _channelMixers[EAudioChannel.Music] = _musicAudioMixer;
        _channelMixers[EAudioChannel.Sfx] = _sfxAudioMixer;
    }

    public void LoadAudioBank(AudioBank audioBank)
    {
        _audioBanks.Add(audioBank);
    }
    public void UnloadAudioBank(AudioBank audioBank)
    {
        _audioBanks.Remove(audioBank);
    }


    public void PlayAudio(EAudioEventType audioClipType, EAudioChannel audioChannel)
    {
        if (CanPlayAudioClip(audioClipType, out var audioData))
        {
            _ = PlayAudioData(audioChannel, audioData);
        }
    }
    public async Awaitable PlayAudioAsync(EAudioEventType audioClipType, EAudioChannel audioChannel, CancellationToken cancellationToken)
    {
        if (CanPlayAudioClip(audioClipType, out var audioData))
        {
            await PlayAudioData(audioChannel, audioData, cancellationToken);
        }
    }

    private async Awaitable PlayAudioData(EAudioChannel audioChannel, AudioClipData audioData, CancellationToken cancellationToken = default)
    {
        if (audioData.clip == null)
        {
            Debug.LogError($"No audio clup for {audioData.ToString()}", audioData);
            return;
        }

        try
        {
            var audioSource = _audioSourceFactory.GetAudioSource();
            audioSource.clip = audioData.clip;
            audioSource.loop = audioData.isLooping;
            audioSource.pitch = audioData.GetPlayPitch();
            audioSource.volume = audioData.GetPlayVolume();
            audioSource.outputAudioMixerGroup = _channelMixers[audioChannel];
            audioSource.Play();
            //Debug.Log($"Played Audio {audioData.clip.name} for channel {audioChannel}", this);

            do
            {
                float duration = audioSource.clip.length / audioSource.pitch;
                await Awaitable.WaitForSecondsAsync(duration, cancellationToken);
            }
            while (audioSource.loop && audioSource.isPlaying);

            audioSource.Stop();
            _audioSourceFactory.ReleaseAudioSource(audioSource);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("PlayAudioData was cancelled", this);
        }
    }
    private bool CanPlayAudioClip(EAudioEventType audioClipType, out AudioClipData audioClipData)
    {
        audioClipData = default;
        return TryGetAudioClip(audioClipType, out audioClipData);
    }
    private bool TryGetAudioClip(EAudioEventType audioClipType, out AudioClipData audioClip)
    {
        foreach (var audioBank in _audioBanks)
        {
            if (audioBank.AudioClipsData.TryGetValue(audioClipType, out audioClip))
            {
                return true;
            }
        }

        Debug.LogError($"No clip of name {audioClipType} found");
        audioClip = null;
        return false;
    }

    public void StopAllAudio()
    {
        Debug.Log("Stop all audio");
        var activeAudioSources = _audioSourceFactory.GetAllActiveAudioSources();
        foreach (var audioSource in activeAudioSources)
        {
            audioSource.Stop();
        }
    }
}
