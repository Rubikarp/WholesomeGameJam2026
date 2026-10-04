using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;

[RequireComponent(typeof(Button))]
public class Zone : MonoBehaviour
{
    private Button button;
    public List<HorseData> horsePool = new List<HorseData>();
    public int capturesToUnlockNext = 3;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnZoneClicked);
        
        button.interactable = GameManager.Instance.AllLootedHorses.Count > capturesToUnlockNext;
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
                GameManager.Instance.HorsePicked = horse;
                Debug.Log($"Encounter horse : {horse.horseName}");
                SceneManager.LoadScene("Capture");
                return;
            }
        }
    }
}