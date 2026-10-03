using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class ObjectRefCompat
    {
        public static string Serialize(Object obj)
        {
            if (obj == null) return "0";
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(obj.GetEntityId()).ToString();
#else
            return ObjectUtils.GetInstanceIdCompat(obj).ToString();
#endif
        }

        public static Object Deserialize(string data)
        {
#if UNITY_6000_4_OR_NEWER
            var raw = ulong.Parse(data);
            return raw != 0UL ? Resolve(EntityId.FromULong(raw)) : null;
#else
            return Resolve(int.Parse(data));
#endif
        }

        /// <summary>Whether Unity's object registry still resolves the exact managed wrapper. Must be called from the main thread.</summary>
        public static bool IsRegistered(Object obj)
        {
            if (obj == null) return false;
#if UNITY_6000_4_OR_NEWER
            return ReferenceEquals(Resolve(obj.GetEntityId()), obj);
#else
            return ReferenceEquals(Resolve(ObjectUtils.GetInstanceIdCompat(obj)), obj);
#endif
        }

#if UNITY_6000_4_OR_NEWER
        public static Object Resolve(EntityId entityId)
            => EditorUtility.EntityIdToObject(entityId);
#else
        public static Object Resolve(int instanceId)
        {
#pragma warning disable CS0618
            return instanceId != 0 ? EditorUtility.InstanceIDToObject(instanceId) : null;
#pragma warning restore CS0618
        }
#endif
    }
}
