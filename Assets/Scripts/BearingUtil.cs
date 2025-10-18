using UnityEngine;

public static class BearingUtil
{
    // 0° = +Z, 90° = +X
    public static float WorldBearingDeg(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; d.y = 0f;
        if (d.sqrMagnitude < 1e-6f) return 0f;
        return (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
    }

    public static Vector3 YawToVector(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
    }
}