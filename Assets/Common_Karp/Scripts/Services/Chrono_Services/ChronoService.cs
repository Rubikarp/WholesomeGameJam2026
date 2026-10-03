using System;
using System.Collections.Generic;
using Alchemy.Serialization;

[AlchemySerialize]
public partial class ChronoService : Singleton<ChronoService>, IChronoService
{
    [AlchemySerializeField, NonSerialized]
    public Dictionary<EChronoType, Chrono> Timers = new Dictionary<EChronoType,Chrono>();
    
    public IChrono GetChrono(EChronoType timerType) => Timers[timerType];
}