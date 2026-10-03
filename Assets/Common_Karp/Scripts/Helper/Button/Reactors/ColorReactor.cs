using System;
using Alchemy.Inspector;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGame.UI
{
	[Serializable]
	public class ColorReactor : StateReactorBase
	{
		[SerializeField] private string id = "BackGroundColor";
		
		[SerializeField, Required] private Graphic[] _targets;
		[SerializeField] private SStateMap<Color> _colors;

		[SerializeField, Range(0f, 1f)] private float _fadeDuration;
		[SerializeField] private Ease _ease;

		public override void React(EReactState state, bool isInstant)
		{
			Color tint = _colors.Resolve(state);

			foreach (Graphic graphic in _targets)
			{
				if (graphic == null)
				{
					continue;
				}

				Tween.StopAll(graphic);
				if (isInstant || _fadeDuration <= 0f)
				{
					graphic.color = tint;
					continue;
				}

				Tween.Color(graphic, tint, _fadeDuration, _ease).GetAwaiter();
			}
		}
		
		public ColorReactor()
		{
			_targets = new Graphic[1];
			_fadeDuration = 0.15f;
			_ease = Ease.Linear;
			
			_colors = new SStateMap<Color>
			{
				Normal = Color.gray2,
				Focused = Color.gray4,
				Pressed = Color.gray8,
				Disabled = Color.gray7,
			};
		}
	}
}