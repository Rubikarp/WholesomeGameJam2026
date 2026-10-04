using System.Collections.Generic;

public class GameManager : Singleton<GameManager>
{
	public HorseData HorsePicked;
	public List<HorseData> AllLootedHorses = new List<HorseData>(32);
}