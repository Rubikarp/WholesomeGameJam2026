using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine;

[RequireComponent(typeof(Button))]
public class Zone : MonoBehaviour
{
    private Button button;
    public int capturesToUnlockNext = 3;
    public Sprite zoneBackground = null;
    public List<HorseData> horsePool = new List<HorseData>();

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnZoneClicked);
        
        button.interactable = GameManager.Instance.AllLootedHorses.Count >= capturesToUnlockNext;
    }

    public void OnZoneClicked()
    {
        float totalWeight = horsePool.Sum(horseData => horseData.rarity.Weight());

        float roll = Random.Range(0f, totalWeight);
        foreach (HorseData horse in horsePool)
        {
            roll -= horse.rarity.Weight();
            
            if (roll <= 0f)
            {
                GameManager.Instance.CurrentHorse = horse;
                GameManager.Instance.CurrentZone = zoneBackground;
                Debug.Log($"Encounter horse : {horse.horseName}");
                SceneManager.LoadScene("Capture");
                return;
            }
        }
    }
}