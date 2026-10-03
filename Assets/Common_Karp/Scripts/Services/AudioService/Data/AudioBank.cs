using System.Collections.Generic;
using System;
using Alchemy.Serialization;
using UnityEngine;

[AlchemySerialize]
[CreateAssetMenu(fileName = "AB_NewAudioBank", menuName = "AudioService/Audio Bank", order = 1)]
public partial class AudioBank : ScriptableObject
{
    [AlchemySerializeField, NonSerialized]
    public Dictionary<EAudioEventType, AudioClipData> AudioClipsData = new();
}
