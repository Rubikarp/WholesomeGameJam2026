using UnityEngine;
using TMPro;

namespace WorldGame.UI
{
	public class BasicButton : ReactiveSelectable
	{
		[Header("Info")] 
		[SerializeField] protected string text;

		[Header("References")] 
		[SerializeField] protected TextMeshProUGUI textSlot;
		[SerializeField] protected ColorReactor[] colorReactors;

		public virtual void EditText(string newText) => text = newText;
		
		protected override void ApplyState(EReactState state, bool isInstant)
		{
			if (textSlot == null)
			{
				Debug.LogWarning($"No text slot assigned to {this}",this);
				return;
			}

			textSlot.text = text;
			foreach (var colorReactor in colorReactors)
				colorReactor.React(state, isInstant);
		}
	}
}
