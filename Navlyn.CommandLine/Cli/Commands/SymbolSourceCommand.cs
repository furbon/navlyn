using System.CommandLine;
using System.Text.Json.Serialization;
using Navlyn.Diagnostics;
using Navlyn.Symbols;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static class SymbolSourceCommand
{
    private const int DefaultMaxLines = 80;
    private const int DefaultBudgetTokens = 4000;

    public static Command Create()
    {
        Option<string> viewOption = CreateViewOption();
        Option<int?> maxLinesOption = new("--max-lines")
        {
            Description = $"Maximum source lines per slice. Defaults to {DefaultMaxLines}."
        };
        Option<int?> budgetTokensOption = new("--budget-tokens")
        {
            Description = $"Approximate character budget per slice. Defaults to {DefaultBudgetTokens} tokens."
        };

        return SourcePositionCommand.Create(
            "symbol-source",
            "Return bounded source slices for the C# symbol at a source position.",
            [viewOption, maxLinesOption, budgetTokensOption],
            (workspace, options, parseResult, cancellationToken) => ExecuteAsync(
                workspace,
                options,
                parseResult.GetValue(viewOption)!,
                parseResult.GetValue(maxLinesOption),
                parseResult.GetValue(budgetTokensOption),
                cancellationToken));
    }

    private static async Task<int> ExecuteAsync(
        LoadedWorkspace loadedWorkspace,
        SourcePositionOptions sourceOptions,
        string view,
        int? maxLines,
        int? budgetTokens,
        CancellationToken cancellationToken)
    {
        int effectiveMaxLines = maxLines ?? DefaultMaxLines;
        int effectiveBudgetTokens = budgetTokens ?? DefaultBudgetTokens;
        if (effectiveMaxLines <= 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "--max-lines must be 1 or greater.");
            return ExitCodes.UsageError;
        }

        if (effectiveBudgetTokens <= 0)
        {
            DiagnosticReporter.WriteError(DiagnosticIds.InvalidLimit, "--budget-tokens must be 1 or greater.");
            return ExitCodes.UsageError;
        }

        SymbolSourceResolutionResult result = await new SymbolSourceResolver().ResolveAsync(
            loadedWorkspace.Solution,
            sourceOptions.File,
            sourceOptions.Line,
            sourceOptions.Column,
            sourceOptions.Project,
            sourceOptions.ExcludeGenerated,
            new SymbolSourceOptions(view, effectiveMaxLines, effectiveBudgetTokens),
            cancellationToken);

        if (result.Error is not null)
        {
            DiagnosticReporter.WriteError(result.Error.DiagnosticId, result.Error.Message);
            return result.Error.ExitCode;
        }

        SymbolSourceResolution resolution = result.Resolution!;
        ConsoleJsonWriter.Write(new SymbolSourceResult(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Project: sourceOptions.ProjectFilter is null ? null : ProjectFilterOutput.FromAppliedFilter(sourceOptions.ProjectFilter),
            SelectionInput: sourceOptions.SelectionInput,
            ExcludeGenerated: sourceOptions.ExcludeGenerated,
            View: resolution.View,
            Limits: resolution.Limits,
            Symbol: resolution.Symbol,
            Slices: resolution.Slices,
            Truncated: resolution.Truncated,
            Warnings: resolution.Warnings));

        return ExitCodes.Success;
    }

    private static Option<string> CreateViewOption()
    {
        Option<string> option = new("--view")
        {
            Description = "Source view: signature, declaration, body, members, xml-doc, or attributes.",
            DefaultValueFactory = _ => "declaration"
        };

        option.AcceptOnlyFromAmong("signature", "declaration", "body", "members", "xml-doc", "attributes");
        return option;
    }

    private sealed record SymbolSourceResult(
        string File,
        int Line,
        int Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ProjectFilterOutput? Project,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CandidateSelectionInput? SelectionInput,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool ExcludeGenerated,
        string View,
        SymbolSourceLimits Limits,
        SymbolSourceSymbol Symbol,
        IReadOnlyList<SymbolSourceSlice> Slices,
        bool Truncated,
        IReadOnlyList<string> Warnings);
}
