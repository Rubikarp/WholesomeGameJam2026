using System.Collections.Generic;
using UnityEngine;

namespace WorldGame.SceneManagement
{
	[System.Serializable]
	public class SceneHistoryStack
	{
		[SerializeField] private List<string> sceneStack = new();
		public bool CanGoBack => sceneStack.Count > 0;
		public int Count => sceneStack.Count;

		public void Push(string sceneName)
		{
			sceneStack.Add(sceneName);
		}

		public string Pop()
		{
			if (sceneStack.Count == 0)
			{
				Debug.LogWarning("[SceneHistoryStack] Pop appelé sur une pile vide.");
				return null;
			}

			var sceneName = sceneStack[^1];
			sceneStack.RemoveAt(sceneStack.Count - 1);
			return sceneName;
		}
		public string Peek()
		{
			if (sceneStack.Count == 0)
			{
				return null;
			}

			return sceneStack[^1];
		}

		public void Clear()
		{
			sceneStack.Clear();
		}
	}
}