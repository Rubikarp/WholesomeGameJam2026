using UnityEngine;
using UnityEngine.UI;

public class MuteButton : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private ToggleImage muteIcon;
    [SerializeField] private Button muteButton;

    [Header("Info")]
    private AudioSettingsService audioSettingsService;

    private void Start()
    {
        audioSettingsService = AudioSettingsService.Instance;
        muteButton.onClick.AddListener(ToggleMute);

        RefreshMuteState();
    }

    public void ToggleMute()
    {
        audioSettingsService.ToggleMuteGame();
        RefreshMuteState();
    }

    private void RefreshMuteState()
    {
        muteIcon.SetState(audioSettingsService.IsGameMuted);
    }
}
