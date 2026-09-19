using UnityEngine;

// Foot trajectories are independent of the pelvis, so a stance foot stays on the ground.
public static class MinerGait
{
    public struct Foot
    {
        public Vector2 ankle;
        public float angle;
        public bool planted;
    }

    public static Foot Sample(float phase, float stride, float duty, float lift)
    {
        phase = Mathf.Repeat(phase, 1);
        duty = Mathf.Clamp(duty, .25f, .75f);
        if (phase < duty)
            return new Foot { ankle = new Vector2(stride * (.5f - phase / duty), .10f), planted = true };
        float t = (phase - duty) / (1 - duty), ratio = (1 - duty) / duty;
        float recovery = (-2 - 2 * ratio) * t * t * t + (3 + 3 * ratio) * t * t - ratio * t;
        float arc = Mathf.Sin(Mathf.PI * t);
        return new Foot {
            ankle = new Vector2(stride * (recovery - .5f), .10f + lift * arc * arc),
            angle = -24 * Mathf.Sin(Mathf.PI * 2 * t) * arc,
            planted = false
        };
    }

    public static Vector2 Knee(Vector2 hip, Vector2 ankle, float thigh, float shin)
    {
        Vector2 delta = ankle - hip;
        float distance = Mathf.Clamp(delta.magnitude, .0001f, thigh + shin - .0001f);
        Vector2 along = delta.sqrMagnitude > .000001f ? delta.normalized : Vector2.down;
        float projected = (thigh * thigh - shin * shin + distance * distance) / (2 * distance);
        float bend = Mathf.Sqrt(Mathf.Max(0, thigh * thigh - projected * projected));
        return hip + along * projected + new Vector2(-along.y, along.x) * bend;
    }
}
