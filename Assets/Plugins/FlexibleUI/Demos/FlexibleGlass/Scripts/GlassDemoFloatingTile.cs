using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public enum GlassDemoFloatingShape { Block, Ball, Diamond }

[AddComponentMenu("")]
public sealed class GlassDemoFloatingTile : MaskableGraphic
{
    private readonly struct Face
    {
        public readonly Vector3 normal;
        public readonly Vector3[] vertices;
        public readonly bool smooth;
        public Face(Vector3 normal, params Vector3[] vertices)
            : this(normal, false, vertices) { }
        public Face(Vector3 normal, bool smooth, params Vector3[] vertices)
        {
            this.normal = normal.normalized;
            this.vertices = vertices;
            this.smooth = smooth;
        }
    }

    private static readonly Face[] BlockFaces = CreateFaces();
    private static readonly Face[] BallFaces = CreateBall();
    private static readonly Face[] DiamondFaces = CreateDiamond();
    private static readonly Vector3 LightDirection = new Vector3(-0.45f, 0.7f, -0.85f).normalized;
    public GlassDemoFloatingShape shape;
    [SerializeField, HideInInspector] private Quaternion orientation = Quaternion.identity;

    public float CollisionRadius => Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) *
        (shape == GlassDemoFloatingShape.Block ? 0.79f : shape == GlassDemoFloatingShape.Ball ? 0.5f : 0.6f);

    public void SetPose(Quaternion value)
    {
        if (orientation == value) return;
        orientation = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rotation = orientation;
        var rect = GetPixelAdjustedRect();
        var scale = Mathf.Min(rect.width, rect.height);
        var faces = shape == GlassDemoFloatingShape.Block ? BlockFaces : shape == GlassDemoFloatingShape.Ball ? BallFaces : DiamondFaces;
        foreach (var face in faces)
        {
            var normal = rotation * face.normal;
            if (normal.z >= 0f) continue;
            var shade = Shade(normal);
            var first = mesh.currentVertCount;
            foreach (var corner in face.vertices)
            {
                var position = rotation * corner;
                mesh.AddVert(new Vector3(rect.center.x + position.x * scale, rect.center.y + position.y * scale), face.smooth ? Shade(position * 2f) : shade, Vector2.zero);
            }
            for (var i = 1; i < face.vertices.Length - 1; i++)
                mesh.AddTriangle(first, first + i, first + i + 1);
        }
    }

    private Color Shade(Vector3 normal)
    {
        var light = Mathf.Clamp01(Vector3.Dot(normal, LightDirection));
        var shade = Color.Lerp(color * (0.45f + light * 0.53f), Color.white, light * light * 0.3f);
        shade.a = color.a;
        return shade;
    }

    private static Face[] CreateBall()
    {
        const int rings = 12, segments = 24;
        var faces = new Face[rings * segments];
        for (var ring = 0; ring < rings; ring++)
        for (var segment = 0; segment < segments; segment++)
        {
            var a = SpherePoint(ring, segment, rings, segments);
            var b = SpherePoint(ring + 1, segment, rings, segments);
            var c = SpherePoint(ring + 1, segment + 1, rings, segments);
            var d = SpherePoint(ring, segment + 1, rings, segments);
            faces[ring * segments + segment] = new Face(a + b + c + d, true, a, b, c, d);
        }
        return faces;
    }

    private static Vector3 SpherePoint(int ring, int segment, int rings, int segments)
    {
        var latitude = Mathf.PI * ((float)ring / rings - 0.5f);
        var longitude = Mathf.PI * 2f * segment / segments;
        return new Vector3(Mathf.Cos(latitude) * Mathf.Cos(longitude), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Sin(longitude)) * 0.5f;
    }

    private static Face[] CreateDiamond()
    {
        var faces = new List<Face>(8);
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
            faces.Add(new Face(new Vector3(x, y, z), Vector3.right * (0.6f * x), Vector3.up * (0.6f * y), Vector3.forward * (0.6f * z)));
        return faces.ToArray();
    }

    private static Face[] CreateFaces()
    {
        const float half = 0.5f;
        const float inset = 0.43f;
        var faces = new List<Face>(26);
        var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
        for (var axis = 0; axis < 3; axis++)
        for (var sign = -1; sign <= 1; sign += 2)
        {
            var normal = axes[axis] * sign;
            var u = axes[(axis + 1) % 3] * inset;
            var v = axes[(axis + 2) % 3] * inset;
            var center = normal * half;
            faces.Add(new Face(normal, center - u - v, center + u - v, center + u + v, center - u + v));
        }
        for (var a = 0; a < 3; a++)
        for (var b = a + 1; b < 3; b++)
        for (var sa = -1; sa <= 1; sa += 2)
        for (var sb = -1; sb <= 1; sb += 2)
        {
            var u = axes[a] * sa;
            var v = axes[b] * sb;
            var edge = axes[3 - a - b] * inset;
            var start = u * half + v * inset;
            var end = u * inset + v * half;
            faces.Add(new Face(u + v, start - edge, start + edge, end + edge, end - edge));
        }
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            faces.Add(new Face(new Vector3(x, y, z),
                new Vector3(x * half, y * inset, z * inset),
                new Vector3(x * inset, y * half, z * inset),
                new Vector3(x * inset, y * inset, z * half)));
        }
        return faces.ToArray();
    }
}
}
