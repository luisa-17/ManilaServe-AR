using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// ✅ COMPLETE: A* Pathfinding Tests with Full Confusion Matrix
/// Manila City Hall AR Navigation System
/// 
/// CONFUSION MATRIX:
/// - TP (True Positive): Path expected AND found correctly
/// - FP (False Positive): Path found BUT incorrect OR shouldn't exist
/// - TN (True Negative): Path NOT expected AND correctly not found
/// - FN (False Negative): Path expected BUT not found
/// </summary>
public class AStarPathfindingTests : MonoBehaviour
{
    [Header("Test Configuration")]
    [SerializeField] private SmartNavigationSystem navigationSystem;
    [SerializeField] private bool runTestsOnStart = false;
    [SerializeField] private bool logDetailedResults = true;
    [SerializeField] private int totalTestsToRun = 110;

    [Header("Test Balance")]
    [Tooltip("Test unreachable pairs for True Negatives (TN)")]
    [SerializeField] private bool testUnreachablePairs = true;
    [Tooltip("Percentage of tests that should be unreachable (for TN)")]
    [SerializeField][Range(0f, 0.5f)] private float unreachableTestRatio = 0.2f; // 20%

    private const float TARGET_ARRIVAL_ACCURACY = 90f;
    private const float TARGET_PATH_OPTIMALITY = 85f;
    private const float TARGET_COMPUTE_TIME = 80f;

    // Confusion Matrix Metrics
    private int truePositives = 0;   // TP: Path expected & found correctly
    private int falsePositives = 0;  // FP: Path found but incorrect
    private int trueNegatives = 0;   // TN: No path expected & none found
    private int falseNegatives = 0;  // FN: Path expected but not found

    private List<TestResult> testResults = new List<TestResult>();

    private class TestResult
    {
        public int scenarioId;
        public string testName;
        public NavigationWaypoint startWP;
        public NavigationWaypoint goalWP;
        public bool shouldBeReachable;  // Expected result
        public bool pathFound;          // Actual result
        public bool arrivedCorrectly;
        public float computeTime;
        public List<NavigationWaypoint> waypointPath;
        public float actualDistance;
        public float theoreticalDistance;
        public float pathOptimality;
        public string confusionClass; // TP, FP, TN, or FN
    }

    void Start()
    {
        if (runTestsOnStart)
        {
            RunAllTests();
        }
    }

    [ContextMenu("Run All A* Tests (Complete Confusion Matrix)")]
    public void RunAllTests()
    {
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("    A* PATHFINDING COMPLETE TEST SUITE");
        Debug.Log("    Full Confusion Matrix: TP, FP, TN, FN");
        Debug.Log("═══════════════════════════════════════════════════\n");

        ResetTestResults();

        var allWaypoints = FindObjectsOfType<NavigationWaypoint>();

        if (allWaypoints == null || allWaypoints.Length < 2)
        {
            Debug.LogError("❌ NO WAYPOINTS FOUND IN SCENE! Cannot run tests.");
            return;
        }

        Debug.Log($"✓ Found {allWaypoints.Length} waypoints in scene");

        // Analyze graph connectivity
        var graphAnalysis = AnalyzeGraphConnectivity(allWaypoints);

        Debug.Log($"✓ Graph Islands: {graphAnalysis.islands.Count}");
        Debug.Log($"✓ Reachable pairs: {graphAnalysis.reachablePairs:N0}");
        Debug.Log($"✓ Unreachable pairs: {graphAnalysis.unreachablePairs:N0}\n");

        // Calculate test distribution
        int unreachableTests = 0;
        if (testUnreachablePairs && graphAnalysis.unreachablePairs > 0)
        {
            unreachableTests = Mathf.RoundToInt(totalTestsToRun * unreachableTestRatio);
            unreachableTests = Mathf.Min(unreachableTests, graphAnalysis.unreachablePairs);
        }

        int reachableTests = totalTestsToRun - unreachableTests;

        Debug.Log($"✓ Testing {reachableTests} reachable paths (for TP/FN)");
        Debug.Log($"✓ Testing {unreachableTests} unreachable paths (for TN/FP)\n");

        // Run tests
        int testCount = 0;

        // Test reachable pairs
        RunReachableTests(graphAnalysis, reachableTests, ref testCount);

        // Test unreachable pairs
        if (unreachableTests > 0)
        {
            RunUnreachableTests(graphAnalysis, unreachableTests, ref testCount);
        }

        CalculateFinalMetrics();
    }

    private GraphAnalysis AnalyzeGraphConnectivity(NavigationWaypoint[] allWaypoints)
    {
        var analysis = new GraphAnalysis();
        var unvisited = new HashSet<NavigationWaypoint>(allWaypoints);

        // Find all disconnected islands using BFS
        while (unvisited.Count > 0)
        {
            var start = unvisited.First();
            var island = new HashSet<NavigationWaypoint>();
            var queue = new Queue<NavigationWaypoint>();

            queue.Enqueue(start);
            island.Add(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.connectedWaypoints == null) continue;

                foreach (var neighbor in current.connectedWaypoints)
                {
                    if (neighbor != null && !island.Contains(neighbor) && unvisited.Contains(neighbor))
                    {
                        island.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            analysis.islands.Add(island);
            foreach (var node in island)
                unvisited.Remove(node);
        }

        // Calculate reachable pairs (within same island)
        foreach (var island in analysis.islands)
        {
            int size = island.Count;
            analysis.reachablePairs += size * (size - 1); // All pairs within island
        }

        // Calculate unreachable pairs (between different islands)
        int totalPairs = allWaypoints.Length * (allWaypoints.Length - 1);
        analysis.unreachablePairs = totalPairs - analysis.reachablePairs;

        analysis.largestIsland = analysis.islands.OrderByDescending(i => i.Count).First();

        return analysis;
    }

    private void RunReachableTests(GraphAnalysis analysis, int count, ref int testCount)
    {
        var island = analysis.largestIsland.ToArray();
        System.Random rng = new System.Random();

        // Shuffle for random distribution
        island = island.OrderBy(x => rng.Next()).ToArray();

        for (int i = 0; i < island.Length && testCount < count; i++)
        {
            for (int j = 0; j < island.Length && testCount < count; j++)
            {
                if (i == j) continue; // Skip same waypoint

                RunSinglePathfindingTest(testCount, island[i], island[j], shouldBeReachable: true);
                testCount++;
            }
        }
    }

    private void RunUnreachableTests(GraphAnalysis analysis, int count, ref int testCount)
    {
        if (analysis.islands.Count < 2)
        {
            Debug.LogWarning("⚠️ Cannot test unreachable pairs - graph has only 1 island!");
            return;
        }

        System.Random rng = new System.Random();
        var islandList = analysis.islands.ToList();
        int testsRun = 0;

        while (testsRun < count)
        {
            // Pick two different islands
            int idx1 = rng.Next(islandList.Count);
            int idx2 = rng.Next(islandList.Count);

            while (idx1 == idx2 && islandList.Count > 1)
            {
                idx2 = rng.Next(islandList.Count);
            }

            var island1 = islandList[idx1];
            var island2 = islandList[idx2];

            var start = island1.ElementAt(rng.Next(island1.Count));
            var goal = island2.ElementAt(rng.Next(island2.Count));

            RunSinglePathfindingTest(testCount, start, goal, shouldBeReachable: false);
            testCount++;
            testsRun++;
        }
    }

    private void ResetTestResults()
    {
        testResults.Clear();
        truePositives = 0;
        falsePositives = 0;
        trueNegatives = 0;
        falseNegatives = 0;
    }

    private void RunSinglePathfindingTest(int scenarioId, NavigationWaypoint start, NavigationWaypoint goal, bool shouldBeReachable)
    {
        var testResult = new TestResult
        {
            scenarioId = scenarioId,
            startWP = start,
            goalWP = goal,
            shouldBeReachable = shouldBeReachable,
            testName = $"{start.name} → {goal.name}"
        };

        testResult.theoreticalDistance = Vector3.Distance(
            start.transform.position,
            goal.transform.position
        );

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            testResult.waypointPath = RunAStarAlgorithm(start, goal);
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Test {scenarioId} failed with exception: {e.Message}");
            testResult.waypointPath = null;
        }

        stopwatch.Stop();
        testResult.computeTime = (float)stopwatch.Elapsed.TotalMilliseconds;

        // Evaluate path and classify into confusion matrix
        if (testResult.waypointPath != null && testResult.waypointPath.Count >= 2)
        {
            testResult.pathFound = true;
            testResult.actualDistance = CalculatePathDistance(testResult.waypointPath);

            if (testResult.actualDistance > 0.001f)
            {
                testResult.pathOptimality = (testResult.theoreticalDistance / testResult.actualDistance) * 100f;
                testResult.pathOptimality = Mathf.Min(testResult.pathOptimality, 100f);
            }

            Vector3 finalPos = testResult.waypointPath[testResult.waypointPath.Count - 1].transform.position;
            Vector3 goalPos = goal.transform.position;
            float distanceToGoal = Vector3.Distance(finalPos, goalPos);
            testResult.arrivedCorrectly = distanceToGoal < 2.0f; // 2m threshold

            // ✅ CONFUSION MATRIX CLASSIFICATION
            if (shouldBeReachable)
            {
                if (testResult.arrivedCorrectly)
                {
                    truePositives++;
                    testResult.confusionClass = "TP";
                }
                else
                {
                    falsePositives++;
                    testResult.confusionClass = "FP";
                }
            }
            else // Should be unreachable
            {
                falsePositives++; // Found path where none should exist
                testResult.confusionClass = "FP";
            }
        }
        else
        {
            testResult.pathFound = false;
            testResult.arrivedCorrectly = false;
            testResult.actualDistance = 0f;
            testResult.pathOptimality = 0f;

            // ✅ CONFUSION MATRIX CLASSIFICATION
            if (shouldBeReachable)
            {
                falseNegatives++; // Expected path but none found
                testResult.confusionClass = "FN";
            }
            else
            {
                trueNegatives++; // Correctly identified as unreachable
                testResult.confusionClass = "TN";
            }
        }

        testResults.Add(testResult);

        // Logging
        if (logDetailedResults)
        {
            string status = testResult.confusionClass == "TP" ? "✓" :
                           testResult.confusionClass == "TN" ? "✓" :
                           testResult.confusionClass == "FP" ? "⚠️" : "✗";

            string expectedStr = shouldBeReachable ? "REACHABLE" : "UNREACHABLE";

            Debug.Log($"{status} Test {scenarioId} [{testResult.confusionClass}]: {testResult.testName}");
            Debug.Log($"   Expected: {expectedStr} | Found: {testResult.pathFound} | Arrived: {testResult.arrivedCorrectly}");

            if (testResult.pathFound)
            {
                Debug.Log($"   Path: {testResult.actualDistance:F2}m, Optimality: {testResult.pathOptimality:F1}%, Time: {testResult.computeTime:F2}ms");
            }
        }
    }

    private void CalculateFinalMetrics()
    {
        Debug.Log("\n═══════════════════════════════════════════════════");
        Debug.Log("         A* PATHFINDING TEST RESULTS");
        Debug.Log("         COMPLETE CONFUSION MATRIX");
        Debug.Log("═══════════════════════════════════════════════════\n");

        int totalTests = testResults.Count;

        if (totalTests == 0)
        {
            Debug.LogError("No tests were run!");
            return;
        }

        // ✅ COMPLETE CONFUSION MATRIX
        Debug.Log("📊 Confusion Matrix (2x2):");
        Debug.Log("");
        Debug.Log("                 │  Predicted: Path  │  Predicted: No Path");
        Debug.Log("─────────────────┼───────────────────┼────────────────────");
        Debug.Log($" Actual: Path    │  TP = {truePositives,-6}      │  FN = {falseNegatives,-6}");
        Debug.Log($" Actual: No Path │  FP = {falsePositives,-6}      │  TN = {trueNegatives,-6}");
        Debug.Log("");

        // Explanations
        Debug.Log("📝 Confusion Matrix Definitions:");
        Debug.Log($"   TP (True Positive):  {truePositives} - Path expected & found correctly ✓");
        Debug.Log($"   FP (False Positive): {falsePositives} - Path found but shouldn't exist ✗");
        Debug.Log($"   TN (True Negative):  {trueNegatives} - No path expected & none found ✓");
        Debug.Log($"   FN (False Negative): {falseNegatives} - Path expected but not found ✗");
        Debug.Log("");

        // Calculate metrics
        float accuracy = totalTests > 0 ? ((float)(truePositives + trueNegatives) / totalTests) * 100f : 0f;
        float precision = (truePositives + falsePositives) > 0 ? ((float)truePositives / (truePositives + falsePositives)) * 100f : 0f;
        float recall = (truePositives + falseNegatives) > 0 ? ((float)truePositives / (truePositives + falseNegatives)) * 100f : 0f;
        float f1Score = (precision + recall) > 0 ? (2 * precision * recall) / (precision + recall) : 0f;
        float specificity = (trueNegatives + falsePositives) > 0 ? ((float)trueNegatives / (trueNegatives + falsePositives)) * 100f : 0f;

        Debug.Log("🎯 Classification Metrics:");
        Debug.Log($"   Accuracy:    {accuracy:F2}% = (TP + TN) / Total");
        Debug.Log($"   Precision:   {precision:F2}% = TP / (TP + FP)");
        Debug.Log($"   Recall:      {recall:F2}% = TP / (TP + FN)");
        Debug.Log($"   F1-Score:    {f1Score:F2}% = 2 × (Precision × Recall) / (Precision + Recall)");
        Debug.Log($"   Specificity: {specificity:F2}% = TN / (TN + FP)");
        Debug.Log("");

        // Path quality metrics (for successful paths only)
        var successfulPaths = testResults.Where(t => t.pathFound && t.arrivedCorrectly).ToList();

        if (successfulPaths.Any())
        {
            float avgOptimality = successfulPaths.Average(t => t.pathOptimality);
            float avgDistance = successfulPaths.Average(t => t.actualDistance);
            float avgTime = successfulPaths.Average(t => t.computeTime);

            Debug.Log("📐 Path Quality Metrics (Successful Paths):");
            Debug.Log($"   Average Optimality: {avgOptimality:F2}%");
            Debug.Log($"   Average Distance: {avgDistance:F2}m");
            Debug.Log($"   Average Compute Time: {avgTime:F2}ms");
            Debug.Log("");
        }

        // Target evaluation
        Debug.Log("🎯 Target Evaluation:");
        Debug.Log($"   Accuracy:    {accuracy:F2}% {(accuracy >= TARGET_ARRIVAL_ACCURACY ? "✓ PASS" : "✗ FAIL")} (target: ≥{TARGET_ARRIVAL_ACCURACY}%)");

        if (successfulPaths.Any())
        {
            float avgOptimality = successfulPaths.Average(t => t.pathOptimality);
            float avgTime = successfulPaths.Average(t => t.computeTime);

            Debug.Log($"   Optimality:  {avgOptimality:F2}% {(avgOptimality >= TARGET_PATH_OPTIMALITY ? "✓ PASS" : "✗ FAIL")} (target: ≥{TARGET_PATH_OPTIMALITY}%)");
            Debug.Log($"   Compute Time: {avgTime:F2}ms {(avgTime <= TARGET_COMPUTE_TIME ? "✓ PASS" : "✗ FAIL")} (target: ≤{TARGET_COMPUTE_TIME}ms)");
        }
        Debug.Log("");

        // Summary
        bool allPassed = accuracy >= TARGET_ARRIVAL_ACCURACY &&
                        (!successfulPaths.Any() || successfulPaths.Average(t => t.pathOptimality) >= TARGET_PATH_OPTIMALITY) &&
                        (!successfulPaths.Any() || successfulPaths.Average(t => t.computeTime) <= TARGET_COMPUTE_TIME);

        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("                    SUMMARY");
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log($"Total Tests: {totalTests}");
        Debug.Log($"   Reachable Tests: {truePositives + falseNegatives + falsePositives}");
        Debug.Log($"   Unreachable Tests: {trueNegatives + (testResults.Count(t => !t.shouldBeReachable && t.pathFound))}");
        Debug.Log("");
        Debug.Log("FINAL VERDICT:");

        if (allPassed)
        {
            Debug.Log("✓✓✓ ALL METRICS PASSED - EXCELLENT! ✓✓✓");
        }
        else
        {
            Debug.LogWarning("⚠️ SOME METRICS FAILED - NEEDS IMPROVEMENT");
        }

        Debug.Log("═══════════════════════════════════════════════════\n");
    }

    // --- A* Algorithm Implementation ---

    private float CalculatePathDistance(List<NavigationWaypoint> path)
    {
        if (path == null || path.Count < 2)
            return 0f;

        float totalDistance = 0f;
        for (int i = 0; i < path.Count - 1; i++)
        {
            totalDistance += Vector3.Distance(
                path[i].transform.position,
                path[i + 1].transform.position
            );
        }
        return totalDistance;
    }

    private List<NavigationWaypoint> RunAStarAlgorithm(NavigationWaypoint start, NavigationWaypoint goal)
    {
        var openSet = new HashSet<NavigationWaypoint> { start };
        var closedSet = new HashSet<NavigationWaypoint>();
        var cameFrom = new Dictionary<NavigationWaypoint, NavigationWaypoint>();
        var gScore = new Dictionary<NavigationWaypoint, float> { [start] = 0f };
        var fScore = new Dictionary<NavigationWaypoint, float>();

        fScore[start] = Vector3.Distance(start.transform.position, goal.transform.position);

        int iterations = 0;
        int maxIterations = 10000;

        while (openSet.Count > 0 && iterations < maxIterations)
        {
            iterations++;

            var current = openSet.OrderBy(wp => fScore.GetValueOrDefault(wp, Mathf.Infinity)).First();

            if (current == goal)
            {
                return ReconstructPath(cameFrom, current);
            }

            openSet.Remove(current);
            closedSet.Add(current);

            if (current.connectedWaypoints == null) continue;

            foreach (var neighbor in current.connectedWaypoints)
            {
                if (neighbor == null || closedSet.Contains(neighbor)) continue;

                float movementCost = Vector3.Distance(current.transform.position, neighbor.transform.position);
                float tentativeG = gScore.GetValueOrDefault(current, Mathf.Infinity) + movementCost;

                if (!openSet.Contains(neighbor))
                {
                    openSet.Add(neighbor);
                }
                else if (tentativeG >= gScore.GetValueOrDefault(neighbor, Mathf.Infinity))
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeG;
                fScore[neighbor] = tentativeG + Vector3.Distance(neighbor.transform.position, goal.transform.position);
            }
        }

        return null;
    }

    private List<NavigationWaypoint> ReconstructPath(Dictionary<NavigationWaypoint, NavigationWaypoint> cameFrom, NavigationWaypoint current)
    {
        var path = new List<NavigationWaypoint> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }
        return path;
    }

    private class GraphAnalysis
    {
        public List<HashSet<NavigationWaypoint>> islands = new List<HashSet<NavigationWaypoint>>();
        public HashSet<NavigationWaypoint> largestIsland;
        public int reachablePairs;
        public int unreachablePairs;
    }

    [ContextMenu("Export Confusion Matrix to CSV")]
    public void ExportConfusionMatrixToCSV()
    {
        if (testResults.Count == 0)
        {
            Debug.LogWarning("No test results to export!");
            return;
        }

        string csv = "Test ID,Start,Goal,Expected,Found,Arrived,Distance(m),Optimality(%),Time(ms),Confusion Class\n";

        foreach (var result in testResults)
        {
            csv += $"{result.scenarioId}," +
                   $"{result.startWP.name}," +
                   $"{result.goalWP.name}," +
                   $"{(result.shouldBeReachable ? "Reachable" : "Unreachable")}," +
                   $"{result.pathFound}," +
                   $"{result.arrivedCorrectly}," +
                   $"{result.actualDistance:F2}," +
                   $"{result.pathOptimality:F1}," +
                   $"{result.computeTime:F2}," +
                   $"{result.confusionClass}\n";
        }

        string path = System.IO.Path.Combine(Application.dataPath, "ConfusionMatrix_Results.csv");
        System.IO.File.WriteAllText(path, csv);
        Debug.Log($"✓ Confusion matrix exported to: {path}");
    }
}