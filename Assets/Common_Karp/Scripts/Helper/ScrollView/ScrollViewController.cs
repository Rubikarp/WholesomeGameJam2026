using PrimeTween;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Alchemy.Inspector;

[RequireComponent(typeof(ScrollView))]
public class ScrollViewController : UIBehaviour
{
    [Header("References")] 
    [SerializeField] private ScrollView scrollView;

    [Header("Position")] 
    [SerializeField] public bool UseStep;

    [NaughtyAttributes.ShowNativeProperty, ReadOnly]
    public Vector2 CurrentPos
    {
        get => new Vector2(_posX, _posY);
        private set
        {
            _posX = Mathf.Clamp01(value.x);
            _posY = Mathf.Clamp01(value.y);
        }
    }

    [HideIf("UseStep"), SerializeField, Range(0f, 1f)]
    private float _posX;

    [HideIf("UseStep"), SerializeField, Range(0f, 1f)]
    private float _posY;

    private int _rangeStepXCount => StepXCount - 1;

    [ShowIf("UseStep"), SerializeField]
    private int _stepX;

    [ShowIf("UseStep"), SerializeField, Min(1)]
    public int StepXCount = 2;

    private int _rangeStepYCount => StepYCount - 1;

    [ShowIf("UseStep"), SerializeField]
    private int _stepY;

    [ShowIf("UseStep"), SerializeField, Min(1)]
    public int StepYCount = 2;

    [Header("Events")] public UnityEvent<Vector2> OnPositionChanged;

    protected override void Awake()
    {
        base.Awake();
        if (scrollView == null) scrollView = GetComponent<ScrollView>();
    }

    protected void OnValidate()
    {
        if (scrollView == null) scrollView = GetComponent<ScrollView>();
        if (UseStep)
            ApplyStep();
        else
            ApplyMove(CurrentPos);
    }

    // Step API
    public void MoveHorizontalToNextStep() => MoveToStep(_stepX + 1, _stepY);
    public void MoveHorizontalToPreviousStep() => MoveToStep(_stepX - 1, _stepY);
    public void MoveVerticalToNextStep() => MoveToStep(_stepX, _stepY + 1);
    public void MoveVerticalToPreviousStep() => MoveToStep(_stepX, _stepY - 1);

    public void MoveToStep(int x, int y)
    {
        _stepX = Mathf.Clamp(x, 0, Mathf.Max(0, StepXCount - 1));
        _stepY = Mathf.Clamp(y, 0, Mathf.Max(0, StepYCount - 1));
        ApplyStep();
    }

    private void ApplyStep()
    {
        float x = StepXCount > 1 ? (float)_stepX / (StepXCount - 1) : 0f;
        float y = StepYCount > 1 ? (float)_stepY / (StepYCount - 1) : 0f;
        MoveTo(new Vector2(x, y));
    }

    // Move API
    public void MoveHorizontallyTo(float x) => MoveTo(new Vector2(x, _posY));
    public void MoveVerticallyTo(float y) => MoveTo(new Vector2(_posX, y));

    public void MoveTo(Vector2 normalizedPos)
    {
        CurrentPos = normalizedPos;
        ApplyMove(CurrentPos);
    }

    private void ApplyMove(Vector2 target)
    {
        scrollView.NormalizedPosition = target;
        OnPositionChanged?.Invoke(CurrentPos);
    }
}