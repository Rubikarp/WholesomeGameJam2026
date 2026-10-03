#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class ObjTracker : MonoBehaviour
    {
        [NonSerialized] public object obj;

        public static void Track(GameObject go, object obj)
        {
            if (!go.TryGetComponent(out ObjTracker tracker))
            {
                tracker = go.AddComponent<ObjTracker>();
            }

            tracker.obj = obj;
        }

        private void OnEnable()
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        private void OnUpdate()
        {
            if (this != null && obj == null)
            {
                EditorApplication.update -= OnUpdate;
                ObjectUtils.SafeDestroy(gameObject);
            }
        }
    }
}
#endif

