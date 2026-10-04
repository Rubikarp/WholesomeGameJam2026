using System;
using System.Linq;
using Alchemy.Inspector;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

public class HorseTracker : MonoBehaviour
{ 
   public HorseData HorseToTrack;
   
   public Image profilePicture;
   public TextMeshProUGUI description;
   public TextMeshProUGUI counter;

   [Button]
   private void OnEnable()
   {
      profilePicture.sprite = HorseToTrack.VisualBestiary;

      int captured = GameManager.Instance.AllLootedHorses.Count(h => h == HorseToTrack);
      bool isUnlocked = captured > 0;
      
      counter.text = captured.ToString();
      if (isUnlocked)
      {
         profilePicture.color = Color.white;
         description.text = HorseToTrack.horseDescription;
      }
      else
      {
         profilePicture.color = Color.black;
         description.text = "Apprivoise le cheval pour en savoir plus";
      }
   }
}
