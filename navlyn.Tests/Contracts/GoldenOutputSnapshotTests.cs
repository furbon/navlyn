using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.MSBuild;
using Navlyn.Cli.OutputProfiles;
using Navlyn.Mcp.Configuration;
using Navlyn.Mcp.Tools;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Tests.Contracts;

public sealed class GoldenOutputSnapshotTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void WorkflowCompactEnvelope_MatchesGoldenSnapshot()
    {
        using MSBuildWorkspace workspaceHandle = MSBuildWorkspace.Create();
        JsonObject output = OutputProfile.Format(
            CreateWorkspace(workspaceHandle),
            "review-diff",
            OutputProfile.Compact,
            new
            {
                workspace = "navlyn.slnx",
                kind = "solution",
                command = "review-diff",
                findings = new[]
                {
                    new
                    {
                        ruleId = "sample.rule",
                        severity = "info",
                        claim = "Sample finding"
                    }
                },
                changedSymbols = new { totalSymbols = 1, limit = 20, truncated = false },
                limits = new { symbolLimit = 20, impactLimit = 20 },
                truncated = false,
                warnings = Array.Empty<string>(),
                nextActions = new[]
                {
                    new { command = "context-pack", query = "Sample finding" }
                }
            });

        output["navlynVersion"] = "<version>";

        Assert.Equal(ReadSnapshot("workflow-compact-envelope.json"), NormalizeJson(output));
    }

    [Fact]
    public void McpToolResultEnvelope_MatchesGoldenSnapshot()
    {
        using JsonDocument resultDocument = JsonDocument.Parse("""
            {
              "command": "find",
              "nextActions": [
                {
                  "command": "symbol-source",
                  "file": "Navlyn.Core/Symbols/SymbolSourceResolver.cs",
                  "line": 12,
                  "column": 1
                },
                {
                  "command": "related-files",
                  "symbol": "Navlyn.Symbols.SymbolSourceResolver"
                }
              ]
            }
            """);
        NavlynToolResult result = NavlynToolResult.Succeeded(
            "navlyn_find_symbol",
            new NavlynSourceCommand("find", ["find", "--workspace", "navlyn.slnx", "--query", "SymbolSourceResolver"]),
            "navlyn.slnx",
            resultDocument.RootElement,
            new NavlynToolMetadata(
                ExecutionPath: "direct",
                WorkspaceCacheStatus: "warm",
                WorkspaceCacheHit: true,
                WorkspaceFingerprint: "fingerprint",
                SnapshotId: "fingerprint",
                CostClass: "semantic-lookup"));

        using JsonDocument envelope = JsonDocument.Parse(NavlynToolResultFormatter.ToJson(result));

        Assert.Equal(ReadSnapshot("mcp-tool-result-envelope.json"), NormalizeJson(envelope.RootElement));
    }

    [Fact]
    public void WorkspaceStatusResult_MatchesGoldenSnapshot()
    {
        WorkspaceStatusResult result = new(
            Command: "workspace-status",
            Workspace: new WorkspaceStatusWorkspace(
                Path: "navlyn.slnx",
                FullPath: "D:/repo/navlyn.slnx",
                Kind: "solution",
                ProjectCount: 1,
                DocumentCount: 12,
                Projects:
                [
                    new WorkspaceStatusProject(
                        Name: "Navlyn.Core(net10.0)",
                        Path: "Navlyn.Core/Navlyn.Core.csproj",
                        Language: "C#",
                        AssemblyName: "Navlyn.Core",
                        TargetFramework: "net10.0")
                ]),
            Snapshot: new WorkspaceStatusSnapshot(
                Fingerprint: "workspace-fingerprint",
                SnapshotId: "workspace-fingerprint",
                FreshnessStatus: "fresh",
                DocumentIndexDocumentCount: 12,
                DocumentIndexEstimatedBytes: 2048),
            Cache: new WorkspaceDiskCacheStatus(
                Enabled: true,
                Mode: "on",
                Directory: "D:/repo/.navlyn/cache",
                ManifestPath: "D:/repo/.navlyn/cache/workspace-index.json",
                Status: "stale",
                Source: "navlyn.workspace.json",
                ConfigEnabled: true,
                CurrentFingerprint: "current-fingerprint",
                CachedFingerprint: "cached-fingerprint",
                CreatedUtc: "2026-07-04T00:00:00.0000000+00:00",
                WriteRequested: false,
                ClearRequested: false,
                StaleReasons: ["tracked workspace files or version fingerprints changed"],
                CandidateRecordsStored: false,
                CandidateRecordsReason: "Candidate ids are session-local and are not persisted until a stable cross-process candidate contract is available.",
                CachedProjectCount: 1,
                CachedDocumentCount: 12,
                CachedDeclarationCount: 34),
            Versions: new WorkspaceStatusVersions(
                NavlynVersion: "<version>",
                RoslynVersion: "5.3.0",
                DotNetRuntimeVersion: "10.0.0"));

        Assert.Equal(ReadSnapshot("workspace-status-result.json"), NormalizeJson(JsonSerializer.SerializeToElement(result, JsonOptions)));
    }

    [Fact]
    public void SymbolSearchMetadata_MatchesGoldenSnapshot()
    {
        SymbolNavigationSearchMetadata metadata = new(
            Scope: "dependent-projects",
            CostClass: "workspace",
            Partial: true,
            CandidateProjectCount: 4,
            SearchedProjectCount: 2,
            CandidateDocumentCount: 120,
            PrefilteredDocumentCount: 40,
            SearchedDocumentCount: 10,
            LexicalPrefilterApplied: true,
            MaxDocuments: 10,
            TruncationReason: "document-budget",
            NextScope: "solution",
            RerunHints:
            [
                "Increase --max-documents above 10 to search more lexically matching documents.",
                "Rerun with --scope solution for the broadest search."
            ]);

        Assert.Equal(ReadSnapshot("symbol-search-metadata.json"), NormalizeJson(JsonSerializer.SerializeToElement(metadata, JsonOptions)));
    }

    [Fact]
    public void McpToolProfiles_MatchGoldenSnapshot()
    {
        var profiles = new
        {
            reader = NavlynMcpToolProfilePolicy.GetToolNames(NavlynMcpToolProfile.Reader),
            review = NavlynMcpToolProfilePolicy.GetToolNames(NavlynMcpToolProfile.Review),
            edit = NavlynMcpToolProfilePolicy.GetToolNames(NavlynMcpToolProfile.Edit),
            full = NavlynMcpToolProfilePolicy.GetToolNames(NavlynMcpToolProfile.Full)
        };

        Assert.Equal(ReadSnapshot("mcp-tool-profiles.json"), NormalizeJson(JsonSerializer.SerializeToElement(profiles, JsonOptions)));
    }

    [Fact]
    public void FileFirstCommandResults_MatchGoldenSnapshots()
    {
        JsonObject outline = new()
        {
            ["file"] = "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
            ["entries"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "CheckCommand",
                    ["kind"] = "NamedType",
                    ["container"] = "Navlyn.Cli.Commands",
                    ["facts"] = SampleFacts("CheckCommand", "NamedType"),
                    ["candidateId"] = "sym:v1:0123456789abcdef",
                    ["path"] = "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
                    ["line"] = 6,
                    ["column"] = 23,
                    ["endLine"] = 31,
                    ["endColumn"] = 2
                }
            }
        };

        JsonObject symbolSource = new()
        {
            ["file"] = "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
            ["line"] = 6,
            ["column"] = 23,
            ["selectionInput"] = new JsonObject
            {
                ["mode"] = "candidateId",
                ["candidateId"] = "sym:v1:0123456789abcdef"
            },
            ["view"] = "declaration",
            ["limits"] = new JsonObject
            {
                ["maxLines"] = 80,
                ["budgetTokens"] = 4000
            },
            ["symbol"] = new JsonObject
            {
                ["name"] = "CheckCommand",
                ["kind"] = "NamedType",
                ["container"] = "Navlyn.Cli.Commands",
                ["facts"] = SampleFacts("CheckCommand", "NamedType")
            },
            ["slices"] = new JsonArray
            {
                new JsonObject
                {
                    ["kind"] = "declaration",
                    ["path"] = "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
                    ["startLine"] = 6,
                    ["endLine"] = 31,
                    ["text"] = "internal static class CheckCommand",
                    ["truncated"] = false
                }
            },
            ["truncated"] = false,
            ["warnings"] = new JsonArray()
        };

        Assert.Equal(ReadSnapshot("file-outline-result.json"), NormalizeJson(outline));
        Assert.Equal(ReadSnapshot("symbol-source-result.json"), NormalizeJson(symbolSource));
    }

    [Fact]
    public void ResolveTargetResult_MatchesGoldenSnapshot()
    {
        JsonObject result = new()
        {
            ["workspace"] = "navlyn.slnx",
            ["kind"] = "solution",
            ["command"] = "resolve-target",
            ["selectionInput"] = new JsonObject
            {
                ["mode"] = "query",
                ["query"] = "CheckCommand"
            },
            ["selectedTarget"] = new JsonObject
            {
                ["name"] = "CheckCommand",
                ["kind"] = "NamedType",
                ["container"] = "Navlyn.Cli.Commands",
                ["facts"] = SampleFacts("CheckCommand", "NamedType"),
                ["path"] = "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
                ["line"] = 6,
                ["column"] = 23,
                ["endLine"] = 31,
                ["endColumn"] = 2
            },
            ["candidateId"] = "sym:v1:0123456789abcdef",
            ["confidence"] = "high",
            ["candidateCount"] = 1,
            ["totalCandidates"] = 1,
            ["recommendedNextActions"] = new JsonArray
            {
                new JsonObject
                {
                    ["command"] = "symbol-source",
                    ["workspace"] = "navlyn.slnx",
                    ["candidateId"] = "sym:v1:0123456789abcdef",
                    ["reason"] = "inspect-selected-source"
                }
            },
            ["warnings"] = new JsonArray()
        };

        Assert.Equal(ReadSnapshot("resolve-target-result.json"), NormalizeJson(result));
    }

    [Fact]
    public void AgentEvidenceResults_MatchGoldenSnapshots()
    {
        JsonObject anchor = SampleAnchor();
        JsonObject preflight = new()
        {
            ["schemaVersion"] = "navlyn.edit-preflight.v1",
            ["workspace"] = "navlyn.slnx",
            ["kind"] = "solution",
            ["command"] = "edit-preflight",
            ["intent"] = new JsonObject
            {
                ["goal"] = "modify",
                ["changeKind"] = "behavior"
            },
            ["anchor"] = SampleAnchor(),
            ["confidence"] = new JsonObject
            {
                ["overall"] = "high",
                ["evidence"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["kind"] = "target-selected",
                        ["effect"] = "raises",
                        ["confidence"] = "high",
                        ["reasonCodes"] = new JsonArray { "selected-target-present" }
                    }
                }
            },
            ["source"] = new JsonObject
            {
                ["status"] = "ok"
            },
            ["context"] = null,
            ["tests"] = null,
            ["risk"] = new JsonObject
            {
                ["level"] = "low",
                ["reasonCodes"] = new JsonArray { "runtime-behavior-not-proven" }
            },
            ["knownUnknowns"] = new JsonArray { "runtime-behavior-not-proven" },
            ["limitations"] = new JsonArray { "Navlyn reports static source evidence only; it does not prove runtime behavior." },
            ["nextCommands"] = new JsonArray
            {
                new JsonObject
                {
                    ["command"] = "post-edit-guard",
                    ["arguments"] = new JsonArray { "--candidate-id", "sym:v1:0123456789abcdef" },
                    ["reason"] = "Run after editing to confirm the dirty diff still matches the anchor."
                }
            },
            ["stopConditions"] = new JsonArray { "After editing, run post-edit-guard against this preflight anchor before widening scope." },
            ["commandsRun"] = new JsonArray
            {
                new JsonObject
                {
                    ["command"] = "resolve-target",
                    ["arguments"] = new JsonArray { "--query", "CheckCommand" }
                }
            }
        };

        JsonObject guard = new()
        {
            ["schemaVersion"] = "navlyn.agent-guard.v1",
            ["workspace"] = "navlyn.slnx",
            ["kind"] = "solution",
            ["command"] = "post-edit-guard",
            ["ok"] = true,
            ["anchor"] = anchor,
            ["diff"] = new JsonObject
            {
                ["mode"] = "working-tree"
            },
            ["changedSymbols"] = new JsonObject
            {
                ["totalSymbols"] = 1,
                ["symbols"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "CheckCommand",
                        ["kind"] = "NamedType",
                        ["container"] = "Navlyn.Cli.Commands",
                        ["path"] = "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
                        ["line"] = 6,
                        ["facts"] = new JsonObject
                        {
                            ["project"] = "Navlyn.CommandLine(net10.0)"
                        }
                    }
                }
            },
            ["projectFilters"] = null,
            ["scores"] = new JsonArray
            {
                new JsonObject
                {
                    ["score"] = 1,
                    ["symbol"] = new JsonObject
                    {
                        ["name"] = "CheckCommand",
                        ["kind"] = "NamedType"
                    },
                    ["matches"] = new JsonArray { "name", "kind", "container", "path", "project" },
                    ["mismatches"] = new JsonArray()
                }
            },
            ["bestScore"] = 1,
            ["risk"] = "low",
            ["reasonCodes"] = new JsonArray { "matched-container", "matched-kind", "matched-name", "matched-path", "matched-project" },
            ["policy"] = new JsonObject
            {
                ["failOnRisk"] = "high",
                ["passed"] = true
            },
            ["warnings"] = new JsonArray(),
            ["recommendedAction"] = "Continue, then run focused tests related to the changed symbol.",
            ["proofBoundary"] = "Static source diff comparison only; generated/runtime/reflection behavior is outside this guard."
        };

        Assert.Equal(ReadSnapshot("edit-preflight-result.json"), NormalizeJson(preflight));
        Assert.Equal(ReadSnapshot("agent-guard-result.json"), NormalizeJson(guard));
    }

    private static string ReadSnapshot(string fileName)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "navlyn.Tests", "Contracts", "GoldenSnapshots", fileName))
            .ReplaceLineEndings("\n")
            .Trim();
    }

    private static string NormalizeJson(JsonNode node)
    {
        return node.ToJsonString(JsonOptions).ReplaceLineEndings("\n").Trim();
    }

    private static string NormalizeJson(JsonElement element)
    {
        return JsonSerializer.Serialize(element, JsonOptions).ReplaceLineEndings("\n").Trim();
    }

    private static JsonObject SampleFacts(string name, string kind)
    {
        return new JsonObject
        {
            ["displayName"] = name,
            ["fullyQualifiedName"] = $"Navlyn.Cli.Commands.{name}",
            ["kind"] = kind,
            ["isSource"] = true,
            ["isMetadata"] = false
        };
    }

    private static JsonObject SampleAnchor()
    {
        return new JsonObject
        {
            ["candidateId"] = "sym:v1:0123456789abcdef",
            ["name"] = "CheckCommand",
            ["kind"] = "NamedType",
            ["container"] = "Navlyn.Cli.Commands",
            ["project"] = "Navlyn.CommandLine(net10.0)",
            ["path"] = "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
            ["line"] = 6,
            ["column"] = 23,
            ["endLine"] = 31,
            ["endColumn"] = 2,
            ["selectedTarget"] = null,
            ["confidence"] = "high",
            ["ambiguityReason"] = null
        };
    }

    private static LoadedWorkspace CreateWorkspace(MSBuildWorkspace workspace)
    {
        return new LoadedWorkspace(
            FullPath: "D:/repo/navlyn.slnx",
            DisplayPath: "navlyn.slnx",
            Kind: "solution",
            Workspace: workspace,
            Solution: workspace.CurrentSolution,
            Projects: []);
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
