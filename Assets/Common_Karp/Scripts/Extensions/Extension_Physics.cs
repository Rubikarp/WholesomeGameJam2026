using UnityEngine;

public static class Extension_Physics
{
    public static Rigidbody ChangeDirection(this Rigidbody rigidbody, Vector3 direction)
    {
        if (direction.sqrMagnitude == 0f) return rigidbody;
        direction.Normalize();
        rigidbody.linearVelocity = direction * rigidbody.linearVelocity.magnitude;

        return rigidbody;
    }
    public static Rigidbody2D ChangeDirection(this Rigidbody2D rigidbody, Vector3 direction)
    {
        if (direction.sqrMagnitude == 0f) return rigidbody;
        direction.Normalize();
        rigidbody.linearVelocity = direction * rigidbody.linearVelocity.magnitude;

        return rigidbody;
    }

    public static Rigidbody Stop(this Rigidbody rigidbody)
    {
        rigidbody.linearVelocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;
        return rigidbody;
    }
    public static Rigidbody2D Stop(this Rigidbody2D rigidbody)
    {
        rigidbody.linearVelocity = Vector2.zero;
        rigidbody.angularVelocity = 0f;
        return rigidbody;
    }
}
