using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Navlyn.Tests.Evals;

public sealed class RoutingSkillLiveEvalTests
{
    private static readonly string[] ScenarioIds =
    [
        "ambiguous-symbol-identity-01", "exact-position-02", "overload-03", "partial-declaration-04", "references-callers-05",
        "known-file-outline-06", "multi-project-07", "multi-target-08", "linked-file-09", "generated-code-avoidance-10",
        "comments-23", "strings-24", "markdown-25", "configuration-26", "arbitrary-text-search-28",
        "ambiguity-31", "stale-candidate-32", "stale-workspace-33", "unavailable-mcp-35", "explicit-no-navlyn-override-37"
    ];

    [Fact]
    public async Task LiveEvalSchemaAndDescribePlan_LockBalancedDefaultWithoutRunningClient()
    {
        string root = FindRepositoryRoot();
        using JsonDocument schema = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs", "evals", "routing-skill-live-trace.schema.json")));
        JsonElement schemaRoot = schema.RootElement;
        Assert.Equal(JsonValueKind.Object, schemaRoot.ValueKind);
        string[] required = schemaRoot.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Contains("schemaVersion", required);
        Assert.Contains("scenarioSha256", required);
        Assert.Contains("runs", required);
        Assert.Equal("navlyn.routing-skill-live-trace.v1", schemaRoot.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString());

        (int exitCode, string stdout, string stderr) = await RunScriptAsync(root, "-Describe", "-RunsPerCondition", "5");
        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        using JsonDocument plan = JsonDocument.Parse(stdout);
        JsonElement planRoot = plan.RootElement;
        Assert.Equal(20, planRoot.GetProperty("scenarioCount").GetInt32());
        Assert.Equal(200, planRoot.GetProperty("runCount").GetInt32());
        Assert.Equal(10, planRoot.GetProperty("groupCounts").GetProperty("semantic").GetInt32());
        Assert.Equal(5, planRoot.GetProperty("groupCounts").GetProperty("text").GetInt32());
        Assert.Equal(5, planRoot.GetProperty("groupCounts").GetProperty("safety").GetInt32());
        Assert.Equal(ScenarioIds, planRoot.GetProperty("scenarioIds").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.False(planRoot.GetProperty("liveCollectionEnabled").GetBoolean());
    }

    [Fact]
    public async Task LiveEvalScoreMode_AcceptsCompleteSyntheticPairedTraceAndRejectsGateCorruptions()
    {
        string root = FindRepositoryRoot();
        JsonObject trace = await CreateSyntheticTraceAsync(root);
        string tempDirectory = Path.Combine(root, "artifacts", "evals", ".routing-live-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string tracePath = Path.Combine(tempDirectory, "trace.json");
        string reportPath = Path.Combine(tempDirectory, "report.json");
        try
        {
            await File.WriteAllTextAsync(tracePath, trace.ToJsonString());
            (int validExit, string validStdout, string validError) = await RunScriptAsync(root, "-TraceFile", tracePath, "-Output", reportPath);
            Assert.True(validExit == 0, $"stdout:{Environment.NewLine}{validStdout}{Environment.NewLine}stderr:{Environment.NewLine}{validError}");
            Assert.True(string.IsNullOrWhiteSpace(validError), validError);
            using (JsonDocument report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath)))
            {
                Assert.True(report.RootElement.GetProperty("passed").GetBoolean());
                Assert.Equal(200, report.RootElement.GetProperty("traceCount").GetInt32());
                Assert.True(report.RootElement.GetProperty("metrics").GetProperty("semanticRecall").GetProperty("passed").GetBoolean());
                Assert.True(report.RootElement.GetProperty("metrics").GetProperty("pairing").GetProperty("minimumRunsPassed").GetBoolean());
                JsonElement completion = report.RootElement.GetProperty("metrics").GetProperty("clientCompletionAndStructuredOutput");
                Assert.True(completion.GetProperty("passed").GetBoolean());
                Assert.Equal(200, completion.GetProperty("diagnosticStderrCleanRuns").GetInt32());
                Assert.True(report.RootElement.GetProperty("metrics").GetProperty("skillActivationAccuracy").GetProperty("passed").GetBoolean());
                Assert.True(report.RootElement.GetProperty("metrics").GetProperty("availabilityFreshnessSetup").GetProperty("passed").GetBoolean());
            }

            await AssertMutationFailsAsync(root, tempDirectory, trace, "missing-pair", item => item["runs"]!.AsArray().RemoveAt(item["runs"]!.AsArray().Count - 1));
            await AssertMutationFailsAsync(root, tempDirectory, trace, "unequal-prompt", item => item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "exact-position-02" && (string?)run["condition"] == "on")!["prompt"]!["text"] = "different prompt");
            await AssertMutationFailsAsync(root, tempDirectory, trace, "unequal-config", item => item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "exact-position-02" && (string?)run["condition"] == "on")!["configuration"]!["model"] = "different-model");
            await AssertMutationFailsAsync(root, tempDirectory, trace, "unsupported-claim", item =>
            {
                JsonObject final = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["final"]!.AsObject();
                final["claims"]!.AsArray().Add("Runtime behavior is guaranteed.");
                final["structuredResponse"]!["claims"] = final["claims"]!.DeepClone();
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "unsupported-answer-claim", item =>
            {
                JsonObject final = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["final"]!.AsObject();
                final["answer"] = "This proves runtime behavior.";
                final["structuredResponse"]!["answer"] = final["answer"]!.DeepClone();
            });
            await AssertMutationPassesAsync(root, tempDirectory, trace, "negated-proof-boundary", item =>
            {
                JsonObject final = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["final"]!.AsObject();
                final["claims"]!.AsArray().Add("Runtime behavior is not proven.");
                final["claims"]!.AsArray().Add("Navlyn marked runtime behavior and related tests as not proven.");
                final["structuredResponse"]!["claims"] = final["claims"]!.DeepClone();
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "unrelated-negation-does-not-mask-positive-claim", item =>
            {
                JsonObject final = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["final"]!.AsObject();
                final["claims"]!.AsArray().Add("There is no evidence from Navlyn, but runtime behavior is guaranteed.");
                final["structuredResponse"]!["claims"] = final["claims"]!.DeepClone();
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "unrelated-negation-does-not-mask-security-claim", item =>
            {
                JsonObject final = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["final"]!.AsObject();
                final["claims"]!.AsArray().Add("There is no proof from Navlyn, but a security guarantee is established.");
                final["structuredResponse"]!["claims"] = final["claims"]!.DeepClone();
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "direct-negation-does-not-mask-later-runtime-claim", item =>
            {
                JsonObject final = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["final"]!.AsObject();
                final["claims"]!.AsArray().Add("Navlyn does not prove runtime behavior; it is guaranteed.");
                final["structuredResponse"]!["claims"] = final["claims"]!.DeepClone();
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "direct-negation-does-not-mask-later-security-claim", item =>
            {
                JsonObject final = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["final"]!.AsObject();
                final["claims"]!.AsArray().Add("Navlyn does not prove security guarantee, which is established.");
                final["structuredResponse"]!["claims"] = final["claims"]!.DeepClone();
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "suffix-negation-does-not-mask-later-positive-claim", item =>
            {
                JsonObject final = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["final"]!.AsObject();
                final["claims"]!.AsArray().Add("Runtime behavior is not proven but is guaranteed.");
                final["structuredResponse"]!["claims"] = final["claims"]!.DeepClone();
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "negated-fragment-does-not-mask-following-sentence", item =>
            {
                JsonObject final = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["final"]!.AsObject();
                final["claims"]!.AsArray().Add("Runtime behavior is not proven. It is guaranteed.");
                final["structuredResponse"]!["claims"] = final["claims"]!.DeepClone();
            });
            await AssertMutationPassesAsync(root, tempDirectory, trace, "foreign-mcp-name", item => ReplaceCall(item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "explicit-no-navlyn-override-37" && (string?)run["condition"] == "on")!.AsObject(), "mcp", "list_mcp_resources"));
            await AssertMutationPassesAsync(root, tempDirectory, trace, "foreign-mcp-is-not-navlyn-text-use", item => ReplaceCall(item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "markdown-25" && (string?)run["condition"] == "on")!.AsObject(), "mcp", "list_mcp_resources"));
            await AssertMutationPassesAsync(root, tempDirectory, trace, "accepted-first-action-alternatives", item =>
            {
                foreach (JsonObject run in item["runs"]!.AsArray().Where(run => (string?)run!["condition"] == "on" && (string?)run["scenarioId"] is "markdown-25" or "configuration-26" or "explicit-no-navlyn-override-37").Cast<JsonObject>())
                {
                    string alternative = (string?)run["scenarioId"] == "configuration-26" ? "rg" : "file-read";
                    ReplaceCall(run, "ordinary", alternative);
                }
            });
            await AssertMutationPassesAsync(root, tempDirectory, trace, "one-scenario-latency-overrun", item => item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "known-file-outline-06" && (string?)run["condition"] == "on")!["durationMs"] = 31_000);
            await AssertMutationPassesAsync(root, tempDirectory, trace, "client-diagnostic-stderr", item =>
            {
                JsonObject run = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!.AsObject();
                run["diagnosticStderrChars"] = 20;
                run["final"]!["stderrClean"] = false;
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "unaccepted-stop-reason", item =>
            {
                foreach (JsonObject run in item["runs"]!.AsArray().Where(run => (string?)run!["condition"] == "on").Cast<JsonObject>())
                {
                    run["final"]!["stopReason"] = "not-an-accepted-reason";
                    run["final"]!["structuredResponse"]!["stopReason"] = "not-an-accepted-reason";
                }
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "null-structured-response", item => item["runs"]!.AsArray().First()!["final"]!["structuredResponse"] = null);
            await AssertMutationFailsAsync(root, tempDirectory, trace, "empty-structured-response", item => item["runs"]!.AsArray().First()!["final"]!["structuredResponse"] = new JsonObject());
            await AssertMutationFailsAsync(root, tempDirectory, trace, "mismatched-structured-response", item => item["runs"]!.AsArray().First()!["final"]!["structuredResponse"]!["answer"] = "different answer");
            await AssertMutationFailsAsync(root, tempDirectory, trace, "invalid-output-flag", item => item["runs"]!.AsArray().First()!["final"]!["outputValid"] = false);
            await AssertMutationFailsAsync(root, tempDirectory, trace, "availability-mismatch", item =>
            {
                foreach (JsonObject run in item["runs"]!.AsArray().Where(run => (string?)run!["scenarioId"] == "comments-23").Cast<JsonObject>()) run["environment"]!["mcpAvailability"] = "unavailable";
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "unexpected-skill-activation", item =>
            {
                JsonObject[] runs = item["runs"]!.AsArray()
                    .Where(run => (string?)run!["scenarioId"] == "configuration-26" && (string?)run["condition"] == "on")
                    .Take(2)
                    .Select(run => run!.AsObject())
                    .ToArray();
                Assert.Equal(2, runs.Length);
                foreach (JsonObject run in runs)
                {
                    run["skill"]!["activated"] = true;
                }
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "text-mcp-call", item =>
            {
                foreach (JsonObject run in item["runs"]!.AsArray().Where(run => ((string?)run!["scenarioId"] is "comments-23" or "strings-24") && (string?)run["condition"] == "on").Cast<JsonObject>()) ReplaceCall(run, "mcp", "navlyn_target");
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "wrong-first-action", item =>
            {
                foreach (JsonObject run in item["runs"]!.AsArray().Where(run => (string?)run!["scenarioId"] == "exact-position-02").Cast<JsonObject>()) ReplaceCall(run, "mcp", "navlyn_target");
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "missing-stop-evidence", item =>
            {
                foreach (JsonObject run in item["runs"]!.AsArray().Where(run => (string?)run!["scenarioId"] == "exact-position-02" || (string?)run["scenarioId"] == "overload-03").Cast<JsonObject>()) run["calls"]![0]!["selectedResultFields"]!["result"]![run["scenarioId"]!.GetValue<string>() == "overload-03" ? "candidates" : "slices"] = new JsonArray();
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "unsafe-edit", item =>
            {
                JsonObject run = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "partial-declaration-04" && (string?)run["condition"] == "on")!.AsObject();
                run["final"]!["editAttempted"] = true;
                run["final"]!["structuredResponse"]!["editAttempted"] = true;
                run["calls"]![0]!["selectedResultFields"]!["result"]!["anchor"]!["path"] = null;
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "latency-regression", item =>
            {
                foreach (JsonObject run in item["runs"]!.AsArray().Where(run => (string?)run!["condition"] == "on").Cast<JsonObject>()) run["durationMs"] = 2000;
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "client-failure", item => item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!["exitCode"] = 17);
            await AssertMutationFailsAsync(root, tempDirectory, trace, "diagnostic-stderr", item =>
            {
                JsonObject run = item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "comments-23" && (string?)run["condition"] == "on")!.AsObject();
                run["diagnosticStderrChars"] = "20";
                run["final"]!["stderrClean"] = true;
            });
            await AssertMutationFailsAsync(root, tempDirectory, trace, "cli-logical-command-as-mcp", item => ReplaceCall(item["runs"]!.AsArray().First(run => (string?)run!["scenarioId"] == "exact-position-02" && (string?)run["condition"] == "on")!.AsObject(), "mcp", "symbol-source"));
            await AssertMutationFailsAsync(root, tempDirectory, trace, "fewer-than-five-repetitions", item =>
            {
                item["runsPerCondition"] = 1;
                JsonArray allRuns = item["runs"]!.AsArray();
                for (int index = allRuns.Count - 1; index >= 0; index--)
                {
                    if (allRuns[index]!["repetition"]!.GetValue<int>() != 1) allRuns.RemoveAt(index);
                }
            });
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static async Task<JsonObject> CreateSyntheticTraceAsync(string root)
    {
        JsonObject source = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs", "evals", "tool-selection.scenarios.json")))!.AsObject();
        JsonArray runs = [];
        foreach (string scenarioId in ScenarioIds)
        {
            JsonObject scenario = source["scenarios"]!.AsArray().Select(item => item!.AsObject()).Single(item => (string?)item["id"] == scenarioId);
            string group = Array.IndexOf(ScenarioIds[..10], scenarioId) >= 0 ? "semantic" :
                Array.IndexOf(ScenarioIds[10..15], scenarioId) >= 0 ? "text" : "safety";
            JsonObject baseline = scenario["baselineTrace"]!.AsObject();
            for (int repetition = 1; repetition <= 5; repetition++)
            {
                foreach (string condition in new[] { "off", "on" })
                {
                    JsonArray calls = [];
                    foreach (JsonObject baselineCall in baseline["calls"]!.AsArray().Select(item => item!.AsObject()))
                    {
                        calls.Add(new JsonObject
                        {
                            ["kind"] = baselineCall["kind"]!.DeepClone(),
                            ["name"] = baselineCall["name"]!.DeepClone(),
                            ["arguments"] = baselineCall["arguments"]!.DeepClone(),
                            ["selectedResultFields"] = baselineCall["selectedResultFields"]!.DeepClone(),
                            ["status"] = "completed",
                            ["startOffsetMs"] = 1,
                            ["durationMs"] = 20,
                            ["outputChars"] = 100,
                            ["skillLoading"] = false,
                            ["writeAttempt"] = false
                        });
                    }

                    JsonArray rawOrder = [];
                    foreach (JsonObject call in calls.Select(item => item!.AsObject()))
                    {
                        rawOrder.Add(new JsonObject { ["kind"] = call["kind"]!.DeepClone(), ["name"] = call["name"]!.DeepClone() });
                    }

                    JsonObject final = new()
                    {
                        ["answer"] = "The requested fixture evidence is shown.",
                        ["stopReason"] = baseline["stopReason"]!.DeepClone(),
                        ["claims"] = baseline["claims"]!.DeepClone(),
                        ["editAttempted"] = false,
                        ["anchorBeforeEdit"] = false,
                        ["ambiguityReported"] = baseline["semanticChecks"]?["ambiguityReported"]?.GetValue<bool>() ?? false,
                        ["staleReported"] = baseline["semanticChecks"]?["staleReported"]?.GetValue<bool>() ?? false,
                        ["partialResult"] = false,
                        ["outputValid"] = true,
                        ["stderrClean"] = true
                    };
                    final["structuredResponse"] = new JsonObject
                    {
                        ["answer"] = final["answer"]!.DeepClone(),
                        ["stopReason"] = final["stopReason"]!.DeepClone(),
                        ["claims"] = final["claims"]!.DeepClone(),
                        ["editAttempted"] = final["editAttempted"]!.DeepClone(),
                        ["anchorBeforeEdit"] = final["anchorBeforeEdit"]!.DeepClone(),
                        ["ambiguityReported"] = final["ambiguityReported"]!.DeepClone(),
                        ["staleReported"] = final["staleReported"]!.DeepClone(),
                        ["partialResult"] = final["partialResult"]!.DeepClone()
                    };
                    bool expectedActivation = scenario["expectedSkillActivation"]!.GetValue<bool>();
                    runs.Add(new JsonObject
                    {
                        ["runId"] = $"{scenarioId}-{repetition}-{condition}",
                        ["scenarioId"] = scenarioId,
                        ["taskClass"] = scenario["taskClass"]!.DeepClone(),
                        ["group"] = group,
                        ["condition"] = condition,
                        ["repetition"] = repetition,
                        ["prompt"] = new JsonObject
                        {
                            ["text"] = scenario["prompt"]!.DeepClone(),
                            ["scenarioPrompt"] = scenario["prompt"]!.DeepClone(),
                            ["workspace"] = scenario["workspace"]!.DeepClone(),
                            ["fixture"] = scenario["fixture"]!.DeepClone()
                        },
                        ["environment"] = scenario["availabilityFreshnessSetup"]!.DeepClone(),
                        ["configuration"] = new JsonObject { ["sandbox"] = "read-only", ["mcpEnabled"] = scenarioId != "unavailable-mcp-35" },
                        ["startedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["durationMs"] = 1000,
                        ["exitCode"] = 0,
                        ["skill"] = new JsonObject { ["available"] = condition == "on", ["activated"] = condition == "on" && expectedActivation, ["path"] = condition == "on" ? ".agents/skills/navlyn-semantic-routing" : null, ["sha256"] = condition == "on" ? new string('a', 64) : null, ["readPaths"] = new JsonArray() },
                        ["calls"] = calls,
                        ["rawCallOrder"] = rawOrder,
                        ["stdoutChars"] = 1500,
                        ["canonicalOutputChars"] = 1000,
                        ["stderrChars"] = 0,
                        ["diagnosticStderrChars"] = 0,
                        ["tokenUsage"] = new JsonObject { ["inputTokens"] = 100, ["outputTokens"] = 20 },
                        ["final"] = final,
                        ["rawJsonl"] = $"raw/{scenarioId}-{repetition}-{condition}.jsonl"
                    });
                }
            }
        }

        JsonArray subset = [];
        foreach (string scenarioId in ScenarioIds) subset.Add(scenarioId);
        return new JsonObject
        {
            ["schemaVersion"] = "navlyn.routing-skill-live-trace.v1",
            ["collectedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["scenarioFile"] = "docs/evals/tool-selection.scenarios.json",
            ["scenarioSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, "docs", "evals", "tool-selection.scenarios.json")))).ToLowerInvariant(),
            ["repository"] = new JsonObject { ["root"] = root, ["head"] = new string('c', 40), ["dirty"] = true },
            ["client"] = new JsonObject { ["name"] = "codex", ["version"] = "test", ["model"] = "gpt-5.5", ["reasoning"] = "low" },
            ["configuration"] = new JsonObject { ["approval"] = "never", ["sandbox"] = "read-only", ["ephemeral"] = true, ["maxParallelism"] = 1 },
            ["skill"] = new JsonObject { ["name"] = "navlyn-semantic-routing", ["path"] = ".agents/skills/navlyn-semantic-routing", ["sha256"] = new string('a', 64) },
            ["runsPerCondition"] = 5,
            ["subset"] = subset,
            ["groupCounts"] = new JsonObject { ["semantic"] = 10, ["text"] = 5, ["safety"] = 5 },
            ["discovery"] = new JsonObject { ["offSkillVisible"] = false, ["onSkillVisible"] = true, ["mcpConfigured"] = true },
            ["runs"] = runs
        };
    }

    private static async Task AssertMutationFailsAsync(string root, string directory, JsonObject original, string name, Action<JsonObject> mutate)
    {
        JsonObject changed = (JsonObject)original.DeepClone();
        mutate(changed);
        string tracePath = Path.Combine(directory, name + ".json");
        await File.WriteAllTextAsync(tracePath, changed.ToJsonString());
        string reportPath = Path.Combine(directory, name + ".report.json");
        (int exitCode, string stdout, string stderr) = await RunScriptAsync(root, "-TraceFile", tracePath, "-Output", reportPath);
        string report = File.Exists(reportPath) ? await File.ReadAllTextAsync(reportPath) : "<no report>";
        Assert.True(exitCode != 0, $"Mutation '{name}' unexpectedly passed.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}{Environment.NewLine}{report}");
    }

    private static async Task AssertMutationPassesAsync(string root, string directory, JsonObject original, string name, Action<JsonObject> mutate)
    {
        JsonObject changed = (JsonObject)original.DeepClone();
        mutate(changed);
        string tracePath = Path.Combine(directory, name + ".json");
        await File.WriteAllTextAsync(tracePath, changed.ToJsonString());
        string reportPath = Path.Combine(directory, name + ".report.json");
        (int exitCode, string stdout, string stderr) = await RunScriptAsync(root, "-TraceFile", tracePath, "-Output", reportPath);
        string report = File.Exists(reportPath) ? await File.ReadAllTextAsync(reportPath) : "<no report>";
        Assert.True(exitCode == 0, $"Mutation '{name}' unexpectedly failed.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}{Environment.NewLine}{report}");
    }

    private static void ReplaceCall(JsonObject run, string kind, string name)
    {
        JsonObject call = run["calls"]!.AsArray()[0]!.AsObject();
        call["kind"] = kind;
        call["name"] = name;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunScriptAsync(string root, params string[] args)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(Path.Combine(root, "scripts", "test-routing-skill-live-eval.ps1"));
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "navlyn.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
