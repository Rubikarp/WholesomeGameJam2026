// Basé sur UILineRenderer de Unity UI Extensions (jack.sydorenko, firagon)
// http://forum.unity3d.com/threads/new-ui-and-line-drawing.253772/
// Réécrit aux standards du projet ConquiersTaVie

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("UI/Extensions/Primitives/UILineRenderer")]
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
public class UILineRenderer : MaskableGraphic
{
    // ─── Types internes ────────────────────────────────────────────────────

    private enum ESegmentType
    {
        Start,
        Middle,
        End,
        Full,
    }

    public enum EJoinType
    {
        Bevel,
        Miter,
    }


    // ─── Constantes ────────────────────────────────────────────────────────

    private const float MIN_MITER_JOIN_RAD = 15f * Mathf.Deg2Rad;
    private const float MIN_BEVEL_NICE_JOIN_RAD = 30f * Mathf.Deg2Rad;
    private const int MAX_VERTEX_COUNT = 64000;


    // ─── UV statiques (partagés, immuables) ───────────────────────────────

    private static readonly Vector2 k_UvTopLeft = new(0f, 0f);
    private static readonly Vector2 k_UvBottomLeft = new(0f, 1f);
    private static readonly Vector2 k_UvTopCenterLeft = new(0.5f, 0f);
    private static readonly Vector2 k_UvTopCenterRight = new(0.5f, 0f);
    private static readonly Vector2 k_UvBotCenterLeft = new(0.5f, 1f);
    private static readonly Vector2 k_UvBotCenterRight = new(0.5f, 1f);
    private static readonly Vector2 k_UvTopRight = new(1f, 0f);
    private static readonly Vector2 k_UvBottomRight = new(1f, 1f);

    private static readonly Vector2[] k_StartUvs =
        { k_UvTopLeft, k_UvBottomLeft, k_UvBotCenterLeft, k_UvTopCenterLeft };

    private static readonly Vector2[] k_MiddleUvs =
        { k_UvTopCenterLeft, k_UvBotCenterLeft, k_UvBotCenterRight, k_UvTopCenterRight };

    private static readonly Vector2[] k_EndUvs =
        { k_UvTopCenterRight, k_UvBotCenterRight, k_UvBottomRight, k_UvTopRight };

    private static readonly Vector2[] k_FullUvs = { k_UvTopLeft, k_UvBottomLeft, k_UvBottomRight, k_UvTopRight };


    // ─── Champs sérialisés ─────────────────────────────────────────────────

    [Header("Points")]
    [Tooltip("Coordonnées en pixels locaux si useRelativeSize est décoché, en 0→1 sinon.")]
    [SerializeField]
    private Vector2[] _points;

    [Header("Apparence")] [SerializeField, Min(0f)]
    private float _lineThickness = 2f;

    [SerializeField] private EJoinType _lineJoin = EJoinType.Bevel;
    [SerializeField] private bool _useLineCaps;

    [Header("Mode")]
    [Tooltip("Si coché, les points sont exprimés en coordonnées normalisées (0→1) relatives au RectTransform.")]
    [SerializeField]
    private bool _useRelativeSize;

    [Tooltip("Chaque paire de points forme un segment indépendant plutôt qu'une polyligne continue.")] [SerializeField]
    private bool _useLineList;


    // ─── Propriétés publiques ──────────────────────────────────────────────

    /// <summary>
    /// Points de la ligne. En coordonnées locales (pixels) si UseRelativeSize est false,
    /// en coordonnées normalisées (0→1) sinon. Minimum 2 points.
    /// </summary>
    public Vector2[] Points
    {
        get => _points;
        set
        {
            if (_points == value)
            {
                return;
            }

            _points = value;
            SetVerticesDirty();
        }
    }

    public float LineThickness
    {
        get => _lineThickness;
        set
        {
            _lineThickness = value;
            SetVerticesDirty();
        }
    }

    public EJoinType LineJoin
    {
        get => _lineJoin;
        set
        {
            _lineJoin = value;
            SetVerticesDirty();
        }
    }

    public bool UseLineCaps
    {
        get => _useLineCaps;
        set
        {
            _useLineCaps = value;
            SetVerticesDirty();
        }
    }

    public bool UseRelativeSize
    {
        get => _useRelativeSize;
        set
        {
            _useRelativeSize = value;
            SetVerticesDirty();
        }
    }

    public bool UseLineList
    {
        get => _useLineList;
        set
        {
            _useLineList = value;
            SetVerticesDirty();
        }
    }


    // ─── Génération du mesh ────────────────────────────────────────────────

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (_points == null || _points.Length < 2)
        {
            return;
        }

        float sizeX = _useRelativeSize ? rectTransform.rect.width : 1f;
        float sizeY = _useRelativeSize ? rectTransform.rect.height : 1f;
        float offsetX = -rectTransform.pivot.x * sizeX;
        float offsetY = -rectTransform.pivot.y * sizeY;

        var segments = _useLineList
            ? BuildLineListSegments(_points, sizeX, sizeY, offsetX, offsetY)
            : BuildPolylineSegments(_points, sizeX, sizeY, offsetX, offsetY);

        CommitSegments(vh, segments);

        if (vh.currentVertCount > MAX_VERTEX_COUNT)
        {
            Debug.LogError(
                $"[UILineRenderer] Dépassement du budget vertex ({vh.currentVertCount}/{MAX_VERTEX_COUNT}). " +
                "Réduisez le nombre de points.");
            vh.Clear();
        }
    }

    // Chaque paire (i-1, i) est un segment indépendant avec caps optionnels.
    private List<UIVertex[]> BuildLineListSegments(
        Vector2[] points,
        float sizeX, float sizeY, float offsetX, float offsetY)
    {
        var segments = new List<UIVertex[]>();

        for (int i = 1; i < points.Length; i += 2)
        {
            var start = ScalePoint(points[i - 1], sizeX, sizeY, offsetX, offsetY);
            var end = ScalePoint(points[i], sizeX, sizeY, offsetX, offsetY);

            if (_useLineCaps)
            {
                segments.Add(CreateLineCap(start, end, ESegmentType.Start));
            }

            segments.Add(CreateLineSegment(start, end, ESegmentType.Middle));

            if (_useLineCaps)
            {
                segments.Add(CreateLineCap(start, end, ESegmentType.End));
            }
        }

        return segments;
    }

    // Polyligne continue : les segments se joignent.
    private List<UIVertex[]> BuildPolylineSegments(
        Vector2[] points,
        float sizeX, float sizeY, float offsetX, float offsetY)
    {
        var segments = new List<UIVertex[]>();

        for (int i = 1; i < points.Length; i++)
        {
            var start = ScalePoint(points[i - 1], sizeX, sizeY, offsetX, offsetY);
            var end = ScalePoint(points[i], sizeX, sizeY, offsetX, offsetY);

            if (_useLineCaps && i == 1)
            {
                segments.Add(CreateLineCap(start, end, ESegmentType.Start));
            }

            segments.Add(CreateLineSegment(start, end, ESegmentType.Middle));

            if (_useLineCaps && i == points.Length - 1)
            {
                segments.Add(CreateLineCap(start, end, ESegmentType.End));
            }
        }

        return segments;
    }

    private void CommitSegments(VertexHelper vh, List<UIVertex[]> segments)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            // En mode polyligne, on calcule la jointure entre deux segments de ligne consécutifs.
            // Les caps en tête/queue ne participent pas aux jointures : on les saute.
            bool isJoinCandidate = !_useLineList && i < segments.Count - 1;

            if (isJoinCandidate)
            {
                bool currentIsCap = _useLineCaps && i == 0;
                bool nextIsCap = _useLineCaps && i == segments.Count - 2;

                if (!currentIsCap && !nextIsCap)
                {
                    EmitJoin(vh, segments, i);
                }
            }

            vh.AddUIVertexQuad(segments[i]);
        }
    }

    /// <summary>
    /// Calcule et applique la jointure entre segments[i] et segments[i+1].
    /// En mode Miter : vertices modifiés in-place, pas de quad supplémentaire.
    /// En mode Bevel : un quad de raccord est émis dans vh AVANT le segment courant.
    /// </summary>
    private void EmitJoin(VertexHelper vh, List<UIVertex[]> segments, int i)
    {
        var vec1 = segments[i][1].position - segments[i][2].position;
        var vec2 = segments[i + 1][2].position - segments[i + 1][1].position;
        float angle = Vector2.Angle(vec1, vec2) * Mathf.Deg2Rad;
        float sign = Mathf.Sign(Vector3.Cross(vec1.normalized, vec2.normalized).z);

        float miterDistance = _lineThickness / (2f * Mathf.Tan(angle / 2f));

        bool miterFits =
            miterDistance < vec1.magnitude / 2f &&
            miterDistance < vec2.magnitude / 2f;

        // UIVertex est une struct : il faut copier, modifier, puis réassigner.
        EJoinType joinType = _lineJoin;

        if (joinType == EJoinType.Miter)
        {
            if (miterFits && angle > MIN_MITER_JOIN_RAD)
            {
                var miterPointA = segments[i][2].position - (Vector3)(vec1.normalized * miterDistance * sign);
                var miterPointB = segments[i][3].position + (Vector3)(vec1.normalized * miterDistance * sign);

                SetVertexPosition(segments[i], 2, miterPointA);
                SetVertexPosition(segments[i], 3, miterPointB);
                SetVertexPosition(segments[i + 1], 0, miterPointB);
                SetVertexPosition(segments[i + 1], 1, miterPointA);
                return;
            }

            joinType = EJoinType.Bevel; // dégradation gracieuse si l'angle est trop aigu
        }

        if (joinType == EJoinType.Bevel && miterFits && angle > MIN_BEVEL_NICE_JOIN_RAD)
        {
            var miterPointA = segments[i][2].position - (Vector3)(vec1.normalized * miterDistance * sign);
            var miterPointB = segments[i][3].position + (Vector3)(vec1.normalized * miterDistance * sign);

            if (sign < 0)
            {
                SetVertexPosition(segments[i], 2, miterPointA);
                SetVertexPosition(segments[i + 1], 1, miterPointA);
            }
            else
            {
                SetVertexPosition(segments[i], 3, miterPointB);
                SetVertexPosition(segments[i + 1], 0, miterPointB);
            }
        }

        // Quad de raccord pour remplir le triangle visible entre les deux segments
        var bevelQuad = new UIVertex[]
        {
            segments[i][2],
            segments[i][3],
            segments[i + 1][0],
            segments[i + 1][1],
        };

        vh.AddUIVertexQuad(bevelQuad);
    }

    // UIVertex est une struct stockée dans un tableau — la modification doit passer par
    // une copie locale puis réassignation pour que le changement soit persisté.
    private static void SetVertexPosition(UIVertex[] quad, int index, Vector3 position)
    {
        var vertex = quad[index];
        vertex.position = position;
        quad[index] = vertex;
    }


    // ─── Helpers géométriques ──────────────────────────────────────────────

    private static Vector2 ScalePoint(Vector2 point, float sx, float sy, float ox, float oy)
    {
        return new Vector2(point.x * sx + ox, point.y * sy + oy);
    }

    private UIVertex[] CreateLineCap(Vector2 start, Vector2 end, ESegmentType type)
    {
        var direction = (end - start).normalized;
        float halfThickness = _lineThickness / 2f;

        if (type == ESegmentType.Start)
        {
            return CreateLineSegment(start - direction * halfThickness, start, ESegmentType.Start);
        }

        if (type == ESegmentType.End)
        {
            return CreateLineSegment(end, end + direction * halfThickness, ESegmentType.End);
        }

        Debug.LogError("[UILineRenderer] CreateLineCap : type invalide. Utiliser Start ou End uniquement.");
        return null;
    }

    private UIVertex[] CreateLineSegment(Vector2 start, Vector2 end, ESegmentType type)
    {
        // Normale perpendiculaire au segment, projetée à demi-épaisseur de chaque côté
        var offset = new Vector2(start.y - end.y, end.x - start.x).normalized * (_lineThickness / 2f);

        var positions = new Vector2[]
        {
            start - offset, // v0
            start + offset, // v1
            end + offset, // v2
            end - offset, // v3
        };

        var uvs = type switch
        {
            ESegmentType.Start => k_StartUvs,
            ESegmentType.End => k_EndUvs,
            ESegmentType.Full => k_FullUvs,
            _ => k_MiddleUvs,
        };

        return BuildQuadVertices(positions, uvs);
    }

    private UIVertex[] BuildQuadVertices(Vector2[] positions, Vector2[] uvs)
    {
        var vertices = new UIVertex[4];
        var color32 = (Color32)color;

        for (int i = 0; i < 4; i++)
        {
            vertices[i] = new UIVertex
            {
                color = color32,
                position = positions[i],
                uv0 = uvs[i],
            };
        }

        return vertices;
    }


    // ─── Lifecycle ────────────────────────────────────────────────────────

    // OnPopulateMesh peut être appelé avant que le Canvas ait effectué son premier
    // layout pass — rectTransform.rect vaut alors (0,0) et les pixels ne se mappent
    // pas correctement. On force un rebuild au Start, après que tout soit initialisé.
    protected override void Start()
    {
        base.Start();
        SetVerticesDirty();
    }

    // Appelé par UGUI quand la taille du RectTransform change (resize, ancres, etc.)
    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        SetVerticesDirty();
    }


    // ─── Editor ───────────────────────────────────────────────────────────

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}