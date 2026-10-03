using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoBackdropMotion
{
    private struct Body
    {
        public RectTransform rect;
        public GlassDemoFloatingTile graphic;
        public Vector2 position, velocity;
        public Quaternion orientation;
        public Vector3 spinAxis;
        public float spinSpeed, radius, inverseMass;
    }

    private readonly Body[] bodies;

    public GlassDemoBackdropMotion(RectTransform[] objects)
    {
        bodies = new Body[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            bodies[i].rect = objects[i];
            bodies[i].graphic = objects[i].GetComponent<GlassDemoFloatingTile>();
        }
    }

    public void Reset(Rect bounds)
    {
        var random = new System.Random(4187);
        for (var i = 0; i < bodies.Length; i++)
        {
            ref var body = ref bodies[i];
            body.radius = body.graphic.CollisionRadius;
            body.inverseMass = 1f / (body.radius * body.radius);
            var bestClearance = float.NegativeInfinity;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var position = new Vector2(Range(random, bounds.xMin + body.radius, bounds.xMax - body.radius), Range(random, bounds.yMin + body.radius, bounds.yMax - body.radius));
                var clearance = float.PositiveInfinity;
                for (var j = 0; j < i; j++)
                    clearance = Mathf.Min(clearance, Vector2.Distance(position, bodies[j].position) - body.radius - bodies[j].radius);
                if (clearance > bestClearance)
                {
                    bestClearance = clearance;
                    body.position = position;
                }
                if (clearance >= 24f) break;
            }
            var heading = Range(random, 0f, Mathf.PI * 2f);
            body.velocity = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * Range(random, 45f, 100f);
            body.orientation = Quaternion.Euler(Range(random, 0f, 360f), Range(random, 0f, 360f), Range(random, 0f, 360f));
            var axisZ = Range(random, -1f, 1f);
            var axisAngle = Range(random, 0f, Mathf.PI * 2f);
            var axisXY = Mathf.Sqrt(1f - axisZ * axisZ);
            body.spinAxis = new Vector3(axisXY * Mathf.Cos(axisAngle), axisXY * Mathf.Sin(axisAngle), axisZ);
            body.spinSpeed = Range(random, 15f, 55f);
        }
        Step(0f, bounds);
    }

    public void Step(float deltaTime, Rect bounds)
    {
        var steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime * 120f));
        var stepTime = deltaTime / steps;
        for (var step = 0; step < steps; step++)
        {
            for (var i = 0; i < bodies.Length; i++)
            {
                bodies[i].position += bodies[i].velocity * stepTime;
                Confine(ref bodies[i], bounds);
            }
            for (var iteration = 0; iteration < 2; iteration++)
            {
                for (var a = 0; a < bodies.Length; a++)
                for (var b = a + 1; b < bodies.Length; b++)
                    ResolveContact(ref bodies[a], ref bodies[b]);
                for (var i = 0; i < bodies.Length; i++) Confine(ref bodies[i], bounds);
            }
        }
        for (var i = 0; i < bodies.Length; i++)
        {
            ref var body = ref bodies[i];
            if (deltaTime > 0f)
                body.orientation = (Quaternion.AngleAxis(body.spinSpeed * deltaTime, body.spinAxis) * body.orientation).normalized;
            body.rect.anchoredPosition = body.position;
            body.graphic.SetPose(body.orientation);
        }
    }

    private static void ResolveContact(ref Body a, ref Body b)
    {
        var separation = b.position - a.position;
        var radius = a.radius + b.radius;
        var squaredDistance = separation.sqrMagnitude;
        if (squaredDistance >= radius * radius) return;
        var distance = Mathf.Sqrt(squaredDistance);
        var normal = distance > 0.0001f ? separation / distance : Vector2.right;
        var inverseMass = a.inverseMass + b.inverseMass;
        var correction = normal * ((radius - distance) / inverseMass);
        a.position -= correction * a.inverseMass;
        b.position += correction * b.inverseMass;
        var closingSpeed = Vector2.Dot(b.velocity - a.velocity, normal);
        if (closingSpeed >= 0f) return;
        var impulse = normal * (-2f * closingSpeed / inverseMass);
        a.velocity -= impulse * a.inverseMass;
        b.velocity += impulse * b.inverseMass;
    }

    private static void Confine(ref Body body, Rect bounds)
    {
        var radius = Mathf.Min(body.radius, Mathf.Min(bounds.width, bounds.height) * 0.5f);
        var minimum = bounds.min + Vector2.one * radius;
        var maximum = bounds.max - Vector2.one * radius;
        if (body.position.x < minimum.x) { body.position.x = minimum.x; body.velocity.x = Mathf.Abs(body.velocity.x); }
        if (body.position.x > maximum.x) { body.position.x = maximum.x; body.velocity.x = -Mathf.Abs(body.velocity.x); }
        if (body.position.y < minimum.y) { body.position.y = minimum.y; body.velocity.y = Mathf.Abs(body.velocity.y); }
        if (body.position.y > maximum.y) { body.position.y = maximum.y; body.velocity.y = -Mathf.Abs(body.velocity.y); }
    }

    private static float Range(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
}
}
