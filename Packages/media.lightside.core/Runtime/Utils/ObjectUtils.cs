using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightSide
{
    /// <summary>Object lifetime and lookup helpers that paper over Unity API differences across versions and play modes.</summary>
    public static class ObjectUtils
    {
        /// <summary>Destroys <paramref name="obj"/> with <see cref="Object.Destroy(Object)"/> in play mode, <see cref="Object.DestroyImmediate(Object)"/> otherwise. Null-safe.</summary>
        public static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
        
        /// <summary><see cref="SafeDestroy(Object)"/> overload that can destroy asset objects in edit mode.</summary>
        public static void SafeDestroy(Object obj, bool allowDestroyAsset)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj, allowDestroyAsset);
        }

        /// <summary>Stable per-object id usable as a cache key across Unity versions (EntityId on 6000.4+, instance id before).</summary>
        public static int GetInstanceIdCompat(Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return (int)(EntityId.ToULong(obj.GetEntityId()) & uint.MaxValue);
#else
            return obj.GetInstanceID();
#endif
        }

        /// <summary>Raw scene handle usable as a cache key across Unity versions.</summary>
        public static ulong GetSceneHandleCompat(Scene scene)
        {
#if UNITY_6000_4_OR_NEWER
            return scene.handle.GetRawData();
#elif UNITY_6000_3_OR_NEWER
            return (uint)scene.handle;
#else
            return (ulong)scene.handle;
#endif
        }

        /// <summary>Finds any loaded object of type <typeparamref name="T"/> across Unity versions (<c>FindAnyObjectByType</c> on 2022.2+).</summary>
        public static T FindAny<T>() where T : Object
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindAnyObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        /// <summary><see cref="FindAny{T}()"/> overload that can also find objects on inactive GameObjects.</summary>
        public static T FindAny<T>(bool includeInactive) where T : Object
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindAnyObjectByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
            return Object.FindObjectOfType<T>(includeInactive);
#endif
        }

        /// <summary>Finds all loaded objects of type <typeparamref name="T"/> (unsorted) across Unity versions.</summary>
        public static T[] FindAll<T>() where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>();
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>();
#endif
        }
    }
}
