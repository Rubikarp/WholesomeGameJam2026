using System;

public enum RarityTier 
{
	Common, 
	Uncommon, 
	Rare, 
	Epic, 
	Legendary 
}

public static class RarityTierExtensions
{
	//Chance de drop
	public static float Weight(this RarityTier rarity)
	{
		float weight = rarity switch
		{
			RarityTier.Common => 5,
			RarityTier.Uncommon => 4,
			RarityTier.Rare => 3,
			RarityTier.Epic => 2,
			RarityTier.Legendary => 1f,
			_ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, $"Invalid RarityTier: {rarity}")
		};
		return weight;
	}

}