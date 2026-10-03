using System;
using Alchemy.Inspector;
using TMPro;
using UnityEngine;

namespace WorldGame.UI
{
	[Serializable]
	public class TextContentReactor : StateReactorBase
	{
		[SerializeField, Required] private TextMeshProUGUI _target;
		[SerializeField] private SStateMap<string> _contents;

		public override void React(EReactState state, bool isInstant)
		{
			if (_target == null)
			{
				return;
			}

			_target.text = _contents.Resolve(state);
		}
	}
}