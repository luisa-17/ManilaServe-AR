using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Comprehensive Testing for Breadth-First Search (BFS) - Unweighted Grid
/// Objective 1 - Navigation System
/// ADJUSTED: No specific percentage targets - qualitative assessment
/// Metrics: Functional correctness, compute time performance
/// </summary>
public class BFSPathfindingTests : MonoBehaviour
{
    [Header("Test Configuration")]
    [SerializeField] private SmartNavigationSystem navigationSystem;
    [SerializeField] private bool runTestsOnStart = false;

    private List<BFSTestResult> testResults = new List<BFSTestResult>();

    // Confusion Matrix Components
    private int truePositives = 0;  // Correct shortest paths found
    private int falsePositives = 0; // Suboptimal paths
    private int falseNegatives = 0; // Missed paths (no path found)

    private class BFSTestResult
    {
        public int scenarioId;
        public Vector3 start;
        public Vector3 goal;
        public int gridSize;
        public float obstacleDensity;
        public bool pathFound;
        public float pathLength;          // Actual distance in meters
        public float optimalPathLength;   // Straight-line distance
        public List<Vector3> path;
        public bool isShortestPath;
        public float computeTime;
    }

    void Start()
    {
        if (runTestsOnStart)
        {
            RunAllBFSTests();
        }
    }

    [ContextMenu("Run All BFS Tests")]
    public void RunAllBFSTests()
    {
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("    BFS UNWEIGHTED PATHFINDING TEST SUITE");
        Debug.Log("═══════════════════════════════════════════════════");

        ResetTestResults();

        // Test Set 1: Low Obstacle Density (20 tests, 0-10%)
        RunBFSTestSet(20, 0f, 0.1f, "Low Obstacle Density");

        // Test Set 2: Medium Obstacle Density (30 tests, 10-25%)
        RunBFSTestSet(30, 0.1f, 0.25f, "Medium Obstacle Density");

        // Test Set 3: High Obstacle Density (30 tests, 25-40%)
        RunBFSTestSet(30, 0.25f, 0.4f, "High Obstacle Density");

        CalculateBFSMetrics();
    }

    private void ResetTestResults()
    {
        testResults.Clear();
        truePositives = 0;
        falsePositives = 0;
        falseNegatives = 0;
    }

    private void RunBFSTestSet(int numTests, float minObstacles, float maxObstacles, string setName)
    {
        Debug.Log($"\n┌─────────────────────────────────────────");
        Debug.Log($"│ {setName} ({numTests} scenarios)");
        Debug.Log($"└─────────────────────────────────────────");

        for (int i = 0; i < numTests; i++)
        {
            float obstacleDensity = UnityEngine.Random.Range(minObstacles, maxObstacles);
            RunSingleBFSTest(i, 10, obstacleDensity);
        }
    }

    private void RunSingleBFSTest(int scenarioId, int gridSize, float obstacleDensity)
    {
        var testResult = new BFSTestResult
        {
            scenarioId = scenarioId,
            gridSize = gridSize,
            obstacleDensity = obstacleDensity
        };

        // Generate random start and goal
        Vector3 start = GenerateRandomPosition(gridSize);
        Vector3 goal = GenerateRandomPosition(gridSize);

        testResult.start = start;
        testResult.goal = goal;

        // Create unweighted grid (all edges have weight 1)
        var allWaypoints = FindObjectsOfType<NavigationWaypoint>();
        var availableWaypoints = FilterByObstacleDensity(allWaypoints, obstacleDensity);

        // Measure computation time
        Stopwatch stopwatch = Stopwatch.StartNew();

        List<NavigationWaypoint> waypointPath = null;
        bool pathFound = false;

        try
        {
            var startWP = FindNearestWaypoint(start, availableWaypoints);
            var goalWP = FindNearestWaypoint(goal, availableWaypoints);

            if (startWP != null && goalWP != null)
            {
                waypointPath = RunBFSAlgorithm(startWP, goalWP);

                if (waypointPath != null && waypointPath.Count > 0)
                {
                    pathFound = true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"BFS Test {scenarioId} failed: {e.Message}");
        }

        stopwatch.Stop();
        testResult.computeTime = (float)stopwatch.Elapsed.TotalMilliseconds;

        if (pathFound && waypointPath != null)
        {
            testResult.pathFound = true;
            testResult.path = waypointPath.Select(wp => wp.transform.position).ToList();

            // ✅ FIX: Calculate actual distance in meters (not waypoint count)
            testResult.pathLength = CalculatePathDistance(waypointPath);

            // Calculate optimal distance (straight line XZ)
            Vector2 startXZ = new Vector2(start.x, start.z);
            Vector2 goalXZ = new Vector2(goal.x, goal.z);
            testResult.optimalPathLength = Vector2.Distance(startXZ, goalXZ);

            // Path is optimal if within 15% of straight-line distance
            float tolerance = 1.15f;
            testResult.isShortestPath = testResult.pathLength <= testResult.optimalPathLength * tolerance;

            if (testResult.isShortestPath)
            {
                truePositives++;
            }
            else
            {
                falsePositives++; // Suboptimal path
            }
        }
        else
        {
            testResult.pathFound = false;
            testResult.pathLength = 0;
            testResult.optimalPathLength = 0;
            falseNegatives++; // Failed to find path
        }

        testResults.Add(testResult);
        LogBFSTestResult(testResult);
    }

    /// <summary>
    /// ✅ FIX: Calculate actual distance in meters (not waypoint count)
    /// </summary>
    private float CalculatePathDistance(List<NavigationWaypoint> path)
    {
        if (path == null || path.Count < 2) return 0f;

        float totalDistance = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            if (path[i] == null || path[i + 1] == null)
            {
                Debug.LogWarning($"[BFS] Null waypoint at index {i}");
                continue;
            }

            Vector3 a = path[i].transform.position;
            Vector3 b = path[i + 1].transform.position;

            // Use XZ distance (ignore Y for ground distance)
            Vector3 flatA = new Vector3(a.x, 0, a.z);
            Vector3 flatB = new Vector3(b.x, 0, b.z);

            float segmentDistance = Vector3.Distance(flatA, flatB);
            totalDistance += segmentDistance;
        }

        return totalDistance;
    }

    private List<NavigationWaypoint> RunBFSAlgorithm(NavigationWaypoint start, NavigationWaypoint goal)
    {
        if (start == null || goal == null)
        {
            Debug.LogError("[BFS] Start or goal is null!");
            return null;
        }

        if (start == goal)
        {
            return new List<NavigationWaypoint> { start };
        }

        Queue<NavigationWaypoint> queue = new Queue<NavigationWaypoint>();
        HashSet<NavigationWaypoint> visited = new HashSet<NavigationWaypoint>();
        Dictionary<NavigationWaypoint, NavigationWaypoint> parent = new Dictionary<NavigationWaypoint, NavigationWaypoint>();

        queue.Enqueue(start);
        visited.Add(start);
        parent[start] = null; // ✅ Mark start's parent as null

        int iterations = 0;
        int maxIterations = 10000; // Safety limit

        while (queue.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            NavigationWaypoint current = queue.Dequeue();

            // Found the goal!
            if (current == goal)
            {
                return ReconstructPath(parent, goal, start);
            }

            // Explore all connected neighbors
            if (current.connectedWaypoints == null || current.connectedWaypoints.Count == 0)
            {
                continue;
            }

            foreach (var neighbor in current.connectedWaypoints)
            {
                if (neighbor == null || visited.Contains(neighbor))
                    continue;

                visited.Add(neighbor);
                parent[neighbor] = current; // ✅ Track parent for path reconstruction
                queue.Enqueue(neighbor);
            }
        }

        if (iterations >= maxIterations)
        {
            Debug.LogError($"[BFS] Max iterations reached from {start.name} to {goal.name}");
        }

        return null;
    }

    /// <summary>
    /// ✅ FIX: Robust path reconstruction with safety checks
    /// </summary>
    private List<NavigationWaypoint> ReconstructPath(
        Dictionary<NavigationWaypoint, NavigationWaypoint> parent,
        NavigationWaypoint goal,
        NavigationWaypoint start)
    {
        List<NavigationWaypoint> path = new List<NavigationWaypoint>();
        NavigationWaypoint current = goal;

        int safety = 0;
        int maxSteps = 1000; // Prevent infinite loops

        // Walk backwards from goal to start
        while (current != null && safety < maxSteps)
        {
            safety++;
            path.Add(current);

            if (current == start) break;

            if (!parent.ContainsKey(current))
            {
                Debug.LogError($"[BFS] Path reconstruction failed at {current.name}! No parent found.");
                Debug.LogError($"[BFS] Partial path has {path.Count} waypoints");
                return null;
            }

            current = parent[current];
        }

        if (safety >= maxSteps)
        {
            Debug.LogError("[BFS] Path reconstruction exceeded max steps - possible cycle!");
            return null;
        }

        if (current != start)
        {
            Debug.LogError($"[BFS] Path doesn't reach start! Ended at {current?.name ?? "null"}");
            return null;
        }

        path.Reverse(); // Make it start → goal

        return path;
    }

    private void CalculateBFSMetrics()
    {
        Debug.Log("\n═══════════════════════════════════════════════════");
        Debug.Log("         BFS FINAL TEST RESULTS");
        Debug.Log("═══════════════════════════════════════════════════");

        int totalTests = testResults.Count;
        int successfulPaths = testResults.Count(t => t.pathFound);

        // Confusion Matrix
        Debug.Log($"\n📊 Confusion Matrix:");
        Debug.Log($"   TP (Correct shortest paths):  {truePositives}");
        Debug.Log($"   FP (Suboptimal paths):        {falsePositives}");
        Debug.Log($"   FN (Missed paths):            {falseNegatives}");

        // ✅ ADJUSTED: No specific percentage target
        float correctness = (float)truePositives / totalTests * 100f;
        Debug.Log($"\n📈 Shortest-Path Correctness:");
        Debug.Log($"   Formula: Correct shortest paths / Total paths × 100");
        Debug.Log($"   = {truePositives} / {totalTests} × 100");
        Debug.Log($"   Result: {correctness:F2}%");
        Debug.Log($"   Assessment: {GetQualitativeAssessment(correctness)}");

        // Reachability Rate
        float reachability = (float)successfulPaths / totalTests * 100f;
        Debug.Log($"\n🎯 Reachability Rate:");
        Debug.Log($"   Formula: Paths Found / Total Tests × 100");
        Debug.Log($"   = {successfulPaths} / {totalTests} × 100");
        Debug.Log($"   Result: {reachability:F2}%");
        Debug.Log($"   Target: ≥95% {(reachability >= 95f ? "✓ PASS" : "✗ FAIL")}");

        // Average Compute Time
        float avgComputeTime = testResults.Average(t => t.computeTime);
        Debug.Log($"\n⚡ Average Compute Time:");
        Debug.Log($"   Calculation: Total time / Total tests");
        Debug.Log($"   = {testResults.Sum(t => t.computeTime):F2} ms / {totalTests} tests");
        Debug.Log($"   = {avgComputeTime:F2} ms per scenario");
        Debug.Log($"   Target: ≤60 ms {(avgComputeTime <= 60f ? "✓ PASS" : "✗ FAIL")}");

        // Performance by Obstacle Density
        Debug.Log($"\n📊 Performance by Obstacle Density:");
        var lowDensity = testResults.Where(t => t.obstacleDensity < 0.15f);
        var medDensity = testResults.Where(t => t.obstacleDensity >= 0.15f && t.obstacleDensity < 0.3f);
        var highDensity = testResults.Where(t => t.obstacleDensity >= 0.3f);

        if (lowDensity.Any())
            Debug.Log($"   Low (0-15%):    {lowDensity.Count(t => t.isShortestPath)}/{lowDensity.Count()} correct, {lowDensity.Average(t => t.computeTime):F2}ms avg");
        if (medDensity.Any())
            Debug.Log($"   Medium (15-30%): {medDensity.Count(t => t.isShortestPath)}/{medDensity.Count()} correct, {medDensity.Average(t => t.computeTime):F2}ms avg");
        if (highDensity.Any())
            Debug.Log($"   High (30-40%):  {highDensity.Count(t => t.isShortestPath)}/{highDensity.Count()} correct, {highDensity.Average(t => t.computeTime):F2}ms avg");

        // ✅ Path Length Analysis (with actual distances)
        var successfulTests = testResults.Where(t => t.pathFound && t.pathLength > 0).ToList();
        if (successfulTests.Any())
        {
            Debug.Log($"\n📏 Path Length Analysis:");
            Debug.Log($"   Average path length: {successfulTests.Average(t => t.pathLength):F2}m");
            Debug.Log($"   Average optimal length: {successfulTests.Average(t => t.optimalPathLength):F2}m");

            float avgRatio = successfulTests.Average(t => t.pathLength / t.optimalPathLength);
            Debug.Log($"   Average path ratio: {avgRatio:F2}x straight-line");
            Debug.Log($"   Quality: {GetPathQualityDescription(avgRatio)}");
        }

        // Connectivity Check
        int totalCells = 100; // 10x10 grid
        int avgWalkableCells = Mathf.RoundToInt(totalCells * (1 - testResults.Average(t => t.obstacleDensity)));
        int reachableCells = testResults.Count(t => t.pathFound);
        Debug.Log($"\n🔗 Connectivity Check:");
        Debug.Log($"   Total cells: {totalCells}");
        Debug.Log($"   Average walkable cells: {avgWalkableCells}");
        Debug.Log($"   Reachable from start: {reachableCells}/{totalTests} tests");

        // Summary
        Debug.Log($"\n═══════════════════════════════════════════════════");
        Debug.Log($"                    SUMMARY");
        Debug.Log($"═══════════════════════════════════════════════════");
        Debug.Log($"Total Tests:          {totalTests}");
        Debug.Log($"Paths Found:          {successfulPaths}");
        Debug.Log($"Optimal Paths:        {truePositives}");
        Debug.Log($"\nFINAL VERDICT:");

        // ✅ ADJUSTED: Performance-based verdict
        bool computeTimePass = avgComputeTime <= 60f;
        bool reachabilityPass = reachability >= 95f;
        bool functionalQuality = correctness >= 85f; // Functional threshold

        Debug.Log($"Compute Time:    {(computeTimePass ? "✓ PASS" : "✗ FAIL")}");
        Debug.Log($"Reachability:    {(reachabilityPass ? "✓ PASS" : "✗ FAIL")}");
        Debug.Log($"Correctness:     {correctness:F1}% - {GetQualitativeAssessment(correctness)}");

        bool allPassed = computeTimePass && reachabilityPass && functionalQuality;
        Debug.Log(allPassed ? "\n✓✓✓ BFS ALGORITHM FUNCTIONAL ✓✓✓" : "\n✗✗✗ SOME METRICS FAILED ✗✗✗");
        Debug.Log($"═══════════════════════════════════════════════════");
    }

    private string GetQualitativeAssessment(float percentage)
    {
        if (percentage >= 95f) return "Excellent";
        if (percentage >= 90f) return "Very Good";
        if (percentage >= 85f) return "Good/Functional";
        if (percentage >= 75f) return "Acceptable";
        return "Needs Improvement";
    }

    private string GetPathQualityDescription(float ratio)
    {
        if (ratio <= 1.1f) return "Excellent (near-optimal)";
        if (ratio <= 1.3f) return "Very Good";
        if (ratio <= 1.5f) return "Good/Acceptable";
        if (ratio <= 2.0f) return "Fair (some detours)";
        return "Poor (major detours)";
    }

    // Helper Methods
    private Vector3 GenerateRandomPosition(int gridSize)
    {
        return new Vector3(
            UnityEngine.Random.Range(0, gridSize),
            0,
            UnityEngine.Random.Range(0, gridSize)
        );
    }

    private NavigationWaypoint[] FilterByObstacleDensity(NavigationWaypoint[] waypoints, float density)
    {
        int removeCount = Mathf.RoundToInt(waypoints.Length * density);
        return waypoints.OrderBy(x => UnityEngine.Random.value)
                       .Skip(removeCount)
                       .ToArray();
    }

    private NavigationWaypoint FindNearestWaypoint(Vector3 pos, NavigationWaypoint[] waypoints)
    {
        return waypoints.OrderBy(wp => Vector3.Distance(pos, wp.transform.position)).FirstOrDefault();
    }

    private void LogBFSTestResult(BFSTestResult result)
    {
        string status = result.isShortestPath ? "✓" : (result.pathFound ? "~" : "✗");
        string pathInfo = result.pathFound ?
            $"{result.pathLength:F2}m (optimal: {result.optimalPathLength:F2}m)" :
            "NO PATH";

        Debug.Log($"{status} Test {result.scenarioId}: " +
                  $"{result.obstacleDensity * 100:F0}% obstacles, " +
                  $"{pathInfo}, " +
                  $"{result.computeTime:F2}ms");
    }
}