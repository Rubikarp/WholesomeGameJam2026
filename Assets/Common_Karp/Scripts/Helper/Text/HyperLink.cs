using Alchemy.Inspector;
using UnityEngine.EventSystems;
using UnityEngine;

public class HyperLink : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
{
    [field: Header("URL")] 
    [field: SerializeField] public string Url { get; set; } = string.Empty;

    [Button]
    public void OpenLink()
    {
        if (string.IsNullOrEmpty(Url)) return;
        Application.OpenURL(Url);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        //OpenLink();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        OpenLink();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
    }
}