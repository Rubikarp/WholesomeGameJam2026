using Alchemy.Inspector;
using UnityEngine;
using System;

namespace WorldGame.UI
{
	[Serializable]
	public class ActiveReactor : StateReactorBase
	{
		[SerializeField, Required] private GameObject[] _targets = new GameObject[1];

		[SerializeField] private SStateMap<bool> _isActive = new SStateMap<bool>
		{
			Normal = true,
			Pressed = true,
			Focused = true,
			Disabled = false,
		};

		public override void React(EReactState state, bool isInstant)
		{
			bool isVisible = _isActive.Resolve(state);

			foreach (GameObject target in _targets)
			{
				if (target == null)
				{
					continue;
				}

				target.SetActive(isVisible);
			}
		}
	}
}