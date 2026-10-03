using UnityEngine;

namespace Core.Utils
{
	public class DontDestroyOnLoad : MonoBehaviour
	{
		void Awake()
		{
			transform.parent = null;
			DontDestroyOnLoad(gameObject);
		}
	}
}
