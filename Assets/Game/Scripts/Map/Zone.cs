using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[System.Serializable]
public class HorsePool
{
    public HorseData horse;
    public float weight = 1f;
}
public class Zone : MonoBehaviour
{
    public static event System.Action<HorseData> HorsePicked;

    public List<HorsePool> horsePool = new List<HorsePool>();
    public int capturesToUnlockNext = 3;
    public string captureSceneName = "Capture";

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OnZoneClicked()
    {
        float totalWeight = 0f;
        foreach (HorsePool entry in horsePool)
            totalWeight += entry.weight;

        float roll = Random.Range(0f, totalWeight);
        foreach (HorsePool entry in horsePool)
        {
            roll -= entry.weight;
            if (roll <= 0f)
            {
                Debug.Log($"{entry.horse.horseName}");
                HorsePicked?.Invoke(entry.horse);
                SceneManager.LoadScene(captureSceneName);
                return;
            }
        }
    }
}
