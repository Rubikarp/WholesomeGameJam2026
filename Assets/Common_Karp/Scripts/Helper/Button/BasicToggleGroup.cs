using System.Collections.Generic;
using Alchemy.Inspector;
using UnityEngine;
using UnityEngine.Events;

namespace WorldGame.UI
{
	public class BasicToggleGroup : MonoBehaviour
	{
		[field: SerializeField] public bool AllowSwitchOff { get; set; }
		[field: SerializeField, Min(1)] public int MaxActiveToggles { get; set; } = 1;
		[field: SerializeField, ReadOnly] public List<BasicToggle> Toggles { get; private set; } = new();

		/// <summary>
		/// Index in the list is the pressed order, list because queue not serialized by unity
		/// </summary>
		[field: SerializeField, ReadOnly] public List<BasicToggle> ActiveToogle { get; private set; } = new();
		public UnityEvent onValueChanged = new();

		public void RegisterToggle(BasicToggle toggle)
		{
			if (Toggles.Contains(toggle)) return;

			Toggles.Add(toggle);
			toggle.onToggleChange += OnToogleChange;

			toggle.SetStateWithoutNotify(false);
		}
		public void UnregisterToggle(BasicToggle toggle)
		{
			if (!Toggles.Contains(toggle)) return;

			Toggles.Remove(toggle);
			toggle.onToggleChange -= OnToogleChange;

			if (ActiveToogle.Contains(toggle))
			{
				ActiveToogle.Remove(toggle);
			}
		}

		public void ClearSelection()
		{
			for (int i = Toggles.Count - 1; i >= 0; i--)
			{
				UnregisterToggle(Toggles[i]);
			}

			onValueChanged?.Invoke();
		}

		public void Toggle(BasicToggle chip)
		{
			if (!Toggles.Contains(chip))
			{
				Debug.LogError($"ToggleGroupHandler : Toogle {chip.name} not registered.", this);
				return;
			}

			if (ActiveToogle.Contains(chip))
			{
				SetToggleOff(chip);
			}
			else
			{
				SetToggleOn(chip);
			}
		}
		
		private void OnToogleChange(BasicToggle chip, bool isOn)
		{
			if (isOn)
				SetToggleOn(chip);
			else
				SetToggleOff(chip);
		}
		private void SetToggleOn(BasicToggle toggle)
		{
			if (ActiveToogle.Contains(toggle)) return;

			ActiveToogle.Add(toggle);
			toggle.SetStateWithoutNotify(true);
			onValueChanged?.Invoke();

			if (ActiveToogle.Count > MaxActiveToggles)
			{
				var oldest = ActiveToogle[0];
				SetToggleOff(oldest);
			}
		}
		private void SetToggleOff(BasicToggle toggle)
		{
			if (!ActiveToogle.Contains(toggle)) return;
			if (!AllowSwitchOff && ActiveToogle.Count == 1) return;

			ActiveToogle.Remove(toggle);
			toggle.SetStateWithoutNotify(false);
			onValueChanged?.Invoke();
		}

	}
}