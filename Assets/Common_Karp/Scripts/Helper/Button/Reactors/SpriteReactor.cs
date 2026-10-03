using System;
using Alchemy.Inspector;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGame.UI
{
	[Serializable]
	public class SpriteReactor : StateReactorBase
	{
		[SerializeField, Required] private Image[] _targets = new Image[1];
		[SerializeField] private SStateMap<Sprite> _sprites;

		public override void React(EReactState state, bool isInstant)
		{
			Sprite visual = _sprites.Resolve(state);

			foreach (Image image in _targets)
			{
				if (image == null)
				{
					continue;
				}

				image.sprite = visual;
			}
		}
	}
}