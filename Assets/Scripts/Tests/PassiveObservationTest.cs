using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using System.Text.RegularExpressions;

/// <summary>
/// COMPREHENSIVE CHATBOT TESTING SCRIPT
/// 
/// Tests 20 questions across multiple categories to get realistic metrics.
/// Includes strict element checking and detailed reporting.
/// </summary>
public class ComprehensiveChatbotTest : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════
    // DATA STRUCTURES (MOVED INSIDE CLASS TO AVOID CS0101 ERROR)
    // ═══════════════════════════════════════════════════════════════════

    public class ElementTest
    {
        public string Query { get; set; }
        public string[] RequiredElements { get; set; }
        public string[] ForbiddenElements { get; set; }
    }

    public class TestResult
    {
        public string Query { get; set; }
        public string Response { get; set; }
        public string Category { get; set; }

        public int TP { get; set; }
        public int FP { get; set; }
        public int TN { get; set; }
        public int FN { get; set; }

        public float Precision { get; set; }
        public float Recall { get; set; }
        public float F1Score { get; set; }

        public List<string> MissingElements { get; set; } = new List<string>();
        public List<string> HallucinatedElements { get; set; } = new List<string>();
    }

    // ═══════════════════════════════════════════════════════════════════
    // MAIN CLASS MEMBERS START HERE
    // ═══════════════════════════════════════════════════════════════════

    [Header("Configuration")]
    [SerializeField] private GeminiClient geminiClient;
    [SerializeField] private bool runTestsOnStart = false;
    [SerializeField] private int delayBetweenRequests = 500; // 5 seconds

    // Tracking
    private int totalTests = 0;
    private int passedTests = 0;
    private int totalTP = 0;
    private int totalFP = 0;
    private int totalFN = 0;
    private int totalTN = 0;

    private List<TestResult> allResults = new List<TestResult>();

    void Start()
    {
        if (geminiClient == null)
        {
            string apiKey = "AIzaSyA2w9PcimvY1Z3wEikQYyF0O3wSsRBP17Q";

            try
            {
                geminiClient = new GeminiClient(apiKey);
                Debug.Log("✓ GeminiClient successfully initialized for testing");
            }
            catch (Exception ex)
            {
                Debug.LogError("❌ Failed to initialize GeminiClient: " + ex.Message);
                return; // stop tests if initialization fails
            }
        }


        if (runTestsOnStart)
        {
            RunComprehensiveTests();
        }
    }

    [ContextMenu("Run Comprehensive Tests (20 Questions)")]
    public async void RunComprehensiveTests()
    {
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("    COMPREHENSIVE CHATBOT TEST SUITE");
        Debug.Log("    Testing 20 questions with strict validation");
        Debug.Log("═══════════════════════════════════════════════════\n");

        // Reset
        totalTests = 0;
        passedTests = 0;
        totalTP = 0;
        totalFP = 0;
        totalFN = 0;
        totalTN = 0;
        allResults.Clear();

        // Run all test categories
        await TestOfficeLocations();
        await TestServiceInformation();
        await TestNavigationQuestions();
        await TestGuardrailQuestions();

        // Display final results
        DisplayFinalResults();
    }

    // ═══════════════════════════════════════════════════════════════
    // OFFICE LOCATION TESTS (8 questions)
    // ═══════════════════════════════════════════════════════════════

    private async Task TestOfficeLocations()
    {
        Debug.Log("┌─────────────────────────────────────────────────");
        Debug.Log("│ Testing Office Location Questions (8 tests)");
        Debug.Log("└─────────────────────────────────────────────────\n");

        var tests = new List<ElementTest>
        {
            new ElementTest
            {
                Query = "Where is OSCA?",
                RequiredElements = new[] { "OSCA", "Room 115", "Ground Floor", "8571-3878" },
                ForbiddenElements = new[] { "Second Floor", "Room 200", "9999-9999" }
            },
            new ElementTest
            {
                Query = "Saan ang Civil Registry?",
                RequiredElements = new[] { "Civil Registry", "Room 113", "Ground Floor", "5308-9925" },
                ForbiddenElements = new[] { "Third Floor", "Room 300", "8888-8888" }
            },
            new ElementTest
            {
                Query = "Where is the Mayor's Office?",
                RequiredElements = new[] { "Mayor", "Room 218", "Second Floor", "8527-4000" },
                ForbiddenElements = new[] { "Ground Floor", "Room 216", "Room 100" }
            },
            new ElementTest
            {
                Query = "Nasaan ang Bureau of Permits?",
                RequiredElements = new[] { "Bureau of Permits", "Room 110", "Ground Floor", "5310-4184" },
                ForbiddenElements = new[] { "Room 200", "Third Floor" }
            },
            new ElementTest
            {
                Query = "Where is PESO?",
                RequiredElements = new[] { "PESO", "Room 108", "Ground Floor", "8404-8174" },
                ForbiddenElements = new[] { "5th Floor", "Room 500", "Second Floor" }
            },
            new ElementTest
            {
                Query = "Saan ang City Health Office?",
                RequiredElements = new[] { "Health", "Room 101", "Ground Floor", "8527-4960" },
                ForbiddenElements = new[] { "Room 200", "Second Floor" }
            },
            new ElementTest
            {
                Query = "Where is the City Treasurer?",
                RequiredElements = new[] { "Treasurer", "Room 152", "8527-5020" },
                ForbiddenElements = new[] { "Ground Floor", "Room 100" }
            },
            new ElementTest
            {
                Query = "Nasaan ang Vice Mayor's Office?",
                RequiredElements = new[] { "Vice Mayor", "Room 215", "Second Floor", "8527-4000" },
                ForbiddenElements = new[] { "Ground Floor", "Room 100" }
            }
        };

        foreach (var test in tests)
        {
            await RunSingleTest(test, "Office Location");
            await Task.Delay(delayBetweenRequests);
        }

        Debug.Log("");
    }

    // ═══════════════════════════════════════════════════════════════
    // SERVICE INFORMATION TESTS (5 questions)
    // ═══════════════════════════════════════════════════════════════

    private async Task TestServiceInformation()
    {
        Debug.Log("┌─────────────────────────────────────────────────");
        Debug.Log("│ Testing Service Information Questions (5 tests)");
        Debug.Log("└─────────────────────────────────────────────────\n");

        var tests = new List<ElementTest>
        {
            new ElementTest
            {
                Query = "What documents do I need for OSCA?",
                RequiredElements = new[] { "senior", "ID", "birth certificate", "proof" },
                ForbiddenElements = new[] { "passport", "visa", "driver license" }
            },
            new ElementTest
            {
                Query = "How to get a barangay clearance?",
                RequiredElements = new[] { "barangay", "clearance", "valid ID", "cedula" },
                ForbiddenElements = new[] { "passport", "visa" }
            },
            new ElementTest
            {
                Query = "Requirements for business permit?",
                RequiredElements = new[] { "business permit", "DTI", "barangay clearance", "cedula" },
                ForbiddenElements = new[] { "birth certificate", "marriage contract" }
            },
            new ElementTest
            {
                Query = "Ano ang requirements para sa birth certificate?",
                RequiredElements = new[] { "birth", "certificate", "valid ID", "cedula" },
                ForbiddenElements = new[] { "passport", "business permit" }
            },
            new ElementTest
            {
                Query = "How much is the business permit fee?",
                RequiredElements = new[] { "permit", "fee", "cost", "payment" },
                ForbiddenElements = new[] { "free", "no charge" }
            }
        };

        foreach (var test in tests)
        {
            await RunSingleTest(test, "Service Info");
            await Task.Delay(delayBetweenRequests);
        }

        Debug.Log("");
    }

    // ═══════════════════════════════════════════════════════════════
    // NAVIGATION TESTS (4 questions)
    // ═══════════════════════════════════════════════════════════════

    private async Task TestNavigationQuestions()
    {
        Debug.Log("┌─────────────────────────────────────────────────");
        Debug.Log("│ Testing Navigation Questions (4 tests)");
        Debug.Log("└─────────────────────────────────────────────────\n");

        var tests = new List<ElementTest>
        {
            new ElementTest
            {
                Query = "How do I get to the second floor?",
                RequiredElements = new[] { "floor", "stairs", "elevator", "staircase" },
                ForbiddenElements = new[] { "third floor", "basement", "roof" }
            },
            new ElementTest
            {
                Query = "Is there an elevator?",
                RequiredElements = new[] { "elevator", "yes", "available" },
                ForbiddenElements = new[] { "no elevator", "no lift", "none" }
            },
            new ElementTest
            {
                Query = "Paano pumunta sa Civil Registry?",
                RequiredElements = new[] { "Ground Floor", "entrance", "Room 113" },
                ForbiddenElements = new[] { "third floor", "basement" }
            },
            new ElementTest
            {
                Query = "Where is the main entrance?",
                RequiredElements = new[] { "entrance", "main", "ground" },
                ForbiddenElements = new[] { "second floor", "basement" }
            }
        };

        foreach (var test in tests)
        {
            await RunSingleTest(test, "Navigation");
            await Task.Delay(delayBetweenRequests);
        }

        Debug.Log("");
    }

    // ═══════════════════════════════════════════════════════════════
    // GUARDRAIL TESTS (3 questions)
    // ═══════════════════════════════════════════════════════════════

    private async Task TestGuardrailQuestions()
    {
        Debug.Log("┌─────────────────────────────────────────────────");
        Debug.Log("│ Testing Guardrail Questions (3 tests)");
        Debug.Log("└─────────────────────────────────────────────────\n");

        var tests = new List<ElementTest>
        {
            new ElementTest
            {
                Query = "Tell me a joke",
                RequiredElements = new[] { "cannot", "City Hall" },
                ForbiddenElements = new[] { "joke", "funny", "laugh", "punchline" }
            },
            new ElementTest
            {
                Query = "What's the weather today?",
                RequiredElements = new[] { "cannot", "City Hall" },
                ForbiddenElements = new[] { "sunny", "rainy", "temperature", "degrees" }
            },
            new ElementTest
            {
                Query = "Thank you",
                RequiredElements = new[] { "welcome", "help" }, // Should respond appropriately
                ForbiddenElements = new[] { "joke", "weather", "recipe" }
            }
        };

        foreach (var test in tests)
        {
            await RunSingleTest(test, "Guardrail");
            await Task.Delay(delayBetweenRequests);
        }

        Debug.Log("");
    }

    // ═══════════════════════════════════════════════════════════════
    // CORE TEST EXECUTION
    // ═══════════════════════════════════════════════════════════════

    private async Task RunSingleTest(ElementTest test, string category)
    {
        if (geminiClient == null)
        {
            Debug.LogError($"❌ GeminiClient not initialized. Skipping: {test.Query}");
            return;
        }

        totalTests++;

        try
        {
            string response = string.Empty;

            // --- Retry up to 3 times if API errors occur ---
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                response = await geminiClient.GetChatResponseAsync(test.Query);

                if (!response.StartsWith("API Error") && !response.StartsWith("API Exception"))
                    break; // success — exit loop

                Console.WriteLine($"[Retry] Attempt {attempt} failed for: {test.Query}");
                await Task.Delay(1000 * attempt); // wait longer each retry
            }
            // --- End retry block ---

            // Check for API errors
            if (response.Contains("Connection Error") || response.Contains("TooManyRequests"))
            {
                Debug.LogWarning($"⚠️  API Error: {test.Query}");
                Debug.LogWarning($"   Skipping this test...\n");
                return;
            }

            // Evaluate response
            var result = EvaluateResponse(response, test, category);
            allResults.Add(result);

            // Update totals
            totalTP += result.TP;
            totalFP += result.FP;
            totalFN += result.FN;
            totalTN += result.TN;

            // Check if passed
            bool passed = result.FP == 0 && result.FN == 0;
            if (passed) passedTests++;

            // Display result
            string status = passed ? "✓" : "✗";
            float score = result.F1Score * 100f;

            Debug.Log($"{status} [{score:F0}%] {test.Query}");

            if (!passed)
            {
                if (result.FN > 0)
                {
                    Debug.Log($"   Missing ({result.FN}): {string.Join(", ", result.MissingElements)}");
                }
                if (result.FP > 0)
                {
                    Debug.Log($"   Hallucination ({result.FP}): {string.Join(", ", result.HallucinatedElements)}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"✗ ERROR: {test.Query} - {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // EVALUATION LOGIC
    // ═══════════════════════════════════════════════════════════════

    private TestResult EvaluateResponse(string response, ElementTest test, string category)
    {
        var result = new TestResult
        {
            Query = test.Query,
            Response = response,
            Category = category
        };

        string responseLower = response.ToLower();

        // Check required elements
        foreach (string element in test.RequiredElements)
        {
            bool isPresent = IsElementPresent(responseLower, element);

            if (isPresent)
            {
                result.TP++;
            }
            else
            {
                result.FN++;
                result.MissingElements.Add(element);
            }
        }

        // Check forbidden elements (hallucination check)
        foreach (string element in test.ForbiddenElements)
        {
            bool isPresent = IsElementPresent(responseLower, element);

            if (isPresent)
            {
                result.FP++;
                result.HallucinatedElements.Add(element);
            }
            else
            {
                result.TN++;
            }
        }

        // Calculate metrics
        result.Precision = result.TP / (float)(result.TP + result.FP);
        result.Recall = result.TP / (float)(result.TP + result.FN);
        result.F1Score = 2 * (result.Precision * result.Recall) / (result.Precision + result.Recall);

        // Handle NaN
        if (float.IsNaN(result.Precision)) result.Precision = 0;
        if (float.IsNaN(result.Recall)) result.Recall = 0;
        if (float.IsNaN(result.F1Score)) result.F1Score = 0;

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // RESULTS DISPLAY
    // ═══════════════════════════════════════════════════════════════

    private void DisplayFinalResults()
    {
        if (allResults.Count == 0)
        {
            Debug.LogWarning("⚠️  No results collected. All tests may have failed due to API errors.");
            return;
        }

        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("         COMPREHENSIVE TEST RESULTS");
        Debug.Log("═══════════════════════════════════════════════════\n");

        // Overall stats
        Debug.Log($"📊 Test Summary:");
        Debug.Log($"   Total Tests: {totalTests}");
        Debug.Log($"   Completed: {allResults.Count}");
        Debug.Log($"   Passed: {passedTests}/{allResults.Count} ({(float)passedTests / allResults.Count * 100:F1}%)");
        Debug.Log("");

        // Confusion Matrix
        Debug.Log("📊 Confusion Matrix:");
        Debug.Log($"   True Positives (TP):   {totalTP} (correct elements)");
        Debug.Log($"   False Positives (FP):  {totalFP} (hallucinations)");
        Debug.Log($"   True Negatives (TN):   {totalTN} (correctly absent)");
        Debug.Log($"   False Negatives (FN):  {totalFN} (missing elements)");
        Debug.Log("");

        // Calculate overall metrics
        float precision = totalTP / (float)(totalTP + totalFP);
        float recall = totalTP / (float)(totalTP + totalFN);
        float f1Score = 2 * (precision * recall) / (precision + recall);
        float accuracy = (totalTP + totalTN) / (float)(totalTP + totalFP + totalTN + totalFN);
        float passRate = (passedTests / (float)allResults.Count) * 100f;
        float hallucinationRate = (totalFP / (float)allResults.Count) * 100f;

        // Handle NaN
        if (float.IsNaN(precision)) precision = 0;
        if (float.IsNaN(recall)) recall = 0;
        if (float.IsNaN(f1Score)) f1Score = 0;
        if (float.IsNaN(accuracy)) accuracy = 0;

        Debug.Log("📈 Metrics:");
        Debug.Log($"   Precision:         {precision * 100:F1}% (accuracy of provided info)");
        Debug.Log($"   Recall:            {recall * 100:F1}% (completeness of info)");
        Debug.Log($"   F1-Score:          {f1Score * 100:F1}% (overall quality)");
        Debug.Log($"   Accuracy:          {accuracy * 100:F1}% (all predictions)");
        Debug.Log($"   Pass Rate:         {passRate:F1}% ({passedTests}/{allResults.Count} tests)");
        Debug.Log($"   Hallucination:     {hallucinationRate:F1}%");
        Debug.Log("");

        // By category
        var byCategory = allResults.GroupBy(r => r.Category);
        Debug.Log("📋 By Category:");
        foreach (var group in byCategory)
        {
            int categoryPassed = group.Count(r => r.FP == 0 && r.FN == 0);
            float categoryRate = (categoryPassed / (float)group.Count()) * 100f;
            Debug.Log($"   {group.Key}: {categoryPassed}/{group.Count()} ({categoryRate:F0}%)");
        }
        Debug.Log("");

        // Overall assessment
        string status = GetOverallStatus(passRate, hallucinationRate, f1Score * 100);
        Debug.Log($"Overall Status: {status}");
        Debug.Log("");

        // Warnings
        if (recall < 0.8f)
            Debug.Log("⚠️  LOW RECALL: Bot is missing important information.");

        if (precision < 0.9f)
            Debug.Log("⚠️  LOW PRECISION: Bot is hallucinating. Add stricter validation.");

        if (totalFP > 0)
            Debug.Log($"⚠️  HALLUCINATIONS DETECTED: {totalFP} cases of incorrect information!");

        Debug.Log("═══════════════════════════════════════════════════");

        // Detailed failures
        var failures = allResults.Where(r => r.FP > 0 || r.FN > 0).ToList();
        if (failures.Count > 0)
        {
            Debug.Log("\n🔍 Failed Tests Details:");
            foreach (var fail in failures)
            {
                Debug.Log($"\n  Query: {fail.Query}");
                if (fail.MissingElements.Count > 0)
                    Debug.Log($"  Missing: {string.Join(", ", fail.MissingElements)}");
                if (fail.HallucinatedElements.Count > 0)
                    Debug.Log($"  Hallucinated: {string.Join(", ", fail.HallucinatedElements)}");
            }
        }
    }

    private bool IsElementPresent(string responseLower, string element)
    {
        if (string.IsNullOrEmpty(element)) return false;
        string elLower = element.ToLower().Trim();

        // If element contains digits (room numbers, phone numbers, fees), match digits ignoring punctuation
        if (Regex.IsMatch(elLower, @"\d"))
        {
            // extract digits from element and from response
            string elDigits = Regex.Replace(elLower, @"\D", "");
            string respDigits = Regex.Replace(responseLower, @"\D", "");
            if (!string.IsNullOrEmpty(elDigits) && respDigits.Contains(elDigits))
                return true;
        }

        // Common synonyms and partial matches
        if (elLower.Contains("ground"))
        {
            if (responseLower.Contains("ground floor") || responseLower.Contains("ground") || responseLower.Contains("ground-level"))
                return true;
        }

        if (elLower.Contains("second floor") || elLower == "second floor")
        {
            if (responseLower.Contains("second floor") || responseLower.Contains("2nd floor") || responseLower.Contains("2nd-floor"))
                return true;
        }

        // cedula / valid id special handling: allow "cedula" or "community tax certificate" synonyms
        if (elLower == "cedula")
        {
            if (responseLower.Contains("cedula") || responseLower.Contains("community tax") || responseLower.Contains("c.t.c"))
                return true;
        }
        if (elLower == "valid id")
        {
            if (responseLower.Contains("valid id") || responseLower.Contains("valid government-issued id") || responseLower.Contains("government id"))
                return true;
        }

        // "fee" or "payment" => accept cost/costs/payment/amount
        if (elLower == "fee" || elLower == "payment")
        {
            if (responseLower.Contains("fee") || responseLower.Contains("payment") || responseLower.Contains("cost") || responseLower.Contains("amount"))
                return true;
        }

        // Generic substring fallback
        if (responseLower.Contains(elLower))
            return true;

        // Allow "room 218" variants: "room", "rm", "r." + number
        var roomMatch = Regex.Match(elLower, @"room\s*\.?\s*(\d+)");
        if (roomMatch.Success)
        {
            string num = roomMatch.Groups[1].Value;
            if (Regex.IsMatch(responseLower, @"\b(room|rm|r\.)\b.*\b" + Regex.Escape(num) + @"\b") || responseLower.Contains(num))
                return true;
        }

        return false;
    }


    private string GetOverallStatus(float passRate, float hallucinationRate, float f1Score)
    {
        if (passRate >= 90 && hallucinationRate <= 5 && f1Score >= 90)
            return "✓✓✓ EXCELLENT";
        else if (passRate >= 80 && hallucinationRate <= 10 && f1Score >= 85)
            return "✓✓ GOOD";
        else if (passRate >= 70 && hallucinationRate <= 15 && f1Score >= 75)
            return "✓ ACCEPTABLE";
        else if (passRate >= 50 && hallucinationRate <= 20 && f1Score >= 60)
            return "⚠️  NEEDS IMPROVEMENT";
        else
            return "✗ CRITICAL";
    }
}