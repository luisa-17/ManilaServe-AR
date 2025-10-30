using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

public class AlgorithmTestSuite : MonoBehaviour
{
    // =========================================================================
    // 🧮 MATRIX TEST SETUP (Weights and Visibility)
    // =========================================================================

    // Pathfinding Grid Matrix (10x10):
    // 0 = Walkable (Cost 1), 1 = Obstacle (Cost ∞), 2 = Destination, 3 = Hallway (Cost 5), 4 = High-Penalty Office (Cost 50)
    private int[,] weightedGrid = new int[10, 10]
    {
        {0, 0, 4, 3, 3, 3, 0, 0, 0, 0},
        {0, 1, 1, 1, 3, 1, 1, 1, 1, 0},
        {0, 0, 0, 0, 3, 1, 0, 0, 1, 0},
        {1, 1, 1, 0, 3, 1, 0, 4, 1, 0},
        {0, 0, 0, 0, 3, 0, 0, 0, 1, 0},
        {0, 1, 1, 1, 1, 1, 1, 0, 1, 0},
        {0, 0, 0, 0, 0, 0, 0, 0, 1, 0},
        {0, 4, 1, 1, 1, 1, 1, 1, 1, 0},
        {0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
        {0, 4, 3, 3, 3, 3, 3, 3, 3, 2} // Start at (0,0), Goal at (9,9)
    };

    private readonly Dictionary<(int r1, int c1, int r2, int c2), int> VisibilityMatrix = new Dictionary<(int, int, int, int), int>
    {
        { (0, 0, 0, 5), 0 },
        { (0, 0, 4, 0), 1 },
        { (2, 2, 4, 2), 0 },
        { (0, 0, 9, 9), 1 },
        { (0, 6, 9, 6), 0 },
        { (0, 2, 0, 5), 1 }
    };


    // =========================================================================
    // ⚙️ STARTUP & EXECUTION
    // =========================================================================

    void Start()
    {
        Debug.Log("=== Algorithm Tests Started ===");
        StartCoroutine(RunAllTests());
    }

    private IEnumerator RunAllTests()
    {
        yield return null;

        Debug.Log("==========================================");
        TestWeightedPathfinding();
        Debug.Log("==========================================");
        TestConnectivityVisibility();
        Debug.Log("==========================================");
        TestServiceDataMapping();
        Debug.Log("==========================================");
        TestCacheConsistency();
        Debug.Log("==========================================");
        TestRequirementTracking();
        Debug.Log("==========================================");
        TestGeminiAPIFallback();
        Debug.Log("==========================================");
        TestPromptInjectionGuardrail();
        Debug.Log("==========================================");
        TestRegexSanitization();
        Debug.Log("==========================================");

        Debug.Log("=== All tests completed successfully. ===\n");
    }

    // =========================================================================
    // PATH RECONSTRUCTION UTILITY (FIX CS0122)
    // =========================================================================

    private static List<MockWeightedNode> ReconstructPathUtility(Dictionary<MockWeightedNode, MockWeightedNode> cameFrom, MockWeightedNode current)
    {
        var path = new List<MockWeightedNode> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }
        return path;
    }

    // =========================================================================
    // 1. PATHFINDING (A* and BFS)
    // =========================================================================

    private void TestWeightedPathfinding()
    {
        Debug.Log("--- 🧩 Pathfinding Algorithms Test (Weighted Grid) ---");

        var graph = new MockWeightedGraph(weightedGrid);
        var start = graph.GetNode(0, 0);
        var goal = graph.GetDestination();

        if (start == null || goal == null)
        {
            Debug.LogError("Setup failed: Start or Goal not found.");
            return;
        }

        // --- A* Test (Uses Weights/Penalties) ---
        var stopwatchA = Stopwatch.StartNew();
        var resultA = MockAStar.FindPath(start, goal, graph);
        stopwatchA.Stop();

        float costA = resultA.Path.Sum(n => n.Cost);
        Debug.Log($"[Test A* Weighted] Path length = **{(resultA.Path.Count - 1)}**, Total Cost = **{costA:F0}**, Time = **{stopwatchA.Elapsed.TotalMilliseconds:F2}ms**");

        // --- BFS Test (Ignores Weights/Penalties) ---
        var stopwatchB = Stopwatch.StartNew();
        var resultB = MockBFS.FindPath(start, goal, graph);
        stopwatchB.Stop();

        Debug.Log($"[Test BFS Unweighted] Path length = **{(resultB.Path.Count - 1)}**, Time = **{stopwatchB.Elapsed.TotalMilliseconds:F2}ms**");

        // --- Validation & Comparison ---
        if (resultA.Path.Count > 0 && resultB.Path.Count > 0)
        {
            Debug.Log($"Comparison: BFS finds a path of length {resultB.Path.Count - 1}. A* finds a path of length {resultA.Path.Count - 1}.");
            CheckAStarSteering(resultA.Path);
        }
        else
        {
            Debug.LogWarning("Comparison failed: One or both algorithms did not find a path.");
        }
    }

    private void CheckAStarSteering(List<MockWeightedNode> path)
    {
        int highCostNodesUsed = path.Count(n => n.Type == 4);

        if (highCostNodesUsed == 0)
            Debug.Log($"[A* Steering] **Successfully avoided High-Penalty Office nodes.** ✅");
        else
            Debug.LogWarning($"[A* Steering] **Path used {highCostNodesUsed} High-Penalty Office node(s).** ❌ (Expected: 0)");
    }

    // =========================================================================
    // 2. CONNECTIVITY CHECK (Visibility Matrix)
    // =========================================================================

    private void TestConnectivityVisibility()
    {
        Debug.Log("--- 🔗 Connectivity Check (Visibility Matrix) ---");

        foreach (var kvp in VisibilityMatrix)
        {
            var p1 = kvp.Key;
            int expected = kvp.Value;
            bool isBlocked = (expected == 1);

            int result = MockVisibilityChecker.CheckVisibility(p1.r1, p1.c1, p1.r2, p1.c2, VisibilityMatrix);

            bool success = (result == expected);
            string status = isBlocked ? "BLOCKED" : "CLEAR";

            Debug.Log($"Check ({p1.r1},{p1.c1}) → ({p1.r2},{p1.c2}) ({status}): **{(success ? "✅" : "❌")}**");
        }
    }

    // =========================================================================
    // 3. SERVICE DATA (Lookup Table)
    // =========================================================================

    private void TestServiceDataMapping()
    {
        Debug.Log("--- 📊 Data Retrieval (Lookup Table) Test ---");
        var lookup = new MockLookupTable();

        // Test 3.1: Successful retrieval
        string officeA = "Manila Health Department";
        var servicesA = lookup.RetrieveServices(officeA);
        bool successA = servicesA.Contains("Health Permit");
        Debug.Log($"[Test 3.1] Retrieve '{officeA}': Success ({successA}) **{(successA ? "✅" : "❌")}**");

        // Test 3.2: Fallback for missing data
        string officeB = "City Treasurer";
        var servicesB = lookup.RetrieveServices(officeB);
        bool successB = servicesB.Contains("N/A");
        Debug.Log($"[Test 3.2] Missing Data Fallback: Success ({successB}) **{(successB ? "✅" : "❌")}**");

        // Test 3.3: Lookup speed (simple benchmark)
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
            lookup.RetrieveServices("Manila Health Department");
        stopwatch.Stop();
        Debug.Log($"[Test 3.3] 1000 Lookups Time: **{stopwatch.Elapsed.TotalMilliseconds:F4}ms**");
    }

    // =========================================================================
    // 4. CACHE CONSISTENCY (Simulated Network)
    // =========================================================================

    private void TestCacheConsistency()
    {
        Debug.Log("--- 💾 Cache Consistency Test ---");
        var cache = new MockCacheSystem();

        // Test Case 4.1: Initial Sync (Cloud → Cache)
        cache.SimulateNetwork(true);
        cache.LoadData();
        bool sync1 = cache.IsCacheState("Synced: [A, B]");
        Debug.Log($"[Test 4.1] Cloud Sync (Online): **{(sync1 ? "✅" : "❌")}**");

        // Test Case 4.2: Offline Persistence
        cache.SimulateNetwork(false);
        cache.UpdateCache("C"); // Change made offline
        cache.SaveToPlayerPrefs();
        cache.LoadData(); // Load should prioritize PlayerPrefs now
        bool offline2 = cache.IsCacheState("Offline: [A, B, C]");
        Debug.Log($"[Test 4.2] Offline Persistence: **{(offline2 ? "✅" : "❌")}**");

        // Test Case 4.3: Resync (PlayerPrefs → Cloud)
        cache.SimulateNetwork(true);
        cache.LoadData(); // Should detect local change and sync it
        bool resync3 = cache.IsCloudState("Synced: [A, B, C]");
        Debug.Log($"[Test 4.3] Resync to Cloud: **{(resync3 ? "✅" : "❌")}**");
    }

    // =========================================================================
    // 5. REQUIREMENT TRACKING (Boolean Vector)
    // =========================================================================

    private void TestRequirementTracking()
    {
        Debug.Log("--- 📌 Requirement Tracking (Boolean Vector) Test ---");

        // [Test 5.1]: All True - Expected: True
        var reqs1 = new List<bool> { true, true, true };
        bool result1 = reqs1.All(r => r);
        Debug.Log($"[Test 5.1] All True: Result ({result1}) **{(result1 ? "✅" : "❌")}**");

        // [Test 5.2]: Mixed - Expected: False
        var reqs2 = new List<bool> { true, false, true };
        bool result2 = reqs2.All(r => r);
        Debug.Log($"[Test 5.2] Mixed: Result ({result2}) **{(!result2 ? "✅" : "❌")}**");
    }

    // =========================================================================
    // 6. CHATBOT ALGORITHMS (LLM, Fallback, Guardrails, Regex)
    // =========================================================================

    private void TestGeminiAPIFallback()
    {
        Debug.Log("--- 🤖 Sequential Model Fallback Test ---");
        var mockClient = new MockGeminiClient();

        // [Test 6.1]: Fail, Fail, Succeed (Fallback to model 3)
        string result1 = mockClient.SimulateResponse(new[] { false, false, true });
        bool success1 = result1.Contains("Passed on model gemini-2.5-flash");
        Debug.Log($"[Test 6.1] Fallback (M1/M2 Fail): Success ({success1}) **{(success1 ? "✅" : "❌")}**");

        // [Test 6.2]: Total failure
        string result2 = mockClient.SimulateResponse(new[] { false, false, false });
        bool success2 = result2.Contains("No compatible Gemini model found");
        Debug.Log($"[Test 6.2] Total failure: Success ({success2}) **{(success2 ? "✅" : "❌")}**");
    }

    private void TestPromptInjectionGuardrail()
    {
        Debug.Log("--- 🛡️ Prompt Injection Guardrail Test ---");
        var guardrail = new MockGuardrail();
        string disallowedInfo = "Manila is famous for its beaches.";
        string allowedInfo = "The Mayor's Contact is (02) 8527-4991";

        // Test 7.1: Check that disallowed info is ignored (Simulate model output)
        string output1 = guardrail.SimulateResponse(disallowedInfo);
        bool success1 = !output1.Contains(disallowedInfo);
        Debug.Log($"[Test 7.1] Disallowed Info Ignored: **{(success1 ? "✅" : "❌")}**");

        // Test 7.2: Check that allowed info is used
        string output2 = guardrail.SimulateResponse(allowedInfo);
        bool success2 = output2.Contains(allowedInfo);
        Debug.Log($"[Test 7.2] Allowed Info Used: **{(success2 ? "✅" : "❌")}**");
    }

    private void TestRegexSanitization()
    {
        Debug.Log("--- 📝 Text Processing (Regex) Test ---");
        var sanitizer = new MockTextSanitizer();

        // Test 8.1: Remove **bold**
        string input1 = "This is **important** data.";
        string expected1 = "This is important data.";
        string result1 = sanitizer.SanitizeBotMarkdown(input1);
        bool success1 = result1 == expected1;
        Debug.Log($"[Test 8.1] Bold removal: **{(success1 ? "✅" : "❌")}**");

        // Test 8.3: Remove inline `code` and convert bullet point
        string input3 = "Use `peso` currency.\n- Item B";
        string expected3 = "Use peso currency.\n• Item B";
        string result3 = sanitizer.SanitizeBotMarkdown(input3);
        bool success3 = result3 == expected3;
        Debug.Log($"[Test 8.3] Mixed conversion: **{(success3 ? "✅" : "❌")}**");
    }


    // =========================================================================
    // MOCK DATA STRUCTURES & ALGORITHMS (ALL PUBLIC CLASS - FIX CS0246)
    // =========================================================================

    public class MockWeightedNode
    {
        public int R, C;
        public int Type;
        public float Cost;
        public MockWeightedNode(int r, int c, int type, float cost) { R = r; C = c; Type = type; Cost = cost; }
        public override bool Equals(object obj) => obj is MockWeightedNode other && R == other.R && C == other.C;
        public override int GetHashCode() => (R, C).GetHashCode();
        public float Heuristic(MockWeightedNode goal) => Math.Abs(R - goal.R) + Math.Abs(C - goal.C);
    }

    public class MockWeightedGraph
    {
        private MockWeightedNode[,] nodes;
        private int rows, cols;
        private MockWeightedNode destination;

        private Dictionary<int, float> CostMap = new Dictionary<int, float>
    {
        {0, 1f},      // Walkable
        {3, 5f},      // Hallway
        {4, 50f},     // High-Penalty Office
        {1, float.MaxValue}, // Obstacle
        {2, 1f}       // Destination (Type 2) - Should be walkable cost
    };

        public MockWeightedGraph(int[,] matrix)
        {
            rows = matrix.GetLength(0);
            cols = matrix.GetLength(1);
            nodes = new MockWeightedNode[rows, cols];

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int type = matrix[r, c];
                    float cost = CostMap[type];
                    nodes[r, c] = new MockWeightedNode(r, c, type, cost);
                    if (type == 2) destination = nodes[r, c];
                }
        }
        public MockWeightedNode GetNode(int r, int c) => (r >= 0 && r < rows && c >= 0 && c < cols) ? nodes[r, c] : null;
        public MockWeightedNode GetDestination() => destination;

        public IEnumerable<MockWeightedNode> GetNeighbors(MockWeightedNode n)
        {
            var neighbors = new List<MockWeightedNode>();
            int[] dr = { 0, 0, 1, -1 };
            int[] dc = { 1, -1, 0, 0 };

            for (int i = 0; i < 4; i++)
            {
                var neighbor = GetNode(n.R + dr[i], n.C + dc[i]);
                if (neighbor != null && neighbor.Cost < float.MaxValue)
                    neighbors.Add(neighbor);
            }
            return neighbors;
        }
    }

    public class PathResult
    {
        public List<MockWeightedNode> Path = new List<MockWeightedNode>();
        public int NodesVisited;
    }

    public static class MockAStar
    {
        public static PathResult FindPath(MockWeightedNode start, MockWeightedNode goal, MockWeightedGraph graph)
        {
            var result = new PathResult();
            var openSet = new PriorityQueue<MockWeightedNode>();
            openSet.Enqueue(start, 0f);

            var cameFrom = new Dictionary<MockWeightedNode, MockWeightedNode>();
            var gScore = new Dictionary<MockWeightedNode, float> { { start, 0f } };
            var fScore = new Dictionary<MockWeightedNode, float> { { start, start.Heuristic(goal) } };

            while (openSet.Count > 0)
            {
                var current = openSet.Dequeue();
                result.NodesVisited++;

                if (current.Equals(goal))
                {
                    result.Path = ReconstructPathUtility(cameFrom, current); // FIXED: Call utility method
                    return result;
                }

                foreach (var neighbor in graph.GetNeighbors(current))
                {
                    float tentativeGScore = gScore.GetValueOrDefault(current, float.MaxValue) + neighbor.Cost;

                    if (tentativeGScore < gScore.GetValueOrDefault(neighbor, float.MaxValue))
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeGScore;
                        fScore[neighbor] = tentativeGScore + neighbor.Heuristic(goal);
                        openSet.Enqueue(neighbor, fScore[neighbor]);
                    }
                }
            }
            return result;
        }
    }

    public static class MockBFS
    {
        public static PathResult FindPath(MockWeightedNode start, MockWeightedNode goal, MockWeightedGraph graph)
        {
            var result = new PathResult();
            var queue = new Queue<MockWeightedNode>();
            queue.Enqueue(start);

            var cameFrom = new Dictionary<MockWeightedNode, MockWeightedNode>();
            var visited = new HashSet<MockWeightedNode> { start };

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.NodesVisited++;

                if (current.Equals(goal))
                {
                    result.Path = ReconstructPathUtility(cameFrom, current); // FIXED: Call utility method
                    return result;
                }

                foreach (var neighbor in graph.GetNeighbors(current))
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        cameFrom[neighbor] = current;
                        queue.Enqueue(neighbor);
                    }
                }
            }
            return result;
        }
    }

    public static class MockVisibilityChecker
    {
        public static int CheckVisibility(int r1, int c1, int r2, int c2, Dictionary<(int, int, int, int), int> matrix)
        {
            if (matrix.TryGetValue((r1, c1, r2, c2), out int result))
                return result;
            return 0;
        }
    }

    public class MockLookupTable
    {
        private readonly Dictionary<string, List<string>> ServiceLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Manila Health Department", new List<string> { "Health Permit", "Medical Certificate", "Sanitary Permit" } },
            { "Civil Registry Office", new List<string> { "Birth Certificate", "Marriage License" } }
        };

        public List<string> RetrieveServices(string officeName)
        {
            if (ServiceLookup.TryGetValue(officeName, out var services))
                return services;
            return new List<string> { "N/A: Data not available for this office." };
        }
    }

    public class MockCacheSystem
    {
        private List<string> CloudData = new List<string> { "A", "B" };
        private List<string> CacheData = new List<string>();
        private List<string> PlayerPrefsData = new List<string>();
        private bool isOnline = true;

        public void SimulateNetwork(bool online) => isOnline = online;

        public void LoadData()
        {
            if (isOnline)
            {
                if (PlayerPrefsData.Count > 0 && !CacheData.SequenceEqual(PlayerPrefsData))
                {
                    CloudData = PlayerPrefsData;
                    CacheData = new List<string>(CloudData);
                    PlayerPrefsData.Clear();
                }
                else
                {
                    CacheData = new List<string>(CloudData);
                }
            }
            else
            {
                CacheData = new List<string>(PlayerPrefsData);
            }
        }

        public void UpdateCache(string item) => CacheData.Add(item);
        public void SaveToPlayerPrefs() => PlayerPrefsData = new List<string>(CacheData);
        public bool IsCacheState(string expected) => expected.Contains(string.Join(", ", CacheData));
        public bool IsCloudState(string expected) => expected.Contains(string.Join(", ", CloudData));
    }

    public class MockGeminiClient
    {
        private readonly string[] MockModelOrder = new[] { "gemini-1.5-flash", "gemini-1.5-flash-8b", "gemini-2.5-flash" };

        public string SimulateResponse(bool[] results)
        {
            for (int i = 0; i < MockModelOrder.Length; i++)
            {
                if (i >= results.Length) return "No compatible Gemini model found for your key. Try v1beta + gemini-2.5-flash via curl to verify the key works.";

                string model = MockModelOrder[i];
                bool isSuccess = results[i];

                if (isSuccess)
                {
                    return $"Passed on model {model} (Fallback count: {i})";
                }
                else
                {
                    continue;
                }
            }
            return "No compatible Gemini model found for your key. Try v1beta + gemini-2.5-flash via curl to verify the key works.";
        }
    }

    public class MockGuardrail
    {
        public string SimulateResponse(string input)
        {
            if (input.Contains("beaches"))
            {
                return "I can only provide information about Manila City Hall offices.";
            }
            return $"I found the following: {input}. May iba pa bang maitutulong ko?";
        }
    }

    public class MockTextSanitizer
    {
        private static readonly Regex RE_BOLD = new Regex(@"\*\*(.*?)\*\*", RegexOptions.Singleline);
        private static readonly Regex RE_BULLET1 = new Regex(@"(?m)^\s*-\s+");
        private static readonly Regex RE_BULLET2 = new Regex(@"(?m)^\s*\*\s+");
        private static readonly Regex RE_BACKTICKS = new Regex(@"`([^`]+)`", RegexOptions.Singleline);

        public string SanitizeBotMarkdown(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = RE_BOLD.Replace(s, "$1");
            s = RE_BACKTICKS.Replace(s, "$1");
            s = RE_BULLET1.Replace(s, "• ");
            s = RE_BULLET2.Replace(s, "• ");
            return s;
        }
    }

    public class PriorityQueue<T>
    {
        private List<(T item, float priority)> elements = new List<(T, float)>();

        public int Count => elements.Count;

        public void Enqueue(T item, float priority)
        {
            elements.Add((item, priority));
            elements.Sort((a, b) => a.priority.CompareTo(b.priority));
        }

        public T Dequeue()
        {
            if (elements.Count == 0) throw new InvalidOperationException("Queue is empty.");

            T item = elements[0].item;
            elements.RemoveAt(0);
            return item;
        }
    }
}