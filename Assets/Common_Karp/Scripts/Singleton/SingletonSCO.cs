using UnityEngine;

/// <summary>
/// Abstract class for making reload-proof singletons out of ScriptableObjects
/// Returns the asset created on the editor, or null if there is none
/// Based on https://www.youtube.com/watch?v=VBA1QCoEAX4
/// </summary>
/// <typeparam name="T">Singleton type</typeparam>

public abstract class SingletonSCO<T> : ScriptableObject where T : ScriptableObject
{
    protected static T instance;
    public static bool HasInstance => instance != null;
    public static T TryGetInstance() => Resources.Load<T>(typeof(T).Name);

    public static T Instance
    {
        get
        {
            if (HasInstance)
            {
                instance = TryGetInstance();
                if (HasInstance)
                {
                    Debug.LogError($"SingletonSCO: {typeof(T).Name} not found in Resources. " +
                        $"Make sure it is properly placed in a Resources folder, and its name is the same as its script.");
                }
            }

            return instance;
        }
    }
}