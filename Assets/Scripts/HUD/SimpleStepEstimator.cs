using System;
using UnityEngine;

public class SimpleStepEstimator : MonoBehaviour
{
    [Tooltip("Avg step length in meters; SmartNavigationSystem will overwrite this from Inspector")]
    public float stepLength = 0.75f;
    [Range(0.05f, 0.5f)] public float threshold = 0.18f;
    [Range(0.2f, 0.6f)] public float minInterval = 0.28f;

    public event Action<float> OnDistance; // delta meters per detected step

    float baseline;   // LPF |accel|
    float lastStepTime;

    void Update()
    {
        // |accel| around 1g at rest
        float mag = Input.acceleration.magnitude;
        baseline = Mathf.Lerp(baseline, mag, 0.10f);
        float delta = mag - baseline;

        if (delta > threshold && (Time.time - lastStepTime) > minInterval)
        {
            lastStepTime = Time.time;
            OnDistance?.Invoke(stepLength);
        }
    }
}