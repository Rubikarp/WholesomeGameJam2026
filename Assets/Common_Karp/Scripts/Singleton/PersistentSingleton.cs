using UnityEngine;
using Alchemy.Inspector;

public class PersistentSingleton<T> : Singleton<T> where T : Component
{
    [Tooltip("If true, the singleton will be unparented from any parent it has in the scene hierarchy on Awake.")]
    [SerializeField, FoldoutGroup("Persistent Singleton Settings")] public bool AutoUnparentOnAwake = true;

    protected override void InitializeSingleton()
    {
        if (!Application.isPlaying) return;

        if (AutoUnparentOnAwake) transform.SetParent(null);

        if (instance == null)
        {
            instance = this as T;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            if (instance != this)
            {
                Destroy(gameObject);
            }
        }
    }
}