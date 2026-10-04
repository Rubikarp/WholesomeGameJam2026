using System.Collections.Generic;
using UnityEngine;

public class GameManager : PersistentSingleton<GameManager>
{
	public Sprite CurrentZone;
	public HorseData CurrentHorse;
	public List<HorseData> AllLootedHorses = new List<HorseData>(32);
}