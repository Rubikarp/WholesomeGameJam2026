using Alchemy.Inspector;
using UnityEngine.Events;
using UnityEngine;
using TMPro;

namespace WorldGame.UI
{
	public class BasicToggle : ReactiveSelectable
	{
		[Header("Info")]
		[field: SerializeField] public bool IsOn { get; protected set; }
		[field: SerializeField] public string Text { get; protected set; }

		[FoldoutGroup("Events")] 
		public UnityEvent<bool> onValueChanged = new();
		public UnityAction<BasicToggle, bool> onToggleChange;

		[Header("References")] 
		[SerializeField] protected TextMeshProUGUI textSlot;
		[SerializeField] protected GameObject[] activeWhenOn;
		[SerializeField] protected GameObject[] activeWhenOff;

		[Header("On")] 
		[SerializeField] protected ColorReactor[] colorOnReactions;
		[Header("Off")] 
		[SerializeField] protected ColorReactor[] colorOffReactions;

		[Button]
		public void Toggle() => SetState(!IsOn);

		public void SetState(bool value)
		{
			if (IsOn == value) return;

			IsOn = value;
			onToggleChange?.Invoke(this, value);

			//Check if not cancelled by toggle group
			if (IsOn != value) return;
			onValueChanged?.Invoke(IsOn);
		}

		public void SetStateWithoutNotify(bool isOn)
		{
			if (IsOn == isOn) return;

			IsOn = isOn;
			DoStateTransition(currentSelectionState, false);
		}

		protected override void ApplyState(EReactState state, bool isInstant)
		{
			textSlot.text = Text;
			foreach (var activable in activeWhenOn)
				activable.SetActive(IsOn);
			foreach (var activable in activeWhenOff)
				activable.SetActive(!IsOn);

			if (IsOn)
			{
				foreach (var colorReactor in colorOnReactions)
					colorReactor.React(state, isInstant);
			}
			else
			{
				foreach (var colorReactor in colorOffReactions)
					colorReactor.React(state, isInstant);
			}
		}
	}
}