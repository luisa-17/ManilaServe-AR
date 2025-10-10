using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class ReticleCircle : MonoBehaviour
{
    public float radius = 0.15f;
    [Range(12, 128)] public int segments = 48;
    public float lineWidth = 0.015f;

    LineRenderer lr;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = segments + 1;
        lr.loop = true;
        lr.widthMultiplier = lineWidth;

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float ang = t * Mathf.PI * 2f;
            float x = Mathf.Cos(ang) * radius;
            float z = Mathf.Sin(ang) * radius;
            lr.SetPosition(i, new Vector3(x, 0f, z)); // local circle in XZ
        }
    }
}