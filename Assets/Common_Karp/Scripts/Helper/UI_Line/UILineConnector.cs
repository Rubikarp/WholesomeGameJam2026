using UnityEngine;

/// <summary>
/// Relie une liste de RectTransforms en mettant à jour automatiquement
/// les points d'un UILineRenderer avec leurs positions pivot.
///
/// Les targets peuvent appartenir à n'importe quel Canvas ou espace :
/// la conversion passe par l'espace monde via InverseTransformPoint,
/// sans dépendance à la caméra ni au mode de rendu du Canvas.
/// </summary>
[AddComponentMenu("UI/Extensions/UILineConnector")]
[RequireComponent(typeof(UILineRenderer))]
public class UILineConnector : MonoBehaviour
{
    // ─── Champs sérialisés ─────────────────────────────────────────────────

    [SerializeField] private RectTransform[] _targets;


    // ─── État interne ──────────────────────────────────────────────────────

    private UILineRenderer _lineRenderer;

    // Cache des positions monde du dernier frame — évite SetVerticesDirty si rien n'a bougé.
    private Vector3[] _cachedWorldPositions;


    // ─── Propriété publique ────────────────────────────────────────────────

    public RectTransform[] Targets
    {
        get => _targets;
        set
        {
            _targets = value;
            _cachedWorldPositions = null; // invalide le cache pour forcer un rebuild
        }
    }


    // ─── Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _lineRenderer = GetComponent<UILineRenderer>();
    }

    private void LateUpdate()
    {
        if (!HasValidTargets())
        {
            return;
        }

        if (HaveTargetsMoved())
        {
            RefreshPoints();
        }
    }


    // ─── Logique ──────────────────────────────────────────────────────────

    private bool HasValidTargets()
    {
        return _targets != null && _targets.Length >= 2;
    }

    /// <summary>
    /// Compare les positions monde actuelles avec le cache.
    /// Retourne true dès qu'un target a bougé ou que le cache est invalide.
    /// </summary>
    private bool HaveTargetsMoved()
    {
        if (_cachedWorldPositions == null || _cachedWorldPositions.Length != _targets.Length)
        {
            return true;
        }

        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] == null)
            {
                return true;
            }

            if (_targets[i].position != _cachedWorldPositions[i])
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshPoints()
    {
        var localPoints = new Vector2[_targets.Length];
        var lineTransform = _lineRenderer.rectTransform;

        if (_cachedWorldPositions == null || _cachedWorldPositions.Length != _targets.Length)
        {
            _cachedWorldPositions = new Vector3[_targets.Length];
        }

        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] == null)
            {
                continue;
            }

            var worldPosition = _targets[i].position;
            _cachedWorldPositions[i] = worldPosition;

            // Conversion monde → espace local du UILineRenderer, indépendante du Canvas
            var localPosition = lineTransform.InverseTransformPoint(worldPosition);
            localPoints[i] = localPosition;
        }

        _lineRenderer.Points = localPoints;
    }


    // ─── Editor ───────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_lineRenderer == null)
        {
            _lineRenderer = GetComponent<UILineRenderer>();
        }

        _cachedWorldPositions = null;

        if (HasValidTargets() && _lineRenderer != null)
        {
            RefreshPoints();
        }
    }
#endif
}