using UnityEngine;

public interface IChrono
{
    bool IsRunning { get; }
    float ElapsedSeconds { get; }

    void Start();
    void Stop();
    void Reset();
    void Pause();
    void Resume();
}

[System.Serializable]
public class Chrono : IChrono
{
    [SerializeField] private float _startTime = 0f;
    [SerializeField] private float _accumulatedSeconds = 0f;
    [SerializeField] private bool _isRunning = false;
    public bool IsRunning => _isRunning;
    public float ElapsedSeconds
    {
        get
        {
            if (_isRunning)
            {
                return _accumulatedSeconds + (Time.time - _startTime);
            }
            return _accumulatedSeconds;
        }
    }

    public void Start()
    {
        if (_isRunning) return;
        _startTime = Time.time;
        _isRunning = true;
    }
    public void Stop()
    {
        if (!_isRunning) return;
        _accumulatedSeconds += Time.time - _startTime;
        _isRunning = false;
    }
    public void Reset()
    {
        _accumulatedSeconds = 0f;
        _startTime = 0f;
        _isRunning = false;
    }
    public void Pause()
    {
        if (!_isRunning) return;
        _accumulatedSeconds += Time.time - _startTime;
        _isRunning = false;
    }
    public void Resume()
    {
        if (_isRunning) return;
        _startTime = Time.time;
        _isRunning = true;
    }

    public Chrono()
    {
        _startTime = 0f;
        _accumulatedSeconds = 0f;
        _isRunning = false;
    }
}
