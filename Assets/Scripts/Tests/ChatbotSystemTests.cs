using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// SIMPLE ELEMENT-BASED CHATBOT TESTER
/// 
/// This script DOES NOT modify your existing chatbot code.
/// It simply queries your GeminiClient and checks if required/forbidden elements are present.
/// 
/// Safe to add/remove without affecting your working chatbot.
/// </summary>
public class SimpleElementTester : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private GeminiClient geminiClient;
    [SerializeField] private bool runTestsOnStart = false;
    [SerializeField] private bool showDebugLogs = true;

    // Test results
    private int totalTests = 0;
    private int passedTests = 0;
    private int totalTP = 0;
    private int totalFP = 0;
    private int totalFN = 0;
    private int totalTN = 0;

    // Track failed tests
    private List<(string query, List<string> missing, List<string> hallucinated)> failedTests =
        new List<(string query, List<string> missing, List<string> hallucinated)>();

    // Category trackers
    private int officeLocationPassed = 0;
    private int officeLocationTotal = 0;
    private int serviceInfoPassed = 0;
    private int serviceInfoTotal = 0;
    private int navigationPassed = 0;
    private int navigationTotal = 0;
    private int guardrailPassed = 0;
    private int guardrailTotal = 0;

    void Start()
    {
        if (geminiClient == null)
        {
            string apiKey = "AIzaSyA2w9PcimvY1Z3wEikQYyF0O3wSsRBP17Q"; // Your API key
            geminiClient = new GeminiClient(apiKey);
            Log("✓ GeminiClient initialized");
        }

        if (runTestsOnStart)
        {
            RunAllTests();
        }
    }

    [ContextMenu("Run Element Tests")]
    public async void RunAllTests()
    {
        Log("═══════════════════════════════════════════════════");
        Log("    SIMPLE ELEMENT-BASED TEST SUITE");
        Log("═══════════════════════════════════════════════════\n");

        // Reset counters
        totalTests = 0;
        passedTests = 0;
        totalTP = 0;
        totalFP = 0;
        totalFN = 0;
        totalTN = 0;
        failedTests.Clear();

        officeLocationPassed = 0;
        officeLocationTotal = 0;
        serviceInfoPassed = 0;
        serviceInfoTotal = 0;
        navigationPassed = 0;
        navigationTotal = 0;
        guardrailPassed = 0;
        guardrailTotal = 0;

        // Run test categories
        await RunOfficeLocationTests();
        await RunServiceInfoTests();
        await RunNavigationTests();
        await RunGuardrailTests();

        // Display final results
        DisplayResults();
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST CATEGORY 1: OFFICE LOCATION TESTS
    // ═══════════════════════════════════════════════════════════════

    private async Task RunOfficeLocationTests()
    {
        Log("┌─────────────────────────────────────────────────");
        Log("│ Testing Office Location Questions (8 tests)");
        Log("└─────────────────────────────────────────────────\n");

        var tests = new List<ElementTest>
        {
            new ElementTest
            {
                Query = "Where is OSCA?",
                RequiredElements = new[] { "OSCA", "Room 115", "Ground Floor", "8571-3878" },
                ForbiddenElements = new[] { "Second Floor", "Room 200", "5th Floor" },
                Category = "Office Location"
            },
            new ElementTest
            {
                Query = "Saan ang Civil Registry?",
                RequiredElements = new[] { "Civil Registry", "Room 113", "Ground Floor", "5308-9925" },
                ForbiddenElements = new[] { "Third Floor", "Room 300", "Second Floor" },
                Category = "Office Location"
            },
            new ElementTest
            {
                Query = "Where is the Mayor's Office?",
                RequiredElements = new[] { "Mayor", "Room 218", "Second Floor", "8527-4000" },
                ForbiddenElements = new[] { "Ground Floor", "Room 100", "Third Floor" },
                Category = "Office Location"
            },
            new ElementTest
            {
                Query = "Nasaan ang Bureau of Permits?",
                RequiredElements = new[] { "Bureau of Permits", "Room 110", "Ground Floor", "5310-4184" },
                ForbiddenElements = new[] { "Room 200", "Third Floor", "Second Floor" },
                Category = "Office Location"
            },
            new ElementTest
            {
                Query = "Where is PESO?",
                RequiredElements = new[] { "PESO", "Room 108", "Ground Floor", "8404-8174" },
                ForbiddenElements = new[] { "Room 500", "Second Floor", "5th Floor", "Fifth Floor" },
                Category = "Office Location"
            },
            new ElementTest
            {
                Query = "Saan ang City Health Office?",
                RequiredElements = new[] { "City Health", "Room 107", "Ground Floor", "5310-3956" },
                ForbiddenElements = new[] { "Second Floor", "Room 200" },
                Category = "Office Location"
            },
            new ElementTest
            {
                Query = "Where is the City Treasurer?",
                RequiredElements = new[] { "Treasurer", "Room 205", "Second Floor", "5310-3955" },
                ForbiddenElements = new[] { "Ground Floor", "Room 100" },
                Category = "Office Location"
            },
            new ElementTest
            {
                Query = "Nasaan ang Vice Mayor's Office?",
                RequiredElements = new[] { "Vice Mayor", "Room 215", "Second Floor", "8527-3535" },
                ForbiddenElements = new[] { "Ground Floor", "Room 100" },
                Category = "Office Location"
            }
        };

        officeLocationTotal = tests.Count;

        foreach (var test in tests)
        {
            bool passed = await RunSingleTest(test);
            if (passed) officeLocationPassed++;
            await Task.Delay(100); // Avoid rate limiting
        }

        Log("");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST CATEGORY 2: SERVICE INFORMATION TESTS
    // ═══════════════════════════════════════════════════════════════

    private async Task RunServiceInfoTests()
    {
        Log("┌─────────────────────────────────────────────────");
        Log("│ Testing Service Information Questions (5 tests)");
        Log("└─────────────────────────────────────────────────\n");

        var tests = new List<ElementTest>
        {
            new ElementTest
            {
                Query = "What documents do I need for OSCA?",
                RequiredElements = new[] { "senior", "ID", "birth certificate", "proof" },
                ForbiddenElements = new[] { "passport", "visa", "driver" },
                Category = "Service Info"
            },
            new ElementTest
            {
                Query = "How to get a barangay clearance?",
                RequiredElements = new[] { "barangay", "clearance", "valid ID", "cedula" },
                ForbiddenElements = new[] { "passport", "visa" },
                Category = "Service Info"
            },
            new ElementTest
            {
                Query = "Requirements for business permit?",
                RequiredElements = new[] { "business permit", "DTI", "barangay clearance", "cedula" },
                ForbiddenElements = new[] { "birth certificate", "marriage contract" },
                Category = "Service Info"
            },
            new ElementTest
            {
                Query = "Ano ang requirements para sa birth certificate?",
                RequiredElements = new[] { "birth certificate", "valid ID", "Civil Registry" },
                ForbiddenElements = new[] { "passport", "visa" },
                Category = "Service Info"
            },
            new ElementTest
            {
                Query = "How much is the business permit fee?",
                RequiredElements = new[] { "business", "permit", "fee" },
                ForbiddenElements = new[] { "free", "no cost" },
                Category = "Service Info"
            }
        };

        serviceInfoTotal = tests.Count;

        foreach (var test in tests)
        {
            bool passed = await RunSingleTest(test);
            if (passed) serviceInfoPassed++;
            await Task.Delay(100);
        }

        Log("");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST CATEGORY 3: NAVIGATION TESTS
    // ═══════════════════════════════════════════════════════════════

    private async Task RunNavigationTests()
    {
        Log("┌─────────────────────────────────────────────────");
        Log("│ Testing Navigation Questions (4 tests)");
        Log("└─────────────────────────────────────────────────\n");

        var tests = new List<ElementTest>
        {
            new ElementTest
            {
                Query = "How do I get to the second floor?",
                RequiredElements = new[] { "floor", "stairs", "elevator" },
                ForbiddenElements = new[] { "third floor", "basement", "no elevator" },
                Category = "Navigation"
            },
            new ElementTest
            {
                Query = "Is there an elevator?",
                RequiredElements = new[] { "elevator", "available", "yes" },
                ForbiddenElements = new[] { "no elevator", "no lift", "unavailable" },
                Category = "Navigation"
            },
            new ElementTest
            {
                Query = "Paano pumunta sa Civil Registry?",
                RequiredElements = new[] { "Ground Floor", "entrance", "Room 113" },
                ForbiddenElements = new[] { "third floor", "basement", "second floor" },
                Category = "Navigation"
            },
            new ElementTest
            {
                Query = "Where is the main entrance?",
                RequiredElements = new[] { "entrance", "Padre Burgos", "ground" },
                ForbiddenElements = new[] { "second floor", "basement" },
                Category = "Navigation"
            }
        };

        navigationTotal = tests.Count;

        foreach (var test in tests)
        {
            bool passed = await RunSingleTest(test);
            if (passed) navigationPassed++;
            await Task.Delay(100);
        }

        Log("");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST CATEGORY 4: GUARDRAIL TESTS
    // ═══════════════════════════════════════════════════════════════

    private async Task RunGuardrailTests()
    {
        Log("┌─────────────────────────────────────────────────");
        Log("│ Testing Guardrail Questions (3 tests)");
        Log("└─────────────────────────────────────────────────\n");

        var tests = new List<ElementTest>
        {
            new ElementTest
            {
                Query = "Tell me a joke",
                RequiredElements = new[] { "cannot", "City Hall" },
                ForbiddenElements = new[] { "Why did", "knock knock", "funny" },
                Category = "Guardrail"
            },
            new ElementTest
            {
                Query = "What's the weather today?",
                RequiredElements = new[] { "cannot", "City Hall" },
                ForbiddenElements = new[] { "sunny", "rainy", "cloudy", "degrees" },
                Category = "Guardrail"
            },
            new ElementTest
            {
                Query = "Thank you",
                RequiredElements = new[] { "welcome", "else" },
                ForbiddenElements = new[] { "joke", "weather" },
                Category = "Guardrail"
            }
        };

        guardrailTotal = tests.Count;

        foreach (var test in tests)
        {
            bool passed = await RunSingleTest(test);
            if (passed) guardrailPassed++;
            await Task.Delay(100);
        }

        Log("");
    }

    // ═══════════════════════════════════════════════════════════════
    // CORE TEST EXECUTION
    // ═══════════════════════════════════════════════════════════════

    private async Task<bool> RunSingleTest(ElementTest test)
    {
        totalTests++;

        try
        {
            string response = await geminiClient.GetChatResponseAsync(test.Query);

            var result = EvaluateResponse(response, test.RequiredElements, test.ForbiddenElements);

            totalTP += result.TruePositives;
            totalFP += result.FalsePositives;
            totalTN += result.TrueNegatives;
            totalFN += result.FalseNegatives;

            bool passed = result.Passed;
            if (passed) passedTests++;

            float percentage = (result.TruePositives / (float)test.RequiredElements.Length) * 100;
            string status = passed ? "✓" : "✗";

            Log($"{status} [{percentage:F0}%] {test.Query}");

            var missing = new List<string>();
            var hallucinated = new List<string>();

            if (result.FalseNegatives > 0)
            {
                foreach (var req in test.RequiredElements)
                {
                    if (!ContainsKeywordFlexible(response, req))
                    {
                        missing.Add(req);
                    }
                }
                Log($"   Missing ({result.FalseNegatives}): {string.Join(", ", missing)}");
            }

            if (result.FalsePositives > 0)
            {
                foreach (var forbidden in test.ForbiddenElements)
                {
                    if (ContainsKeywordFlexible(response, forbidden))
                    {
                        hallucinated.Add(forbidden);
                    }
                }
                Log($"   Hallucination ({result.FalsePositives}): {string.Join(", ", hallucinated)}");
            }

            if (!passed)
            {
                failedTests.Add((test.Query, missing, hallucinated));
            }

            return passed;
        }
        catch (Exception ex)
        {
            Log($"✗ [ERROR] {test.Query}: {ex.Message}");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // EVALUATION LOGIC
    // ═══════════════════════════════════════════════════════════════

    private ElementTestResult EvaluateResponse(string response, string[] requiredElements, string[] forbiddenElements)
    {
        var result = new ElementTestResult();

        foreach (var req in requiredElements)
        {
            if (ContainsKeywordFlexible(response, req))
                result.TruePositives++;
            else
                result.FalseNegatives++;
        }

        foreach (var forbidden in forbiddenElements)
        {
            if (ContainsKeywordFlexible(response, forbidden))
                result.FalsePositives++;
            else
                result.TrueNegatives++;
        }

        result.Passed = (result.FalsePositives == 0 && result.FalseNegatives == 0);

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // FLEXIBLE KEYWORD MATCHING WITH SYNONYMS
    // ═══════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, string[]> synonyms = new Dictionary<string, string[]>
{
    // --- Office Names ---
    { "osca", new[] { "office of senior citizens affairs", "senior affairs" } },
    { "peso", new[] { "public employment service office" } }, // REMOVED "5th floor" - THIS WAS THE BUG!
    { "civil registry", new[] { "cro", "registry office", "civil registration" } },
    { "mayor", new[] { "mayor's office", "office of the mayor" } },
    { "vice mayor", new[] { "vice mayor's office", "office of the vice mayor" } },
    { "bureau of permits", new[] { "bop", "permits office", "business permits" } },
    { "city health", new[] { "cho", "health office", "city health office" } },
    { "treasurer", new[] { "city treasurer", "treasurer's office", "treasury" } },

    // --- Location/Contact ---
    { "room 115", new[] { "rm 115", "room115", "rm. 115" } },
    { "room 113", new[] { "rm 113", "room113", "rm. 113" } },
    { "room 218", new[] { "rm 218", "room218", "rm. 218" } },
    { "room 215", new[] { "rm 215", "room215", "rm. 215" } },
    { "room 110", new[] { "rm 110", "room110", "rm. 110" } },
    { "room 108", new[] { "rm 108", "room108", "rm. 108" } },
    { "room 107", new[] { "rm 107", "room107", "rm. 107" } },
    { "room 205", new[] { "rm 205", "room205", "rm. 205" } },
    { "ground floor", new[] { "1st floor", "first floor", "ground level" } },
    { "second floor", new[] { "2nd floor" } },

    // --- Requirements & Documents ---
    { "id", new[] { "identification", "government id", "valid id", "government-issued id" } },
    { "valid id", new[] { "government id", "government-issued id", "identification card" } },
    { "cedula", new[] { "community tax certificate", "ctc" } },
    { "cost", new[] { "fee", "payment", "amount" } },
    { "senior", new[] { "senior citizen", "elderly" } },
    { "certificate", new[] { "document", "birth record" } },
    { "barangay", new[] { "village", "community", "barangay office" } },
    { "barangay clearance", new[] { "barangay certificate", "barangay permit" } },

    // --- Navigation Variants ---
    { "stairs", new[] { "staircase", "steps", "stairway" } },
    { "staircase", new[] { "stairs", "steps", "stairway" } },
    { "available", new[] { "accessible", "exists", "present", "there is" } },
    { "entrance", new[] { "entry", "entryway", "door", "main door", "access", "main entrance" } },
    { "ground", new[] { "ground floor", "main floor", "first floor" } },
    { "yes", new[] { "available", "there is", "we have" } },
    { "welcome", new[] { "you're welcome", "walang anuman" } },
    { "else", new[] { "anything else", "more", "pa" } },
};


    private bool ContainsKeywordFlexible(string response, string keyword)
    {
        if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(keyword))
            return false;

        // --- Normalize both sides ---
        string normalizedResponse = response.ToLowerInvariant()
            .Replace("-", "")
            .Replace(" ", "")
            .Replace(".", "")
            .Replace(",", "")
            .Replace("(", "")
            .Replace(")", "");

        string normalizedKeyword = keyword.ToLowerInvariant()
            .Replace("-", "")
            .Replace(" ", "")
            .Replace(".", "")
            .Replace(",", "")
            .Replace("(", "")
            .Replace(")", "");

        // --- Direct check ---
        if (normalizedResponse.Contains(normalizedKeyword))
            return true;

        // --- Synonym expansion ---
        if (synonyms.ContainsKey(keyword.ToLowerInvariant()))
        {
            foreach (var alt in synonyms[keyword.ToLowerInvariant()])
            {
                string normalizedAlt = alt.ToLowerInvariant()
                    .Replace("-", "")
                    .Replace(" ", "")
                    .Replace(".", "")
                    .Replace(",", "")
                    .Replace("(", "")
                    .Replace(")", "");

                if (normalizedResponse.Contains(normalizedAlt))
                    return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // RESULTS DISPLAY
    // ═══════════════════════════════════════════════════════════════

    private void DisplayResults()
    {
        Log("═══════════════════════════════════════════════════");
        Log("         COMPREHENSIVE TEST RESULTS");
        Log("═══════════════════════════════════════════════════\n");

        // --- Summary ---
        Log("📊 Test Summary:");
        Log($"   Total Tests: {totalTests}");
        Log($"   Completed: {totalTests}");
        Log($"   Passed: {passedTests}/{totalTests} ({(passedTests / (float)totalTests * 100):F1}%)\n");

        // --- Confusion Matrix ---
        Log("📊 Confusion Matrix:");
        Log($"   True Positives (TP):   {totalTP} (correct elements)");
        Log($"   False Positives (FP):  {totalFP} (hallucinations)");
        Log($"   True Negatives (TN):   {totalTN} (correctly absent)");
        Log($"   False Negatives (FN):  {totalFN} (missing elements)\n");

        // --- Metric calculations ---
        float precision = totalTP / (float)(Math.Max(1, totalTP + totalFP));
        float recall = totalTP / (float)(Math.Max(1, totalTP + totalFN));
        float f1Score = (precision + recall > 0)
            ? 2 * (precision * recall) / (precision + recall)
            : 0;
        float accuracy = (totalTP + totalTN) / (float)Math.Max(1, (totalTP + totalFP + totalTN + totalFN));
        float passRate = (passedTests / (float)Math.Max(1, totalTests)) * 100f;
        float hallucinationRate = (totalFP / (float)Math.Max(1, totalTests)) * 100f;

        // --- Core Metrics ---
        Log("📈 Metrics:");
        Log($"   Precision:         {precision * 100:F1}% (accuracy of provided info)");
        Log($"   Recall:            {recall * 100:F1}% (completeness of info)");
        Log($"   F1-Score:          {f1Score * 100:F1}% (overall quality)");
        Log($"   Accuracy:          {accuracy * 100:F1}% (all predictions)");
        Log($"   Pass Rate:         {passRate:F1}% ({passedTests}/{totalTests} tests)");
        Log($"   Hallucination:     {hallucinationRate:F1}%\n");

        // --- Category Breakdown ---
        Log("📋 By Category:");
        Log($"   Office Location: {officeLocationPassed}/{officeLocationTotal} ({(officeLocationPassed / (float)Math.Max(1, officeLocationTotal) * 100):F0}%)");
        Log($"   Service Info: {serviceInfoPassed}/{serviceInfoTotal} ({(serviceInfoPassed / (float)Math.Max(1, serviceInfoTotal) * 100):F0}%)");
        Log($"   Navigation: {navigationPassed}/{navigationTotal} ({(navigationPassed / (float)Math.Max(1, navigationTotal) * 100):F0}%)");
        Log($"   Guardrail: {guardrailPassed}/{guardrailTotal} ({(guardrailPassed / (float)Math.Max(1, guardrailTotal) * 100):F0}%)\n");

        // --- SMART OVERALL STATUS ---
        string overallStatus;
        if (passRate < 50)
            overallStatus = "✗ CRITICAL";
        else if (passRate < 75)
            overallStatus = "⚠️  NEEDS IMPROVEMENT";
        else if (passRate < 90)
            overallStatus = "✅ GOOD";
        else
            overallStatus = "🌟 EXCELLENT";

        Log($"Overall Status: {overallStatus}\n");

        // --- Context-Aware Hints ---
        if (recall < 0.8f)
            Log("⚠️  LOW RECALL: Bot is missing important information.");

        if (totalFP > 0)
            Log($"⚠️  HALLUCINATIONS DETECTED: {totalFP} cases of incorrect information!");

        if (passRate >= 90f && f1Score > 0.9f)
            Log("✅  Excellent! Bot responses are accurate and complete.");

        Log("═══════════════════════════════════════════════════");

        // --- Failed Tests Details ---
        if (failedTests.Count > 0)
        {
            Log("\n🔍 Failed Tests Details:\n");
            foreach (var (query, missing, hallucinated) in failedTests)
            {
                Log($"  Query: {query}");
                if (missing.Count > 0)
                    Log($"  Missing: {string.Join(", ", missing)}");
                if (hallucinated.Count > 0)
                    Log($"  Hallucinated: {string.Join(", ", hallucinated)}");
                Log("");
            }
        }
    }

    private void Log(string message)
    {
        Debug.Log(message);
    }
}

// ═══════════════════════════════════════════════════════════════════
// DATA STRUCTURES
// ═══════════════════════════════════════════════════════════════════

public class ElementTest
{
    public string Query { get; set; }
    public string[] RequiredElements { get; set; }
    public string[] ForbiddenElements { get; set; }
    public string Category { get; set; }
}

public class ElementTestResult
{
    public int TruePositives { get; set; }   // Required elements found
    public int FalsePositives { get; set; }  // Forbidden elements present (hallucinations)
    public int TrueNegatives { get; set; }   // Forbidden elements absent (correct)
    public int FalseNegatives { get; set; }  // Required elements missing
    public bool Passed { get; set; }   // Indicates if the test meets threshold

    public float Precision => TruePositives / (float)Math.Max(1, TruePositives + FalsePositives);
    public float Recall => TruePositives / (float)Math.Max(1, TruePositives + FalseNegatives);
    public float F1Score => (Precision + Recall > 0) ? 2 * (Precision * Recall) / (Precision + Recall) : 0;
    public float Accuracy => (TruePositives + TrueNegatives) /
                             (float)Math.Max(1, (TruePositives + FalsePositives + TrueNegatives + FalseNegatives));
}