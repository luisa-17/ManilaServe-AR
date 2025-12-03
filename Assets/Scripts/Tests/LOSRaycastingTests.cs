using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Comprehensive Testing for Line-of-Sight (LOS) Raycasting - Visibility Matrix
/// Objective 1 - Navigation System
/// Based on PDF Metrics:
/// - 100+ ray tests across floor models (multi-room, occlusion)
/// - Target Metrics: ≥90% visibility accuracy, ≤8% false occlusion, ≤30ms compute time
/// </summary>
public class LOSRaycastingTests : MonoBehaviour
{
    [Header("Test Configuration")]
    [SerializeField] private SmartNavigationSystem navigationSystem;
    [SerializeField] private LayerMask obstacleLayer;
    [SerializeField] private int numberOfRaySamples = 10;
    [SerializeField] private bool runTestsOnStart = false;

    private List<LOSTestResult> testResults = new List<LOSTestResult>();
    
    // Confusion Matrix Components for Visibility
    private int truePositives = 0;  // Correctly identified visible
    private int falsePositives = 0; // False visible (showed through walls)
    private int falseNegatives = 0; // False occlusion (hidden when visible)
    private int trueNegatives = 0;  // Correctly identified blocked

    private class LOSTestResult
    {
        public int testId;
        public Vector3 fromPosition;
        public Vector3 toPosition;
        public float distance;
        public List<string> obstaclesInPath;
        public VisibilityStatus expectedStatus;
        public VisibilityStatus actualStatus;
        public bool isCorrect;
        public float computeTime;
        public int samplesChecked;
    }

    private enum VisibilityStatus
    {
        Clear = 0,          // No obstruction between points
        Blocked = 1,        // Solid obstacle blocks view
        Partial = 2         // Semi-transparent material (glass/door)
    }

    void Start()
    {
        obstacleLayer = LayerMask.GetMask("Wall", "Obstacle", "Furniture");

        if (obstacleLayer.value == 0)
        {
            Debug.LogError("⚠️ OBSTACLE LAYER NOT CONFIGURED! Raycasting will fail. Ensure layers exist.");
        }

        if (runTestsOnStart)
        {
            RunAllLOSTests();
        }
    }

    [ContextMenu("Run All LOS Tests")]
    public void RunAllLOSTests()
    {
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("    LINE-OF-SIGHT RAYCASTING TEST SUITE");
        Debug.Log("═══════════════════════════════════════════════════");
        
        ResetTestResults();
        
        // Test Set 1: Clear Line of Sight (30 tests)
        RunLOSTestSet(30, VisibilityStatus.Clear, "Clear LOS Tests");
        
        // Test Set 2: Blocked by Walls (40 tests)
        RunLOSTestSet(40, VisibilityStatus.Blocked, "Wall Occlusion Tests");
        
        // Test Set 3: Partial Visibility (15 tests)
        RunLOSTestSet(15, VisibilityStatus.Partial, "Partial Visibility Tests");
        
        // Test Set 4: Multi-room Tests (15 tests)
        RunMultiRoomTests(15);
        
        CalculateLOSMetrics();
    }

    private void ResetTestResults()
    {
        testResults.Clear();
        truePositives = 0;
        falsePositives = 0;
        falseNegatives = 0;
        trueNegatives = 0;
    }

    private void RunLOSTestSet(int numTests, VisibilityStatus expectedStatus, string setName)
    {
        Debug.Log($"\n┌─────────────────────────────────────────");
        Debug.Log($"│ {setName} ({numTests} scenarios)");
        Debug.Log($"└─────────────────────────────────────────");

        for (int i = 0; i < numTests; i++)
        {
            RunSingleLOSTest(i, expectedStatus);
        }
    }

    private void RunSingleLOSTest(int testId, VisibilityStatus expectedStatus)
    {
        var testResult = new LOSTestResult
        {
            testId = testId,
            expectedStatus = expectedStatus,
            obstaclesInPath = new List<string>()
        };

        // Generate test positions based on expected status
        GenerateTestPositions(expectedStatus, out Vector3 from, out Vector3 to);
        testResult.fromPosition = from;
        testResult.toPosition = to;
        testResult.distance = Vector3.Distance(from, to);

        // Measure computation time
        Stopwatch stopwatch = Stopwatch.StartNew();
        
        // Run raycasting algorithm
        VisibilityStatus actualStatus = PerformRaycasting(from, to, out List<string> obstacles);
        
        stopwatch.Stop();
        testResult.computeTime = (float)stopwatch.Elapsed.TotalMilliseconds;
        testResult.actualStatus = actualStatus;
        testResult.obstaclesInPath = obstacles;
        testResult.samplesChecked = numberOfRaySamples;

        // Determine correctness and update confusion matrix
        testResult.isCorrect = (actualStatus == expectedStatus);
        UpdateConfusionMatrix(expectedStatus, actualStatus);

        testResults.Add(testResult);
        LogLOSTestResult(testResult);
    }

    private void RunMultiRoomTests(int numTests)
    {
        Debug.Log($"\n┌─────────────────────────────────────────");
        Debug.Log($"│ Multi-Room Occlusion Tests ({numTests} scenarios)");
        Debug.Log($"└─────────────────────────────────────────");

        for (int i = 0; i < numTests; i++)
        {
            // Test visibility across multiple rooms
            Vector3 room1Pos = GetPositionInRoom("Room1");
            Vector3 room2Pos = GetPositionInRoom("Room2");
            
            // Should be blocked by walls between rooms
            RunCustomLOSTest(100 + i, room1Pos, room2Pos, VisibilityStatus.Blocked);
        }
    }

    private void RunCustomLOSTest(int testId, Vector3 from, Vector3 to, VisibilityStatus expectedStatus)
    {
        var testResult = new LOSTestResult
        {
            testId = testId,
            fromPosition = from,
            toPosition = to,
            expectedStatus = expectedStatus,
            distance = Vector3.Distance(from, to),
            obstaclesInPath = new List<string>()
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        VisibilityStatus actualStatus = PerformRaycasting(from, to, out List<string> obstacles);
        stopwatch.Stop();

        testResult.computeTime = (float)stopwatch.Elapsed.TotalMilliseconds;
        testResult.actualStatus = actualStatus;
        testResult.obstaclesInPath = obstacles;
        testResult.samplesChecked = numberOfRaySamples;
        testResult.isCorrect = (actualStatus == expectedStatus);

        UpdateConfusionMatrix(expectedStatus, actualStatus);
        testResults.Add(testResult);
        LogLOSTestResult(testResult);
    }

    private VisibilityStatus PerformRaycasting(Vector3 from, Vector3 to, out List<string> obstacles)
    {
        obstacles = new List<string>();

        // CRITICAL FIX 1: Add a small vertical offset (e.g., eye level) for wall checks
        Vector3 fromElevated = from + Vector3.up * 1.6f;
        Vector3 toElevated = to + Vector3.up * 1.6f;
        Vector3 direction = (toElevated - fromElevated).normalized;
        float distance = Vector3.Distance(fromElevated, toElevated);

        if (distance < 0.1f) return VisibilityStatus.Clear;

        // CRITICAL FIX 2: Use RaycastAll along the full line to catch all obstacles
        RaycastHit[] hits = Physics.RaycastAll(fromElevated, direction, distance, obstacleLayer);

        if (hits.Length > 0)
        {
            // Sort hits by distance to ensure the closest one is processed first
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                // Only consider hits that are actually between the start and end points
                if (hit.distance < distance)
                {
                    string objName = hit.collider.name;

                    if (!obstacles.Contains(objName))
                    {
                        obstacles.Add(objName);
                    }

                    // Check object type based on tags/layers
                    if (hit.collider.CompareTag("Wall") || hit.collider.CompareTag("Obstacle"))
                    {
                        // Confirmed solid block
                        return VisibilityStatus.Blocked;
                    }
                    else if (hit.collider.CompareTag("Glass") || hit.collider.CompareTag("Door"))
                    {
                        // Partial obstruction (should be handled as Partial in your UI)
                        return VisibilityStatus.Partial;
                    }
                }
            }
        }

        // No solid, permanent obstruction found
        return VisibilityStatus.Clear;
    }
    private void UpdateConfusionMatrix(VisibilityStatus expected, VisibilityStatus actual)
    {
        // For visibility matrix: Visible = TP/FN, Blocked = TN/FP
        if (expected == VisibilityStatus.Clear)
        {
            if (actual == VisibilityStatus.Clear)
                truePositives++; // Correctly identified as visible
            else
                falseNegatives++; // False occlusion (hidden when visible)
        }
        else if (expected == VisibilityStatus.Blocked)
        {
            if (actual == VisibilityStatus.Blocked)
                trueNegatives++; // Correctly identified as blocked
            else
                falsePositives++; // False visible (showed through walls)
        }
        else // Partial
        {
            if (actual == VisibilityStatus.Partial)
                truePositives++;
            else
                falseNegatives++;
        }
    }

    private void CalculateLOSMetrics()
    {
        Debug.Log("\n═══════════════════════════════════════════════════");
        Debug.Log("         LOS RAYCASTING FINAL TEST RESULTS");
        Debug.Log("═══════════════════════════════════════════════════");

        int totalTests = testResults.Count;

        // Confusion Matrix
        Debug.Log($"\n📊 Visibility Confusion Matrix:");
        Debug.Log($"   TP (Correctly visible):       {truePositives}");
        Debug.Log($"   FP (False visible):           {falsePositives}");
        Debug.Log($"   FN (False occlusion):         {falseNegatives}");
        Debug.Log($"   TN (Correctly blocked):       {trueNegatives}");

        // Visibility Accuracy
        float visibilityAccuracy = ((float)(truePositives + trueNegatives) / 
                                   (truePositives + trueNegatives + falsePositives + falseNegatives)) * 100f;
        
        Debug.Log($"\n📈 Visibility Accuracy:");
        Debug.Log($"   Formula: (TP + TN) / (TP + TN + FP + FN) × 100");
        Debug.Log($"   = ({truePositives} + {trueNegatives}) / ({truePositives} + {trueNegatives} + {falsePositives} + {falseNegatives}) × 100");
        Debug.Log($"   = {visibilityAccuracy:F2}%");
        Debug.Log($"   Target: ≥90% {(visibilityAccuracy >= 90f ? "✓ PASS" : "✗ FAIL")}");

        // False Occlusion Rate
        float falseOcclusionRate = ((float)falseNegatives / (truePositives + falseNegatives)) * 100f;
        
        Debug.Log($"\n⚠️  False Occlusion Rate:");
        Debug.Log($"   Formula: FN / (TP + FN) × 100");
        Debug.Log($"   = {falseNegatives} / ({truePositives} + {falseNegatives}) × 100");
        Debug.Log($"   = {falseOcclusionRate:F2}%");
        Debug.Log($"   Target: ≤8% {(falseOcclusionRate <= 8f ? "✓ PASS" : "✗ FAIL")}");

        // Average Compute Time
        float avgComputeTime = testResults.Average(t => t.computeTime);
        
        Debug.Log($"\n⚡ Average Compute Time:");
        Debug.Log($"   = {testResults.Sum(t => t.computeTime):F2} ms / {totalTests} rays");
        Debug.Log($"   = {avgComputeTime:F2} ms per ray");
        Debug.Log($"   Target: ≤30 ms {(avgComputeTime <= 30f ? "✓ PASS" : "✗ FAIL")}");

        // Precision
        float precision = truePositives > 0 ? ((float)truePositives / (truePositives + falsePositives)) * 100f : 0f;
        Debug.Log($"\n🎯 Precision:");
        Debug.Log($"   = TP / (TP + FP) × 100");
        Debug.Log($"   = {truePositives} / ({truePositives} + {falsePositives}) × 100");
        Debug.Log($"   = {precision:F2}%");

        // Distance Analysis
        Debug.Log($"\n📏 Distance Analysis:");
        var distanceGroups = testResults.GroupBy(t => 
            t.distance < 5f ? "Short (<5m)" : 
            t.distance < 10f ? "Medium (5-10m)" : 
            "Long (>10m)");

        foreach (var group in distanceGroups)
        {
            int correct = group.Count(t => t.isCorrect);
            float avgTime = group.Average(t => t.computeTime);
            Debug.Log($"   {group.Key}: {correct}/{group.Count()} correct, {avgTime:F2}ms avg");
        }

        // Occlusion Pattern Analysis
        Debug.Log($"\n🧱 Occlusion Analysis:");
        var blockedTests = testResults.Where(t => t.expectedStatus == VisibilityStatus.Blocked);
        var clearTests = testResults.Where(t => t.expectedStatus == VisibilityStatus.Clear);
        
        Debug.Log($"   Clear LOS tests:     {clearTests.Count(t => t.isCorrect)}/{clearTests.Count()} correct");
        Debug.Log($"   Blocked LOS tests:   {blockedTests.Count(t => t.isCorrect)}/{blockedTests.Count()} correct");

        // Summary
        Debug.Log($"\n═══════════════════════════════════════════════════");
        Debug.Log($"                    SUMMARY");
        Debug.Log($"═══════════════════════════════════════════════════");
        Debug.Log($"Total Tests:          {totalTests}");
        Debug.Log($"Correct Results:      {testResults.Count(t => t.isCorrect)}");
        Debug.Log($"Incorrect Results:    {testResults.Count(t => !t.isCorrect)}");
        Debug.Log($"\nFINAL VERDICT:");
        
        bool allPassed = visibilityAccuracy >= 90f && falseOcclusionRate <= 8f && avgComputeTime <= 30f;
        Debug.Log(allPassed ? "✓✓✓ ALL METRICS PASSED ✓✓✓" : "✗✗✗ SOME METRICS FAILED ✗✗✗");
        Debug.Log($"═══════════════════════════════════════════════════");
    }

    // Helper Methods
    private void GenerateTestPositions(VisibilityStatus status, out Vector3 from, out Vector3 to)
    {
        switch (status)
        {
            case VisibilityStatus.Clear:
                // Generate positions in same room with clear LOS
                from = GetRandomPositionInOpenArea();
                to = from + UnityEngine.Random.insideUnitSphere * 5f;
                to.y = from.y;
                break;

            case VisibilityStatus.Blocked:
                // Generate positions with wall between them
                from = GetRandomPositionInOpenArea();
                to = GetPositionBehindWall(from);
                break;

            case VisibilityStatus.Partial:
                // Generate positions with glass/door between
                from = GetRandomPositionInOpenArea();
                to = GetPositionBehindGlass(from);
                break;

            default:
                from = Vector3.zero;
                to = Vector3.forward * 5f;
                break;
        }
    }

    private Vector3 GetRandomPositionInOpenArea()
    {
        // Return random position in walkable area
        return new Vector3(
            UnityEngine.Random.Range(-10f, 10f),
            1.5f,  // Eye level
            UnityEngine.Random.Range(-10f, 10f)
        );
    }

    private Vector3 GetPositionBehindWall(Vector3 from)
    {
        // Find nearest wall and place target behind it
        Vector3 wallDirection = UnityEngine.Random.insideUnitSphere;
        wallDirection.y = 0;
        return from + wallDirection.normalized * 10f;
    }

    private Vector3 GetPositionBehindGlass(Vector3 from)
    {
        // Similar to wall but through glass partition
        return from + Vector3.forward * 5f;
    }

    private Vector3 GetPositionInRoom(string roomName)
    {
        // Get random position within specified room
        GameObject room = GameObject.Find(roomName);
        if (room != null)
        {
            Bounds bounds = room.GetComponent<Collider>()?.bounds ?? new Bounds(room.transform.position, Vector3.one * 5f);
            return new Vector3(
                UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                1.5f,
                UnityEngine.Random.Range(bounds.min.z, bounds.max.z)
            );
        }
        return Vector3.zero;
    }

    private void LogLOSTestResult(LOSTestResult result)
    {
        string status = result.isCorrect ? "✓" : "✗";
        string obstacleInfo = result.obstaclesInPath.Count > 0 
            ? $"({string.Join(", ", result.obstaclesInPath)})" 
            : "(clear)";
        
        Debug.Log($"{status} Test {result.testId}: " +
                  $"{result.distance:F2}m, " +
                  $"Expected: {result.expectedStatus}, " +
                  $"Got: {result.actualStatus} {obstacleInfo}, " +
                  $"{result.computeTime:F2}ms");
    }

    private void OnDrawGizmos()
    {
        // Visualize ray tests in editor
        if (testResults.Count > 0)
        {
            foreach (var result in testResults.Take(10)) // Show first 10
            {
                Gizmos.color = result.isCorrect ? Color.green : Color.red;
                Gizmos.DrawLine(result.fromPosition, result.toPosition);
            }
        }
    }
}
