using UnityEngine;
using System.Collections.Generic;

public class HorseTracker : MonoBehaviour
{
   public List<HorseData> Horses = new List<HorseData>();
   public HorseData HorseToAdd;

   public void RegisterHorses(HorseData HorseToAdd) {

}

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Horses.Add(HorseToAdd);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
