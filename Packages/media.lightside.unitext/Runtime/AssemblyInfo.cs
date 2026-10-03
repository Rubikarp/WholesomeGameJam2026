using System.Runtime.CompilerServices;
using LightSide;
#if UNITY_WEBGL && !UNITY_EDITOR && !UNITY_6000_4_OR_NEWER
using Unity.Burst;
#endif

[assembly: GenerateStateAccessors(Marker = "LightSide.ParameterAttribute")]
[assembly: InternalsVisibleTo("LightSide.UniText.Editor")]
[assembly: InternalsVisibleTo("LightSide.UniText.Editor.Localization")]
[assembly: InternalsVisibleTo("LightSide.UniText.Editor.SBP")]
[assembly: InternalsVisibleTo("UniText.Test")]
[assembly: InternalsVisibleTo("UniText.Tests.EditMode")]
[assembly: InternalsVisibleTo("LightSide.UniText.Inspection")]
[assembly: InternalsVisibleTo("UniText.Tests.PlayMode")]
#if UNITY_WEBGL && !UNITY_EDITOR && !UNITY_6000_4_OR_NEWER
[assembly: BurstCompile(DisableDirectCall = true)]
#endif
