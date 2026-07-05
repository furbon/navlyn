using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Navlyn.Diagnostics;

namespace Navlyn.Symbols;

internal sealed class CallHierarchyResolver
{
    public async Task<CallersResolutionResult> ResolveCallersAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        return await ResolveCallersAsync(
            solution,
            file,
            line,
            column,
            project,
            excludeGenerated,
            SymbolNavigationSearchOptions.Default,
            cancellationToken);
    }

    public async Task<CallersResolutionResult> ResolveCallersAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        SymbolNavigationSearchOptions searchOptions,
        CancellationToken cancellationToken)
    {
        SourceSymbolResolutionResult result = await new SourceSymbolResolver().ResolveAsync(
            solution,
            file,
            line,
            column,
            project,
            excludeGenerated,
            cancellationToken);

        if (result.Error is not null)
        {
            return CallersResolutionResult.Failed(result.Error);
        }

        SourceSymbolResolution resolution = result.Resolution!;
        SymbolNavigationSearchPlan searchPlan = await SymbolNavigationSearchPlanner.CreateAsync(
            solution,
            resolution,
            searchOptions,
            excludeGenerated,
            cancellationToken);

        IReadOnlyList<ISymbol> targetSymbols = await FindRelatedTargetSymbolsAsync(
            solution,
            resolution.Symbol,
            searchPlan.Projects,
            cancellationToken);

        List<CallHierarchyGroup> groups = [];
        foreach (ISymbol targetSymbol in targetSymbols)
        {
            IEnumerable<SymbolCallerInfo> callers = await SymbolFinder.FindCallersAsync(
                targetSymbol,
                solution,
                searchPlan.Documents,
                cancellationToken);

            foreach (SymbolCallerInfo caller in callers)
            {
                CallHierarchySymbol? callerSymbol = CreateSymbol(caller.CallingSymbol, excludeGenerated);
                if (callerSymbol is null)
                {
                    continue;
                }

                IReadOnlyList<CallHierarchyLocation> locations = [.. caller.Locations
                    .Select(location => CreateLocation(location, excludeGenerated))
                    .OfType<CallHierarchyLocation>()
                    .Distinct()
                    .OrderBy(location => location.Path, StringComparer.Ordinal)
                    .ThenBy(location => location.Line)
                    .ThenBy(location => location.Column)];

                if (locations.Count == 0)
                {
                    continue;
                }

                groups.Add(new CallHierarchyGroup(callerSymbol, locations));
            }
        }

        IReadOnlyList<CallHierarchyGroup> mergedGroups = MergeGroups(groups);

        return CallersResolutionResult.Succeeded(new CallersResolution(
            File: resolution.File,
            Line: resolution.Line,
            Column: resolution.Column,
            Symbol: CreateInputSymbol(resolution.Symbol),
            Callers: mergedGroups,
            Search: searchPlan.Metadata));
    }

    public async Task<CallsResolutionResult> ResolveCallsAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        bool includeMetadata,
        CancellationToken cancellationToken)
    {
        SourceDocumentResolutionResult documentResult =
            await new SourceDocumentResolver().ResolveAsync(solution, file, project, excludeGenerated, cancellationToken);

        if (documentResult.Error is not null)
        {
            return CallsResolutionResult.Failed(documentResult.Error);
        }

        SourceDocumentResolution sourceDocument = documentResult.Resolution!;
        if (!TryGetPosition(sourceDocument.Text, line, column, out int position, out string? positionError))
        {
            return CallsResolutionResult.Failed(
                DiagnosticIds.InvalidSourcePosition,
                positionError!,
                ExitCodes.UsageError);
        }

        SyntaxNode? root = await sourceDocument.Document.GetSyntaxRootAsync(cancellationToken);
        SemanticModel? semanticModel = await sourceDocument.Document.GetSemanticModelAsync(cancellationToken);
        if (root is null || semanticModel is null)
        {
            return CallsResolutionResult.Failed(
                DiagnosticIds.SymbolNotFoundAtPosition,
                $"No containing source member found at {sourceDocument.DisplayPath}:{line}:{column}.",
                ExitCodes.UsageError);
        }

        SyntaxToken token = root.FindToken(position);
        ISymbol? callerSymbol = SymbolNavigationFacts.ResolveSymbol(semanticModel, token, position, cancellationToken);
        if (!IsCallableContainer(callerSymbol))
        {
            callerSymbol = semanticModel.GetEnclosingSymbol(position, cancellationToken);
        }

        if (callerSymbol is null)
        {
            return CallsResolutionResult.Failed(
                DiagnosticIds.SymbolNotFoundAtPosition,
                $"No containing source member found at {sourceDocument.DisplayPath}:{line}:{column}.",
                ExitCodes.UsageError);
        }

        callerSymbol = SymbolNavigationFacts.NormalizeSourceNavigationSymbol(callerSymbol);

        Location? callerLocation = SymbolNavigationFacts.GetBestSourceLocation(
            callerSymbol,
            root.SyntaxTree,
            position,
            excludeGenerated);

        if (callerLocation is null)
        {
            return CallsResolutionResult.Failed(
                DiagnosticIds.SourceDefinitionNotFound,
                $"No source definition found for containing symbol {callerSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}.",
                ExitCodes.UsageError);
        }

        TextSpan callerSpan = await GetAnalysisSpanAsync(callerSymbol, root.SyntaxTree, callerLocation.SourceSpan, cancellationToken);
        List<CallHierarchyGroup> groups = [];
        foreach (SyntaxNode node in root.DescendantNodes(callerSpan))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!callerSpan.Contains(node.Span))
            {
                continue;
            }

            if (IsInsideNestedSymbol(node, semanticModel, callerSymbol, cancellationToken))
            {
                continue;
            }

            ISymbol? calleeSymbol = ResolveCalleeSymbol(semanticModel, node, cancellationToken);
            if (calleeSymbol is null)
            {
                continue;
            }

            CallHierarchySymbol? callee = CreateSymbol(calleeSymbol, excludeGenerated, includeMetadata);
            CallHierarchyLocation? location = CreateLocation(node.GetLocation(), excludeGenerated);
            if (callee is null || location is null)
            {
                continue;
            }

            groups.Add(new CallHierarchyGroup(callee, [location]));
        }

        return CallsResolutionResult.Succeeded(new CallsResolution(
            File: sourceDocument.DisplayPath,
            Line: line,
            Column: column,
            Caller: CreateInputSymbol(callerSymbol),
            Calls: MergeGroups(groups),
            Search: SymbolNavigationSearchMetadata.Local()));
    }

    private static async Task<IReadOnlyList<ISymbol>> FindRelatedTargetSymbolsAsync(
        Solution solution,
        ISymbol symbol,
        IReadOnlyList<Project> projects,
        CancellationToken cancellationToken)
    {
        List<ISymbol> symbols = [symbol];

        if (symbol is IMethodSymbol method)
        {
            AddOverriddenMethods(method, symbols);
            symbols.AddRange(method.ExplicitInterfaceImplementations);
        }
        else if (symbol is IPropertySymbol property)
        {
            AddOverriddenProperties(property, symbols);
            symbols.AddRange(property.ExplicitInterfaceImplementations);
        }
        else if (symbol is IEventSymbol eventSymbol)
        {
            AddOverriddenEvents(eventSymbol, symbols);
            symbols.AddRange(eventSymbol.ExplicitInterfaceImplementations);
        }

        if (symbol is IMethodSymbol or IPropertySymbol or IEventSymbol)
        {
            ImmutableHashSet<Project> projectSet = projects.ToImmutableHashSet();
            IEnumerable<ISymbol> implementations = await SymbolFinder.FindImplementationsAsync(
                symbol,
                solution,
                projectSet,
                cancellationToken);

            IEnumerable<ISymbol> overrides = await SymbolFinder.FindOverridesAsync(
                symbol,
                solution,
                projectSet,
                cancellationToken);

            symbols.AddRange(implementations);
            symbols.AddRange(overrides);
        }

        return [.. symbols.Distinct(SymbolEqualityComparer.Default)];
    }

    private static bool IsCallableContainer(ISymbol? symbol)
    {
        return symbol is IMethodSymbol or IPropertySymbol or IEventSymbol;
    }

    private static async Task<TextSpan> GetAnalysisSpanAsync(
        ISymbol symbol,
        SyntaxTree syntaxTree,
        TextSpan fallbackSpan,
        CancellationToken cancellationToken)
    {
        foreach (SyntaxReference syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.SyntaxTree != syntaxTree)
            {
                continue;
            }

            SyntaxNode syntax = await syntaxReference.GetSyntaxAsync(cancellationToken);
            return syntax.Span;
        }

        return fallbackSpan;
    }

    private static void AddOverriddenMethods(IMethodSymbol method, List<ISymbol> symbols)
    {
        for (IMethodSymbol? current = method.OverriddenMethod; current is not null; current = current.OverriddenMethod)
        {
            symbols.Add(current);
        }
    }

    private static void AddOverriddenProperties(IPropertySymbol property, List<ISymbol> symbols)
    {
        for (IPropertySymbol? current = property.OverriddenProperty; current is not null; current = current.OverriddenProperty)
        {
            symbols.Add(current);
        }
    }

    private static void AddOverriddenEvents(IEventSymbol eventSymbol, List<ISymbol> symbols)
    {
        for (IEventSymbol? current = eventSymbol.OverriddenEvent; current is not null; current = current.OverriddenEvent)
        {
            symbols.Add(current);
        }
    }

    private static bool IsInsideNestedSymbol(
        SyntaxNode node,
        SemanticModel semanticModel,
        ISymbol callerSymbol,
        CancellationToken cancellationToken)
    {
        ISymbol? enclosingSymbol = semanticModel.GetEnclosingSymbol(node.SpanStart, cancellationToken);
        return enclosingSymbol is not null &&
            !SymbolEqualityComparer.Default.Equals(enclosingSymbol, callerSymbol);
    }

    private static ISymbol? ResolveCalleeSymbol(
        SemanticModel semanticModel,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        IOperation? operation = semanticModel.GetOperation(node, cancellationToken);
        ISymbol? symbol = operation switch
        {
            IInvocationOperation invocation => ResolveInvocationCallee(invocation),
            IObjectCreationOperation creation => creation.Constructor,
            IPropertyReferenceOperation property => property.Property,
            IEventReferenceOperation eventReference => eventReference.Event,
            IMethodReferenceOperation methodReference => methodReference.Method,
            IBinaryOperation binary => binary.OperatorMethod,
            IConversionOperation conversion => conversion.OperatorMethod,
            _ => null
        };

        symbol ??= GetEventOrPropertySymbol(semanticModel.GetSymbolInfo(node, cancellationToken).Symbol);

        return symbol is IMethodSymbol { AssociatedSymbol: not null } method
            ? method.AssociatedSymbol
            : symbol?.OriginalDefinition ?? symbol;
    }

    private static ISymbol ResolveInvocationCallee(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
            TryResolveDelegateInvocationTarget(invocation.Instance) is { } delegateTarget)
        {
            return delegateTarget;
        }

        return invocation.TargetMethod;
    }

    private static ISymbol? TryResolveDelegateInvocationTarget(IOperation? operation)
    {
        return operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation property => property.Property,
            IEventReferenceOperation eventReference => eventReference.Event,
            IMethodReferenceOperation methodReference => methodReference.Method,
            IConversionOperation conversion => TryResolveDelegateInvocationTarget(conversion.Operand),
            _ => null
        };
    }

    private static ISymbol? GetUserDefinedOperator(ISymbol? symbol)
    {
        return symbol is IMethodSymbol
        {
            MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion
        }
            ? symbol
            : null;
    }

    private static ISymbol? GetEventOrPropertySymbol(ISymbol? symbol)
    {
        return symbol is IEventSymbol or IPropertySymbol ? symbol : null;
    }

    private static IReadOnlyList<CallHierarchyGroup> MergeGroups(IReadOnlyList<CallHierarchyGroup> groups)
    {
        return [.. groups
            .GroupBy(group => group.Symbol)
            .Select(group => new CallHierarchyGroup(
                group.Key,
                [.. group
                    .SelectMany(item => item.Locations)
                    .Distinct()
                    .OrderBy(location => location.Path, StringComparer.Ordinal)
                    .ThenBy(location => location.Line)
                    .ThenBy(location => location.Column)
                    .ThenBy(location => location.EndLine)
                    .ThenBy(location => location.EndColumn)]))
            .Where(group => group.Locations.Count > 0)
            .OrderBy(group => group.Symbol.Path, StringComparer.Ordinal)
            .ThenBy(group => group.Symbol.Line)
            .ThenBy(group => group.Symbol.Column)
            .ThenBy(group => group.Symbol.Name, StringComparer.Ordinal)
            .ThenBy(group => group.Symbol.Kind, StringComparer.Ordinal)
            .ThenBy(group => group.Symbol.Container, StringComparer.Ordinal)];
    }

    private static CallHierarchySymbol CreateInputSymbol(ISymbol symbol)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol).FirstOrDefault();
        return new CallHierarchySymbol(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol),
            Path: location?.Path,
            Line: location?.Line,
            Column: location?.Column,
            EndLine: location?.EndLine,
            EndColumn: location?.EndColumn);
    }

    private static CallHierarchySymbol? CreateSymbol(ISymbol symbol, bool excludeGenerated)
    {
        return CreateSymbol(symbol, excludeGenerated, includeMetadata: false);
    }

    private static CallHierarchySymbol? CreateSymbol(ISymbol symbol, bool excludeGenerated, bool includeMetadata)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol, excludeGenerated).FirstOrDefault();
        if (location is null && (!includeMetadata || !symbol.Locations.Any(symbolLocation => symbolLocation.IsInMetadata)))
        {
            return null;
        }

        return new CallHierarchySymbol(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol),
            Path: location?.Path,
            Line: location?.Line,
            Column: location?.Column,
            EndLine: location?.EndLine,
            EndColumn: location?.EndColumn);
    }

    private static CallHierarchyLocation? CreateLocation(Location location, bool excludeGenerated)
    {
        SymbolSourceLocation? sourceLocation = SymbolNavigationFacts.CreateSourceLocation(location, excludeGenerated);

        return sourceLocation is null
            ? null
            : new CallHierarchyLocation(
                Path: sourceLocation.Path,
                Line: sourceLocation.Line,
                Column: sourceLocation.Column,
                EndLine: sourceLocation.EndLine,
                EndColumn: sourceLocation.EndColumn);
    }

    private static bool TryGetPosition(
        SourceText text,
        int line,
        int column,
        out int position,
        out string? error)
    {
        position = 0;
        error = null;

        if (line < 1)
        {
            error = $"Line must be 1 or greater. Actual value: {line}.";
            return false;
        }

        if (column < 1)
        {
            error = $"Column must be 1 or greater. Actual value: {column}.";
            return false;
        }

        if (line > text.Lines.Count)
        {
            error = $"Line {line} is outside the source file. The file has {text.Lines.Count} lines.";
            return false;
        }

        TextLine textLine = text.Lines[line - 1];
        int lineLength = textLine.End - textLine.Start;
        int maxColumn = lineLength + 1;
        if (column > maxColumn)
        {
            error = $"Column {column} is outside line {line}. The maximum column is {maxColumn}.";
            return false;
        }

        position = textLine.Start + column - 1;
        return true;
    }
}

internal sealed record CallersResolutionResult(CallersResolution? Resolution, CallHierarchyResolutionError? Error)
{
    public static CallersResolutionResult Succeeded(CallersResolution resolution)
    {
        return new CallersResolutionResult(resolution, Error: null);
    }

    public static CallersResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new CallersResolutionResult(
            Resolution: null,
            Error: new CallHierarchyResolutionError(diagnosticId, message, exitCode));
    }

    public static CallersResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record CallsResolutionResult(CallsResolution? Resolution, CallHierarchyResolutionError? Error)
{
    public static CallsResolutionResult Succeeded(CallsResolution resolution)
    {
        return new CallsResolutionResult(resolution, Error: null);
    }

    public static CallsResolutionResult Failed(int diagnosticId, string message, int exitCode)
    {
        return new CallsResolutionResult(
            Resolution: null,
            Error: new CallHierarchyResolutionError(diagnosticId, message, exitCode));
    }

    public static CallsResolutionResult Failed(SymbolNavigationError error)
    {
        return Failed(error.DiagnosticId, error.Message, error.ExitCode);
    }
}

internal sealed record CallersResolution(
    string File,
    int Line,
    int Column,
    CallHierarchySymbol Symbol,
    IReadOnlyList<CallHierarchyGroup> Callers,
    SymbolNavigationSearchMetadata Search);

internal sealed record CallsResolution(
    string File,
    int Line,
    int Column,
    CallHierarchySymbol Caller,
    IReadOnlyList<CallHierarchyGroup> Calls,
    SymbolNavigationSearchMetadata Search);

internal sealed record CallHierarchySymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

internal sealed record CallHierarchyGroup(
    CallHierarchySymbol Symbol,
    IReadOnlyList<CallHierarchyLocation> Locations);

internal sealed record CallHierarchyLocation(string Path, int Line, int Column, int EndLine, int EndColumn);

internal sealed record CallHierarchyResolutionError(int DiagnosticId, string Message, int ExitCode);
