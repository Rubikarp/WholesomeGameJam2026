using System.Collections.Generic;
using Alchemy.Inspector;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class LifePointBar : MonoBehaviour
{
    [Header("Variables")]
    [field: SerializeField] public int MaxLifePoints = 5;
    [field: SerializeField, Min(0)] public int CurrentLifePoint = 0;
    [SerializeField, ReadOnly] private int _progress;

    [Header("References")]
    [Required] public LifePoint lifePointPrefab;
    [Required] public RectTransform lifePointContainer;
    [SerializeField, NonReorderable] 
    private List<LifePoint> lifePoints = new List<LifePoint>();
    [Space]
    [Required] public Image emptyLifePointPrefab;
    [Required] public RectTransform emptyLifePointContainer;

    [Header("Events")]
    public UnityEvent onDie;

    private void OnValidate()
    {
        CurrentLifePoint = Mathf.Clamp(CurrentLifePoint, 0, MaxLifePoints);
        _progress = CurrentLifePoint;

        RefreshLifePoints();
    }
    private void Start()
    {
        InitializeLifePointsVisuals();
        FullLife();
    }

    public void FullLife() => SetLifePoints(MaxLifePoints);
    public void SetLifePoints(int amount)
    {
        CurrentLifePoint = Mathf.Clamp(amount, 0, MaxLifePoints);
        RefreshLifePoints();
    }
    public void GainLifePoints(int amount = 1)
    {
        CurrentLifePoint = Mathf.Min(CurrentLifePoint + 1, MaxLifePoints);
        RefreshLifePoints();
    }
    public void LoseLifePoints(int amount = 1)
    {
        if(CurrentLifePoint - amount <= 0)
        {
            CurrentLifePoint = 0;
            onDie?.Invoke();
            return;
        }
        CurrentLifePoint = CurrentLifePoint - amount;
        RefreshLifePoints();
    }
    public void ChangeMaxLife(int newMaxLife)
    {
        MaxLifePoints = newMaxLife;
        InitializeLifePointsVisuals();
        FullLife();
        RefreshLifePoints();
    }

    [Button]
    private void InitializeLifePointsVisuals()
    {
        lifePointContainer.DeleteChildrens();
        lifePointContainer.name = "LifePointContainer";

        // Recreate empty life point container
#if UNITY_EDITOR
        DestroyImmediate(emptyLifePointContainer.gameObject);
#else
        Destroy(emptyLifePointContainer.gameObject);
#endif
        emptyLifePointContainer = Instantiate(lifePointContainer, lifePointContainer.parent);
        emptyLifePointContainer.SetSiblingIndex(lifePointContainer.GetSiblingIndex());
        emptyLifePointContainer.name = "EmptyLifePointContainer";

        // Fill life points
        lifePoints.Clear();
        for (int i = 0; i < MaxLifePoints; i++)
        {
#if UNITY_EDITOR
            LifePoint lifePoint = (LifePoint)PrefabUtility.InstantiatePrefab(lifePointPrefab, lifePointContainer);
            Image emptyLifePoint = (Image)PrefabUtility.InstantiatePrefab(emptyLifePointPrefab, emptyLifePointContainer);
#else
            LifePoint lifePoint = Instantiate(lifePointPrefab, lifePointContainer);
            Image emptyLifePoint = Instantiate(emptyLifePointPrefab, emptyLifePointContainer);
#endif
            lifePoint.name = $"LifePoint_{i}";
            emptyLifePoint.name = $"EmptyLifePoint_{i}";

            lifePoint.imageBackground = emptyLifePoint;
            lifePoints.Add(lifePoint);
        }
    }
    private void RefreshLifePoints()
    {
        for (int i = 0; i < MaxLifePoints; i++)
        {
            lifePoints[i].SetState(i < CurrentLifePoint);
        }
    }
}
