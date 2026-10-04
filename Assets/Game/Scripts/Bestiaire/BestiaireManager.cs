using System.Collections.Generic;
using UnityEngine;

public class Bestiaire : MonoBehaviour {
    public HorseTracker MesChevaux;
        
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start() {
        foreach (HorseData horse in MesChevaux.Horses)
    {
        Debug.Log("Cheval : " + horse.horseName + " " + horse.rarity + " " + horse.horseDescription + " " + horse.Visual);
    }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
