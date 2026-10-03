using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
#endif

namespace JeffGrawAssets.FlexibleUI
{ 
public class InputModuleReplacer : MonoBehaviour
{
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    void Awake()
    {
        var standaloneInputModule = GetComponent<StandaloneInputModule>();
        if (standaloneInputModule != null)
        {
            Destroy(standaloneInputModule);
            gameObject.AddComponent<InputSystemUIInputModule>();
        }
        Destroy(this);
    }
#endif
}
}
