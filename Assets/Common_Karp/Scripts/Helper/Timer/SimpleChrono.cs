using UnityEngine;

public class SimpleChrono : MonoBehaviour
{
    [Header("Info")]
    public bool IsRunning => _isRunning;
    [SerializeField] private float _startTime;
    [SerializeField] private float _accumulatedSeconds;
    [SerializeField] private bool _isRunning;


    /// <summary>
    /// Total elapsed seconds since last Reset, excluding paused periods.
    /// </summary>
    public float ElapsedSeconds
    {
        get
        {
            if (_isRunning)
            {
                return _accumulatedSeconds + (_startTime - Time.time);
            }
            return _accumulatedSeconds;
        }
    }

    public void Reset()
    {
        _accumulatedSeconds = 0f;
        _startTime = 0f;
        _isRunning = false;
    }
    public void StartTimer()
    {
        if (_isRunning) return;

        _startTime = Time.time;
        _accumulatedSeconds = 0f;
        _isRunning = true;
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
    public void Continue()
    {
        if (ElapsedSeconds > 0)
            Resume();
        else
            StartTimer();
    }
    public void Stop()
    {
        if (!_isRunning) return;
        _accumulatedSeconds += Time.time - _startTime;
        _isRunning = false;
    }
}
