using UnityEngine;
using System.Collections.Generic;

public class SimplePathTest : MonoBehaviour
{
    public Transform[] waypoints;
    public LineRenderer pathLine;

    void Start()
    {
        // Create line renderer if none exists
        if (pathLine == null)
        {
            pathLine = gameObject.AddComponent<LineRenderer>();
            pathLine.startWidth = 0.2f;
            pathLine.endWidth = 0.2f;
            pathLine.startColor = Color.cyan;
            pathLine.endColor = Color.cyan;
        }

        // Get all waypoints automatically
        GameObject[] waypointObjs = GameObject.FindGameObjectsWithTag("Untagged");
        List<Transform> foundWaypoints = new List<Transform>();

        foreach (GameObject obj in waypointObjs)
        {
            if (obj.name.Contains("Waypoint"))
            {
                foundWaypoints.Add(obj.transform);
            }
        }

        waypoints = foundWaypoints.ToArray();
        DrawTestPath();

        Debug.Log($"Found {waypoints.Length} waypoints for testing");
    }

    void DrawTestPath()
    {
        if (waypoints.Length > 0)
        {
            Vector3[] positions = new Vector3[waypoints.Length];
            for (int i = 0; i < waypoints.Length; i++)
            {
                positions[i] = waypoints[i].position;
            }

            pathLine.positionCount = positions.Length;
            pathLine.SetPositions(positions);
        }
    }
}