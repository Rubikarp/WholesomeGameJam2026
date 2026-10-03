using Alchemy.Inspector;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine;

namespace WG.Common
{
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class FocusArea : UIBehaviour
    {
        [Header("Borders")]
        [SerializeField] private RectTransform TopBorder;
        [SerializeField] private Image TopBorderImage;
        [SerializeField] private RectTransform BottomBorder;
        [SerializeField] private Image BottomBorderImage;
        [SerializeField] private RectTransform LeftBorder;
        [SerializeField] private Image LeftBorderImage;
        [SerializeField] private RectTransform RightBorder;
        [SerializeField] private Image RightBorderImage;
        [Space]
        [SerializeField] private Color BorderColor = Color.gray;

        [Header("Focus")]
        [SerializeField] private RectTransform FocusZone;
        [SerializeField] private Image FocusZoneImage;
        [SerializeField] private Sprite FocusSprite;

        void Update()
        {
            if (!Application.isPlaying)
            {
                UpdateBorders();
            }
        }
        [Button]
        private void SpawnArea()
        {
            transform.DeleteChildrens();
            if (FocusZone == null)
            {
                var go = GameObject.Instantiate(new GameObject("Focus", typeof(RectTransform), typeof(Image)), transform);
                go.name = "Focus";
                FocusZone = go.GetComponent<RectTransform>();
                FocusZoneImage = go.GetComponent<Image>();
            }
            if (TopBorder == null)
            {
                var go = GameObject.Instantiate(new GameObject("TopBorder", typeof(RectTransform), typeof(Image)), transform);
                go.name = "TopBorder";
                TopBorder = go.GetComponent<RectTransform>();
                TopBorderImage = go.GetComponent<Image>();
            }
            if (BottomBorder == null)
            {
                var go = GameObject.Instantiate(new GameObject("BottomBorder", typeof(RectTransform), typeof(Image)), transform);
                go.name = "BottomBorder";
                BottomBorder = go.GetComponent<RectTransform>();
                BottomBorderImage = go.GetComponent<Image>();
            }
            if (LeftBorder == null)
            {
                var go = GameObject.Instantiate(new GameObject("LeftBorder", typeof(RectTransform), typeof(Image)), transform);
                go.name = "LeftBorder";
                LeftBorder = go.GetComponent<RectTransform>();
                LeftBorderImage = go.GetComponent<Image>();
            }
            if (RightBorder == null)
            {
                var go = GameObject.Instantiate(new GameObject("RightBorder", typeof(RectTransform), typeof(Image)), transform);
                go.name = "RightBorder";
                RightBorder = go.GetComponent<RectTransform>();
                RightBorderImage = go.GetComponent<Image>();
            }
            UpdateBorders();
        }
        [Button]
        private void UpdateBorders()
        {
            if (TopBorder == null || 
                BottomBorder == null || 
                LeftBorder == null || 
                RightBorder == null || 
                FocusZone == null)
            {
                SpawnArea();
            }
            TopBorderImage.color = BorderColor;
            TopBorder.pivot = new Vector2(0.5f, 1);
            TopBorder.anchorMin = new Vector2(0, 1);
            TopBorder.anchorMax = new Vector2(1, 1);

            BottomBorderImage.color = BorderColor;
            BottomBorder.pivot = new Vector2(0.5f, 0);
            BottomBorder.anchorMin = new Vector2(0, 0);
            BottomBorder.anchorMax = new Vector2(1, 0);

            LeftBorderImage.color = BorderColor;
            LeftBorder.pivot = new Vector2(0, 0.5f);
            LeftBorder.anchorMin = new Vector2(0, 0);
            LeftBorder.anchorMax = new Vector2(0, 1);

            RightBorderImage.color = BorderColor;
            RightBorder.pivot = new Vector2(1, 0.5f);
            RightBorder.anchorMin = new Vector2(1, 0);
            RightBorder.anchorMax = new Vector2(1, 1);

            FocusZoneImage.sprite = FocusSprite;
            FocusZoneImage.color = BorderColor;
            FocusZone.pivot = new Vector2(0.5f, 0.5f);
            FocusZone.anchorMin = new Vector2(0, 0);
            FocusZone.anchorMax = new Vector2(1, 1);

            TopBorder.anchoredPosition = new Vector2(0, 0);
            BottomBorder.anchoredPosition = new Vector2(0, 0);
            LeftBorder.anchoredPosition = new Vector2(0, FocusZone.anchoredPosition.y);
            RightBorder.anchoredPosition = new Vector2(0, FocusZone.anchoredPosition.y);

            TopBorder.sizeDelta = new Vector2(0, -FocusZone.offsetMax.y);
            BottomBorder.sizeDelta = new Vector2(0, FocusZone.offsetMin.y);
            LeftBorder.sizeDelta = new Vector2(FocusZone.offsetMin.x, -(FocusZone.offsetMin.y - FocusZone.offsetMax.y));
            RightBorder.sizeDelta = new Vector2(-FocusZone.offsetMax.x, -(FocusZone.offsetMin.y - FocusZone.offsetMax.y));
        }
    }
}
