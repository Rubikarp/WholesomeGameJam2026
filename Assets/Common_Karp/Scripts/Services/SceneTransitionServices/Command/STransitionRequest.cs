using NaughtyAttributes;
using UnityEngine;
using System;
using Object = UnityEngine.Object;

namespace WorldGame.SceneManagement
{
	[Serializable]
	public struct STransitionRequest
	{
		[Header("Info")] 
		[Scene] public string SceneName;
		public ETransitionEffect Effect;

		[Header("Optional Data")] 
		public bool PushToHistory;
		public ScriptableObject Payload;

		public STransitionRequest(string sceneName,
			ETransitionEffect effect = ETransitionEffect.InkFade,
			ScriptableObject payload = null,
			bool pushToHistory = true)
		{
			SceneName = sceneName;
			Effect = effect;
			PushToHistory = pushToHistory;
			Payload = payload;
		}

		public STransitionRequest(STransitionRequest source)
		{
			SceneName = source.SceneName;
			Effect = source.Effect;
			PushToHistory = source.PushToHistory;
			Payload = source.Payload;
		}

		public bool IsValid => !string.IsNullOrEmpty(SceneName);
	}
}