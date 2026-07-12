using System.Diagnostics;
using System.Text.Json;

namespace Navlyn.Tests.Evals;

public sealed class ToolSelectionEvalTests
{
    [Fact]
    public void ToolSelectionScenarioFile_IsMachineReadable()
    {
        string repoRoot = FindRepositoryRoot();
        string scenarioPath = Path.Combine(repoRoot, "docs", "evals", "tool-selection.scenarios.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scenarioPath));
        JsonElement root = document.RootElement;

        Assert.Equal("navlyn.tool-selection-eval.v1", root.GetProperty("schemaVersion").GetString());
        JsonElement scenarios = root.GetProperty("scenarios");
        Assert.True(scenarios.GetArrayLength() >= 20);
        foreach (JsonElement scenario in scenarios.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("id").GetString()));
            Assert.True(scenario.GetProperty("expectedFirstSteps").GetArrayLength() >= 1);
            Assert.True(scenario.GetProperty("acceptedStopConditions").GetArrayLength() >= 1);
            Assert.True(scenario.GetProperty("baselineTrace").GetProperty("chosenSequence").GetArrayLength() >= 1);
        }
    }

    [Fact]
    public async Task ToolSelectionEvalRunner_BaselineTracesPass()
    {
        string repoRoot = FindRepositoryRoot();
        string outputPath = Path.Combine(Path.GetTempPath(), "navlyn-tool-selection-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "test-tool-selection-eval.ps1"));
            process.StartInfo.ArgumentList.Add("-UseBaselineTraces");
            process.StartInfo.ArgumentList.Add("-Output");
            process.StartInfo.ArgumentList.Add(outputPath);

            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(outputPath));
            JsonElement root = report.RootElement;
            Assert.True(root.GetProperty("passed").GetBoolean());
            Assert.Equal(1.0, root.GetProperty("score").GetDouble());
            Assert.True(root.GetProperty("scenarioCount").GetInt32() >= 20);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void WrongSymbolAvoidanceScenarioFile_IsMachineReadable()
    {
        string repoRoot = FindRepositoryRoot();
        string scenarioPath = Path.Combine(repoRoot, "docs", "evals", "wrong-symbol-avoidance.scenarios.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scenarioPath));
        JsonElement root = document.RootElement;

        Assert.Equal("navlyn.wrong-symbol-avoidance-eval.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(1.0, root.GetProperty("minimumScore").GetDouble());
        JsonElement scenarios = root.GetProperty("scenarios");
        Assert.True(scenarios.GetArrayLength() >= 2);
        foreach (JsonElement scenario in scenarios.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("workspace").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("file").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("query").GetString()));
            Assert.True(scenario.TryGetProperty("textSearchExpectedWrongLine", out JsonElement line) && line.GetInt32() > 0);
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("navlynExpectedFirstConfidence").GetString()));
        }
    }

    [Fact]
    public async Task WrongSymbolAvoidanceEvalRunner_Passes()
    {
        string repoRoot = FindRepositoryRoot();
        string outputPath = Path.Combine(Path.GetTempPath(), "navlyn-wrong-symbol-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "test-wrong-symbol-avoidance-eval.ps1"));
            process.StartInfo.ArgumentList.Add("-NoBuild");
            process.StartInfo.ArgumentList.Add("-Output");
            process.StartInfo.ArgumentList.Add(outputPath);

            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(outputPath));
            JsonElement root = report.RootElement;
            Assert.Equal("navlyn.wrong-symbol-avoidance-eval.report.v1", root.GetProperty("schemaVersion").GetString());
            Assert.Equal(1.0, root.GetProperty("score").GetDouble());
            Assert.Equal(root.GetProperty("total").GetInt32(), root.GetProperty("passed").GetInt32());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void McpAgentTraceFile_IsMachineReadable()
    {
        string repoRoot = FindRepositoryRoot();
        string tracePath = Path.Combine(repoRoot, "docs", "evals", "mcp-agent-traces.replay.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(tracePath));
        JsonElement root = document.RootElement;

        Assert.Equal("navlyn.mcp-agent-trace-eval.v1", root.GetProperty("schemaVersion").GetString());
        JsonElement traces = root.GetProperty("traces");
        Assert.True(traces.GetArrayLength() >= 20);
        foreach (JsonElement trace in traces.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(trace.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(trace.GetProperty("kind").GetString()));
            Assert.True(trace.GetProperty("chosenSequence").GetArrayLength() >= 1);
            Assert.True(trace.TryGetProperty("stdoutChars", out JsonElement stdoutChars) && stdoutChars.GetInt32() >= 0);
        }
    }

    [Fact]
    public async Task McpAgentTraceEvalRunner_Passes()
    {
        string repoRoot = FindRepositoryRoot();
        string outputPath = Path.Combine(Path.GetTempPath(), "navlyn-agent-trace-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "test-mcp-agent-trace-eval.ps1"));
            process.StartInfo.ArgumentList.Add("-Output");
            process.StartInfo.ArgumentList.Add(outputPath);

            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(outputPath));
            JsonElement root = report.RootElement;
            Assert.Equal("navlyn.mcp-agent-trace-eval.report.v1", root.GetProperty("schemaVersion").GetString());
            Assert.True(root.GetProperty("passed").GetBoolean());
            Assert.True(root.GetProperty("score").GetDouble() >= 0.97);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "navlyn.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
