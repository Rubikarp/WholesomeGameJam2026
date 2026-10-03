using System.Threading;
using System;
using System.Collections.Generic;
using UnityEngine.Audio;
using UnityEngine;
using Alchemy.Serialization;
using Alchemy.Inspector;
using PrimeTween;

public interface IAudioSettingsService
{
    public void SetVolume(EAudioChannel audioChannel, float newVolume);
    public void FadeVolume(EAudioChannel audioChannel, float targetVolume, float duration, Ease esasing = Ease.Linear);
    public Awaitable FadeVolumeAsync(EAudioChannel audioChannel, float targetVolume, float duration, CancellationToken cancellationToken, Ease esasing = Ease.Linear);

    public bool IsGameMuted { get; }
    public void MuteGame(bool isMuted);
    public void ToggleMuteGame();
}


[AlchemySerialize]
public partial class AudioSettingsService : Singleton<AudioSettingsService>, IAudioSettingsService
{
    [Header("Audio Mixers")]
    [SerializeField] private AudioMixerGroup _masterAudioMixer;
    [SerializeField] private AudioMixerGroup _musicAudioMixer;
    [SerializeField] private AudioMixerGroup _sfxAudioMixer;

    [field: Header("Audio Info")]
    [field: SerializeField] public bool IsGameMuted { get; private set; }
    [AlchemySerializeField, NonSerialized] private Dictionary<EAudioChannel, AudioMixerGroup> _channelMixers = new();
    [AlchemySerializeField, NonSerialized] private Dictionary<EAudioChannel, float> _channelVolumes = new();

    protected override void Awake()
    {
        base.Awake();

        _channelMixers[EAudioChannel.Master] = _masterAudioMixer;
        _channelMixers[EAudioChannel.Music] = _musicAudioMixer;
        _channelMixers[EAudioChannel.Sfx] = _sfxAudioMixer;
        
        _masterAudioMixer.audioMixer.GetFloat($"Volume_{Enum.GetName(typeof(EAudioChannel), EAudioChannel.Master)}", out float masterVolume);
        _musicAudioMixer.audioMixer.GetFloat($"Volume_{Enum.GetName(typeof(EAudioChannel), EAudioChannel.Music)}", out float musicVolume);
        _sfxAudioMixer.audioMixer.GetFloat($"Volume_{Enum.GetName(typeof(EAudioChannel), EAudioChannel.Sfx)}", out float sfxVolume);

        //Remap from -80 to 20 to 0 to 1
        masterVolume = DecibelToVolume(masterVolume);
        musicVolume = DecibelToVolume(musicVolume);
        sfxVolume = DecibelToVolume(sfxVolume);

        _channelVolumes[EAudioChannel.Master] = masterVolume;
        _channelVolumes[EAudioChannel.Music] = musicVolume;
        _channelVolumes[EAudioChannel.Sfx] = sfxVolume;
    }
    [Button]
    private void Start()
    {
        MuteGame(IsGameMuted);
    }
    public void SetVolume(EAudioChannel audioChannel, float newVolume)
    {
        if (_channelMixers.TryGetValue(audioChannel, out var mixer))
        {
            float logValue;
            string settingsNames = $"Volume_{Enum.GetName(typeof(EAudioChannel), audioChannel)}";

            if (newVolume > float.Epsilon)
            {
                logValue = VolumeToDecibel(newVolume);
                _channelVolumes[audioChannel] = newVolume;
            }
            else
            {
                logValue = -80f;
            }

            if (!mixer.audioMixer.SetFloat(settingsNames, logValue))
            {
                Debug.LogError($"Can't find parameter {settingsNames} inside audiomixer", mixer);
            }
        }
        else Debug.LogError($"Can't find audio miwer", this);
    }
    public void FadeVolume(EAudioChannel audioChannel, float targetVolume, float duration, Ease esasing = Ease.Linear)
    {
        if (_channelMixers.TryGetValue(audioChannel, out var mixer))
        {
            float currentVolume = _channelVolumes.TryGetValue(audioChannel, out var v) ? v : 1f;

            Tween.Custom(this, currentVolume, targetVolume, duration, 
                (target, x) => SetVolume(audioChannel, x),
                ease: esasing
                );
        }
    }
    public async Awaitable FadeVolumeAsync(EAudioChannel audioChannel, float targetVolume, float duration, CancellationToken cancellationToken, Ease esasing = Ease.Linear)
    {
        if (_channelMixers.TryGetValue(audioChannel, out var mixer))
        {
            float currentVolume = _channelVolumes.TryGetValue(audioChannel, out var v) ? v : 1f;
            await Tween.Custom(this, currentVolume, targetVolume, duration, 
                (target, x) => SetVolume(audioChannel, x),
                ease: esasing
            ).SetCancellationToken(cancellationToken);
        }
    }

    public void MuteGame(bool isMuted)
    {
        IsGameMuted = isMuted;
        foreach (var channel in _channelMixers.Keys)
        {
            if (isMuted)
            {
                SetVolume(channel, 0f);
            }
            else
            {
                SetVolume(channel, _channelVolumes.TryGetValue(channel, out var volume) ? volume : 1f);
            }
        }
    }
    [Button]
    public void ToggleMuteGame() => MuteGame(!IsGameMuted);

    /// <summary>
    /// Convert 0-1 to -80 to +20 dB (logarithmic)
    /// </summary>
    /// <param name="volume"></param>
    /// <returns></returns>
    public static float VolumeToDecibel(float volume)
    {
        if (volume <= float.Epsilon) return -80f;

        // Comme log10 * 20, multiplier par 10 ajouter 1*20 dB
        return Mathf.Log10(volume * 10f) * 20f;
    }   
    /// <summary>
    /// Convert -80 to +20 dB to 0-1 (linear)
    /// </summary>
    /// <param name="decibel"></param>
    /// <returns></returns>
    public static float DecibelToVolume(float decibel)
    {
        // Comme log10 * 20, diviser par 10 d'enlever 1*20 dB
        float linear = Mathf.Pow(10f, decibel / 20f) / 10f;
        return Mathf.Clamp01(linear);
    }
}
