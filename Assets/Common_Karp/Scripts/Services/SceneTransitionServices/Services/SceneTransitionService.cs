using System.Collections.Generic;
using System.Threading;
using System;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using UnityEngine;
using Alchemy.Serialization;
using Alchemy.Inspector;

namespace WorldGame.SceneManagement
{
	[AlchemySerialize]
	public partial class SceneTransitionService : MonoBehaviour, ISceneTransitionService
	{
		[Header("Effects")] 
		[AlchemySerializeField, NonSerialized]
		private Dictionary<ETransitionEffect, ITransitionEffect> transitionToEffects = new();
		
		[Header("Events")] 
		public UnityAction<STransitionRequest> OnTransitionStarted { get; set; }
		public UnityAction<ScriptableObject> OnLoadTransitonData { get; set; }
		public UnityAction OnTransitionCompleted { get; set; }

		[Header("Debug")] 
		[SerializeField] private SceneHistoryStack _historyStack = new SceneHistoryStack();
		[SerializeField] private STransitionRequest _currentRequest;
		[SerializeField] private bool isTransitioning;
		public bool CanGoBack => _historyStack.CanGoBack && !isTransitioning;

		[Button]
		public void RequestTransition(STransitionRequest request) =>
			_ = LaunchTransitionToAsync(request, destroyCancellationToken);

		[Button]
		public void RequestGoBack() => _ = GoBackAsync(destroyCancellationToken);

		public async Awaitable GoBackAsync(CancellationToken ct)
		{
			if (!CanGoBack)
			{
				Debug.LogWarning("[SceneTransitionService] GoBack impossible : pile vide ou transition en cours.");
				return;
			}

			var scene = _historyStack.Pop();
			// Si scene actuel selectionner celle encore avant
			if (scene == SceneManager.GetActiveScene().name)
			{
				scene = _historyStack.Pop();
			}

			if (string.IsNullOrEmpty(scene))
			{
				Debug.LogWarning($"[SceneTransitionService] GoBack impossible : scene '{scene}' inconnue.");
				return;
			}

			var request = new STransitionRequest(scene,
				ETransitionEffect.InkFade,
				null,
				false);
			await LaunchTransitionToAsync(request, ct);
		}

		public async Awaitable LaunchTransitionToAsync(STransitionRequest request, CancellationToken ct)
		{
			isTransitioning = true;
			_currentRequest = request;
			OnTransitionStarted?.Invoke(request);

			_historyStack.Push(request.SceneName);
			var sceneLoading = SceneManager.LoadSceneAsync(request.SceneName);
			if (request.Effect != ETransitionEffect.None && transitionToEffects.ContainsKey(request.Effect))
			{
				var transitionEffect = transitionToEffects[request.Effect];
				Debug.Log($" [SceneTransitionService] Transition avec effet {request.Effect}");
				await TransitionWithEffect(transitionEffect, sceneLoading, ct);
			}
			else
			{
				await TransitionWithoutEffect(sceneLoading, ct);
			}

			isTransitioning = false;

			if (request.Payload != null) OnLoadTransitonData?.Invoke(request.Payload);
			OnTransitionCompleted?.Invoke();
		}

		private async Awaitable TransitionWithoutEffect(AsyncOperation sceneLoading, CancellationToken ct)
		{
			sceneLoading.allowSceneActivation = false;
			await Awaitable.NextFrameAsync(ct);

			// allowSceneActivation=false bloque le chargement à 0.9 et isDone reste false.
			// On attend donc le palier 0.9, pas isDone.
			while (sceneLoading.progress < 0.9f) await Awaitable.NextFrameAsync(ct);

			sceneLoading.allowSceneActivation = true;
			while (!sceneLoading.isDone) await Awaitable.NextFrameAsync(ct);
		}

		private async Awaitable TransitionWithEffect(ITransitionEffect effect, AsyncOperation sceneLoading,
			CancellationToken ct)
		{
			sceneLoading.allowSceneActivation = false;
			await effect.PlayInAsync(ct);

			// allowSceneActivation=false bloque le chargement à 0.9 et isDone reste false.
			// On attend le palier 0.9 en rapportant la progression normalisée (0..1).
			while (sceneLoading.progress < 0.9f)
			{
				effect.ReactToProgress(sceneLoading.progress / 0.9f);
				await Awaitable.NextFrameAsync(ct);
			}

			effect.ReactToProgress(1f);
			sceneLoading.allowSceneActivation = true;
			while (!sceneLoading.isDone)
				await Awaitable.NextFrameAsync(ct); // Attendre l'activation réelle de la scène.

			await effect.PlayOutAsync(ct);
		}
	}
}