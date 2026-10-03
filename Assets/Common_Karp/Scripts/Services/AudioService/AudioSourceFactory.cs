using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public interface IAudioSourceFactory
{
    public AudioSource GetAudioSource();
    public void ReleaseAudioSource(AudioSource audioSource);
    public List<AudioSource> GetAllActiveAudioSources();
}

public class AudioSourceFactory : MonoBehaviour, IAudioSourceFactory
{
    [SerializeField] private Transform audioSourceParent;
    [SerializeField] private ObjectPool<AudioSource> _audioSourcePool;
    private readonly List<AudioSource> _activeSources = new List<AudioSource>(50);

    #region Pooling Methods
    private AudioSource CreateAudioSource()
    {
        GameObject go = new GameObject("AudioSource_Inactive");
        go.transform.SetParent(audioSourceParent);

        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;

        return src;
    }
    private void OnGet(AudioSource src)
    {
        src.gameObject.SetActive(true);
        src.gameObject.name = "AudioSource_Active";
    }
    private void OnRelease(AudioSource src)
    {
        src.Stop();
        src.clip = null;
        src.loop = false;
        src.pitch = 1f;
        src.volume = 1f;
        src.gameObject.SetActive(false);
        src.gameObject.name = "AudioSource_Inactive";
    }
    private void OnDestroyPooledItem(AudioSource src)
    {
        GameObject.Destroy(src.gameObject);
    }
    #endregion

    private void Awake()
    //public AudioSourceFactory(AudioSource audioSourcePrefab)
    {
        _audioSourcePool = new ObjectPool<AudioSource>(
            createFunc: () => CreateAudioSource(),
            actionOnGet: OnGet,
            actionOnRelease: OnRelease,
            actionOnDestroy: OnDestroyPooledItem,
            collectionCheck: false,
            defaultCapacity: 10,
            maxSize: 50
        );
    }
    public AudioSource GetAudioSource()
    {
        var audioSource = _audioSourcePool.Get();
        _activeSources.Add(audioSource);
        return audioSource;
    }
    public void ReleaseAudioSource(AudioSource audioSource)
    {
        if (_activeSources.Contains(audioSource))
        {
            _activeSources.Remove(audioSource);
            _audioSourcePool.Release(audioSource);
        }
        else
        {
            Debug.LogWarning("Attempted to release an AudioSource that is not managed by this factory.", this);
        }
    }
    public List<AudioSource> GetAllActiveAudioSources() => new List<AudioSource>(_activeSources);
}
