using System.CommandLine;
using Microsoft.CodeAnalysis;
using Navlyn.Cli.OutputProfiles;
using Navlyn.ContextPacks;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class ContextPackCommand
{
    private const int DefaultBudgetTokens = 8000;
    private const int DefaultItemLimit = 80;
    private const int DefaultCandidateLimit = 20;
    private const int DefaultMemberLimit = 50;
    private const int DefaultReferenceLimit = 100;
    private const int DefaultRelationLimit = 25;
    private const int DefaultFileLimit = 50;
    private const int DefaultQueryDiagnosticLimit = 50;
    private const int DefaultSymbolLimit = 50;
    private const int DefaultImpactLimit = 100;
    private const int DefaultDiffDiagnosticLimit = 100;
    private const int DefaultRelatedTestLimit = 50;
    private const int DefaultDepth = 2;
    private const int DefaultSnippetLines = 1;

    public static Command Create()
    {
        Option<string?> queryOption = new("--query")
        {
            Description = "Symbol name query for query-mode context packs."
        };
        Option<string?> candidateIdOption = FuzzyCommandSupport.CreateCandidateIdOption();
        Option<bool> diffOption = new("--diff")
        {
            Description = "Use diff mode and create context from changed symbols and review facts."
        };
        Option<string[]> assumeKindOption = FuzzyCommandSupport.CreateAssumeKindOption();
        Option<string> matchOption = FuzzyCommandSupport.CreateMatchOption();
        Option<bool> caseSensitiveOption = SharedOptions.CreateCaseSensitiveOption();
        Option<string[]> projectOption = SharedOptions.CreateProjectFiltersOption();
        Option<bool> excludeGeneratedOption = SharedOptions.CreateExcludeGeneratedOption();
        Option<string?> goalOption = CreateGoalOption();
        Option<string?> changeKindOption = CreateChangeKindOption();
        Option<int?> budgetTokensOption = new("--budget-tokens")
        {
            Description = $"Approximate context material budget. Defaults to {DefaultBudgetTokens}."
        };
        Option<int?> itemLimitOption = new("--item-limit")
        {
            Description = $"Maximum number of ranked context items to return. Defaults to {DefaultItemLimit}."
        };
        Option<string> snippetPolicyOption = CreateSnippetPolicyOption();
        Option<int?> snippetLinesOption = FuzzyCommandSupport.CreateSnippetLinesOption();
        Option<int?> candidateLimitOption = new("--candidate-limit")
        {
            Description = $"Maximum number of fuzzy candidates to return. Defaults to {DefaultCandidateLimit}."
        };
        Option<int?> memberLimitOption = new("--member-limit")
        {
            Description = $"Maximum number of member outline entries to return. Defaults to {DefaultMemberLimit}."
        };
        Option<int?> referenceLimitOption = new("--reference-limit")
        {
            Description = $"Maximum number of reference locations to return. Defaults to {DefaultReferenceLimit}."
        };
        Option<int?> relationLimitOption = new("--relation-limit")
        {
            Description = $"Maximum number of relation entries per relation kind to return. Defaults to {DefaultRelationLimit}."
        };
        Option<int?> fileLimitOption = new("--file-limit")
        {
            Description = $"Maximum number of related files to return. Defaults to {DefaultFileLimit}."
        };
        Option<int?> diagnosticLimitOption = new("--diagnostic-limit")
        {
            Description = $"Maximum number of diagnostics to return. Defaults to {DefaultQueryDiagnosticLimit} in query mode and {DefaultDiffDiagnosticLimit} in diff mode."
        };
        Option<int?> symbolLimitOption = DiffCommandSupport.CreateSymbolLimitOption(DefaultSymbolLimit);
        Option<int?> impactLimitOption = DiffCommandSupport.CreateImpactLimitOption(DefaultImpactLimit);
        Option<int?> relatedTestLimitOption = DiffCommandSupport.CreateRelatedTestLimitOption(DefaultRelatedTestLimit);
        Option<int?> depthOption = DiffCommandSupport.CreateDepthOption(DefaultDepth);
        Option<string?> baseOption = DiffCommandSupport.CreateBaseOption();
        Option<string?> headOption = DiffCommandSupport.CreateHeadOption();
        Option<bool> stagedOption = DiffCommandSupport.CreateStagedOption();
        Option<bool> includeUnstagedOption = DiffCommandSupport.CreateIncludeUnstagedOption();
        Option<string> candidatePolicyOption = FuzzyCommandSupport.CreateCandidatePolicyOption("fail");
        Option<string> minConfidenceOption = FuzzyCommandSupport.CreateMinConfidenceOption("medium");
        Option<bool> explainSelectionOption = FuzzyCommandSupport.CreateExplainSelectionOption();
        Option<string> profileOption = OutputProfile.CreateOption();

        return WorkspaceCommand.Create(
            "context-pack",
            "Create a deterministic bounded facts pack for an agent investigation.",
            [
                queryOption,
                candidateIdOption,
                diffOption,
                assumeKindOption,
                matchOption,
                caseSensitiveOption,
                projectOption,
                excludeGeneratedOption,
                goalOption,
                changeKindOption,
                budgetTokensOption,
                itemLimitOption,
                snippetPolicyOption,
                snippetLinesOption,
                candidateLimitOption,
                memberLimitOption,
                referenceLimitOption,
                relationLimitOption,
                fileLimitOption,
                diagnosticLimitOption,
                symbolLimitOption,
                impactLimitOption,
                relatedTestLimitOption,
                depthOption,
                baseOption,
                headOption,
                stagedOption,
                includeUnstagedOption,
                candidatePolicyOption,
                minConfidenceOption,
                explainSelectionOption,
                profileOption
            ],
            (workspace, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                parseResult.GetValue(queryOption),
                parseResult.GetValue(candidateIdOption),
                parseResult.GetValue(diffOption),
                parseResult.GetValue(assumeKindOption) ?? [],
                parseResult.GetValue(matchOption)!,
                parseResult.GetValue(caseSensitiveOption),
                parseResult.GetValue(projectOption) ?? [],
                parseResult.GetValue(excludeGeneratedOption),
                parseResult.GetValue(goalOption),
                parseResult.GetValue(changeKindOption),
                parseResult.GetValue(budgetTokensOption),
                parseResult.GetValue(itemLimitOption),
                parseResult.GetValue(snippetPolicyOption)!,
                parseResult.GetValue(snippetLinesOption),
                parseResult.GetValue(candidateLimitOption),
                parseResult.GetValue(memberLimitOption),
                parseResult.GetValue(referenceLimitOption),
                parseResult.GetValue(relationLimitOption),
                parseResult.GetValue(fileLimitOption),
                parseResult.GetValue(diagnosticLimitOption),
                parseResult.GetValue(symbolLimitOption),
                parseResult.GetValue(impactLimitOption),
                parseResult.GetValue(relatedTestLimitOption),
                parseResult.GetValue(depthOption),
                parseResult.GetValue(baseOption),
                parseResult.GetValue(headOption),
                parseResult.GetValue(stagedOption),
                parseResult.GetValue(includeUnstagedOption),
                parseResult.Tokens.Any(token => token.Value == "--include-unstaged"),
                parseResult.GetValue(candidatePolicyOption)!,
                parseResult.GetValue(minConfidenceOption)!,
                parseResult.GetValue(explainSelectionOption),
                parseResult.GetValue(profileOption)!,
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace workspace,
        string? query,
        string? candidateId,
        bool diff,
        IReadOnlyList<string> assumeKinds,
        string match,
        bool caseSensitive,
        IReadOnlyList<string> projectFilters,
        bool excludeGenerated,
        string? goal,
        string? changeKind,
        int? budgetTokens,
        int? itemLimit,
        string snippetPolicy,
        int? snippetLines,
        int? candidateLimit,
        int? memberLimit,
        int? referenceLimit,
        int? relationLimit,
        int? fileLimit,
        int? diagnosticLimit,
        int? symbolLimit,
        int? impactLimit,
        int? relatedTestLimit,
        int? depth,
        string? baseRef,
        string? headRef,
        bool staged,
        bool includeUnstaged,
        bool includeUnstagedSpecified,
        string candidatePolicy,
        string minConfidence,
        bool explainSelection,
        string profile,
        CancellationToken cancellationToken)
    {
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        bool hasCandidateId = !string.IsNullOrWhiteSpace(candidateId);
        if ((hasQuery || hasCandidateId) == diff || (hasQuery && hasCandidateId))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.ParseError, "Specify exactly one context-pack input mode: --query, --candidate-id, or --diff.");
            return ExitCodes.UsageError;
        }

        if (!diff && (!string.IsNullOrWhiteSpace(baseRef) || !string.IsNullOrWhiteSpace(headRef) || staged || includeUnstagedSpecified))
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidDiffOptions, "Diff options require --diff.");
            return ExitCodes.UsageError;
        }

        ContextPackOptions options = CreateOptions(
            goal ?? (diff ? "review" : "understand"),
            changeKind,
            budgetTokens,
            itemLimit,
            snippetPolicy,
            snippetLines,
            candidateLimit,
            memberLimit,
            referenceLimit,
            relationLimit,
            fileLimit,
            diagnosticLimit,
            symbolLimit,
            impactLimit,
            relatedTestLimit,
            depth);

        if (!ValidateOptions(options, diff, out int exitCode))
        {
            return exitCode;
        }

        ContextPackResolver resolver = new();
        if (!diff)
        {
            if (!FuzzyCommandSupport.TryCreateSelection(
                workspace,
                query,
                candidateId,
                assumeKinds,
                match,
                caseSensitive,
                projectFilters,
                excludeGenerated,
                options.CandidateLimit,
                candidatePolicy,
                minConfidence,
                explainSelection,
                allowGroupPolicy: false,
                out FuzzyQueryOptions queryOptions,
                out IReadOnlyList<Project> projects,
                out IReadOnlyList<FuzzyProjectFilter>? projectOutputs,
                out exitCode))
            {
                return exitCode;
            }

            FuzzyDiscoveryResolver fuzzyResolver = new();
            if (!await FuzzyCommandSupport.TryValidateSelectionAsync(fuzzyResolver, projects, queryOptions, cancellationToken))
            {
                return ExitCodes.UsageError;
            }

            ContextPackResult result = await resolver.ResolveQueryAsync(
                workspace,
                queryOptions,
                projects,
                projectOutputs,
                excludeGenerated,
                options,
                cancellationToken);
            ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "context-pack", profile, result, new
            {
                mode = hasQuery ? "query" : "candidateId",
                goal = options.Goal,
                changeKind = options.ChangeKind,
                projectFilters,
                excludeGenerated,
                options
            }));
            return ExitCodes.Success;
        }

        if (!DiffCommandSupport.TryCreateRequest(baseRef, headRef, staged, includeUnstaged, out DiffRequest request, out exitCode) ||
            !DiffCommandSupport.TryCreateProjectContext(workspace, projectFilters, out IReadOnlyList<Project> diffProjects, out IReadOnlyList<DiffProjectFilter>? diffProjectOutputs, out exitCode))
        {
            return exitCode;
        }

        DiffWorkflowExecutionResult<ContextPackResult> diffResult = await resolver.ResolveDiffAsync(
            workspace,
            request,
            diffProjects,
            diffProjectOutputs,
            excludeGenerated,
            options,
            cancellationToken);

        if (diffResult.Error is not null)
        {
            return DiffCommandSupport.WriteError(diffResult.Error);
        }

        ConsoleJsonWriter.Write(OutputProfile.Format(workspace, "context-pack", profile, diffResult.Result!, new
        {
            mode = "diff",
            baseRef,
            headRef,
                staged,
                includeUnstaged,
                changeKind = options.ChangeKind,
                projectFilters,
            excludeGenerated,
            options
        }));
        return ExitCodes.Success;
    }

    private static Option<string?> CreateGoalOption()
    {
        Option<string?> option = new("--goal")
        {
            Description = "Context ranking profile: review, modify, or understand."
        };

        option.AcceptOnlyFromAmong("review", "modify", "understand");
        return option;
    }

    private static Option<string?> CreateChangeKindOption()
    {
        Option<string?> option = new("--change-kind")
        {
            Description = "Context ranking hint for modify workflows: behavior, signature, rename, constructor, nullability, async, public-api, di-registration, or endpoint."
        };

        option.AcceptOnlyFromAmong("behavior", "signature", "rename", "constructor", "nullability", "async", "public-api", "di-registration", "endpoint");
        return option;
    }

    private static Option<string> CreateSnippetPolicyOption()
    {
        Option<string> option = new("--snippet-policy")
        {
            Description = "Snippet inclusion policy: none, signature, line, or block.",
            DefaultValueFactory = _ => "line"
        };

        option.AcceptOnlyFromAmong("none", "signature", "line", "block");
        return option;
    }

    private static ContextPackOptions CreateOptions(
        string goal,
        string? changeKind,
        int? budgetTokens,
        int? itemLimit,
        string snippetPolicy,
        int? snippetLines,
        int? candidateLimit,
        int? memberLimit,
        int? referenceLimit,
        int? relationLimit,
        int? fileLimit,
        int? diagnosticLimit,
        int? symbolLimit,
        int? impactLimit,
        int? relatedTestLimit,
        int? depth)
    {
        return new ContextPackOptions(
            Goal: goal,
            BudgetTokens: budgetTokens ?? DefaultBudgetTokens,
            ItemLimit: itemLimit ?? DefaultItemLimit,
            SnippetPolicy: snippetPolicy,
            SnippetLines: snippetLines ?? DefaultSnippetLines,
            CandidateLimit: candidateLimit ?? DefaultCandidateLimit,
            MemberLimit: memberLimit ?? DefaultMemberLimit,
            ReferenceLimit: referenceLimit ?? DefaultReferenceLimit,
            RelationLimit: relationLimit ?? DefaultRelationLimit,
            FileLimit: fileLimit ?? DefaultFileLimit,
            QueryDiagnosticLimit: diagnosticLimit ?? DefaultQueryDiagnosticLimit,
            SymbolLimit: symbolLimit ?? DefaultSymbolLimit,
            ImpactLimit: impactLimit ?? DefaultImpactLimit,
            DiffDiagnosticLimit: diagnosticLimit ?? DefaultDiffDiagnosticLimit,
            RelatedTestLimit: relatedTestLimit ?? DefaultRelatedTestLimit,
            Depth: depth ?? DefaultDepth,
            ChangeKind: changeKind);
    }

    private static bool ValidateOptions(ContextPackOptions options, bool diff, out int exitCode)
    {
        return FuzzyCommandSupport.TryCreatePositiveOption("--budget-tokens", options.BudgetTokens, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--item-limit", options.ItemLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreateNonNegativeOption("--snippet-lines", options.SnippetLines, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--candidate-limit", options.CandidateLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--member-limit", options.MemberLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--reference-limit", options.ReferenceLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--relation-limit", options.RelationLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--file-limit", options.FileLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--diagnostic-limit", diff ? options.DiffDiagnosticLimit : options.QueryDiagnosticLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--symbol-limit", options.SymbolLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--impact-limit", options.ImpactLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreatePositiveOption("--related-test-limit", options.RelatedTestLimit, out exitCode) &&
            FuzzyCommandSupport.TryCreateNonNegativeOption("--depth", options.Depth, out exitCode);
    }
}
