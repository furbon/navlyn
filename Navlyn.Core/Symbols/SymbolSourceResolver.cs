using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Navlyn.Diagnostics;
using Navlyn.GeneratedCode;
using Navlyn.Languages;
using Navlyn.Paths;

namespace Navlyn.Symbols;

internal sealed class SymbolSourceResolver
{
    public async Task<SymbolSourceResolutionResult> ResolveAsync(
        Solution solution,
        FileInfo file,
        int line,
        int column,
        Project? project,
        bool excludeGenerated,
        SymbolSourceOptions options,
        CancellationToken cancellationToken)
    {
        SourceSymbolResolutionResult result = await new SourceSymbolResolver().ResolveAsync(
            solution,
            file,
            line,
            column,
            project,
            excludeGenerated,
            cancellationToken,
            requireExactBinding: options.ExternalSource is "metadata" or "decompiled");

        if (result.Error is not null)
        {
            return SymbolSourceResolutionResult.Failed(result.Error);
        }

        SourceSymbolResolution source = result.Resolution!;
        if (options.ExternalSource == "decompiled" && options.View == "body" &&
            source.Symbol is INamedTypeSymbol && source.Symbol.Locations.Any(location => location.IsInMetadata))
        {
            // At `new LibraryType(...)`, a type token normally selects the type. A body
            // request needs the uniquely bound constructor, not a guess from its arguments.
            SourceSymbolResolutionResult constructor = await ResolveExternalConstructorAsync(solution, source, cancellationToken);
            if (constructor.Error is not null) return SymbolSourceResolutionResult.Failed(constructor.Error);
            source = constructor.Resolution!;
        }
        SymbolSourceSymbol symbol = CreateSymbol(source.Symbol, source.ProjectName);
        IReadOnlyList<Location> locations = [.. source.Symbol.Locations
            .Where(location => location.IsInSource && location.GetLineSpan().IsValid)
            .Where(location => !excludeGenerated || !GeneratedCodeFacts.IsGeneratedPath(location.GetLineSpan().Path))
            .OrderBy(location => PathDisplay.FromCurrentDirectory(location.GetLineSpan().Path), StringComparer.Ordinal)
            .ThenBy(location => location.GetLineSpan().StartLinePosition.Line)
            .ThenBy(location => location.GetLineSpan().StartLinePosition.Character)];

        if (locations.Count == 0)
        {
            if (options.ExternalSource is "metadata" or "decompiled")
            {
                if (!source.HasExactBinding)
                {
                    return SymbolSourceResolutionResult.Failed(new SymbolNavigationError(
                        DiagnosticIds.ExternalMemberAmbiguous,
                        "The call-site symbol has no unique Roslyn binding.",
                        ExitCodes.UsageError));
                }

                Project? externalProject = solution.GetProject(source.ProjectId);
                if (externalProject is null)
                {
                    return SymbolSourceResolutionResult.Failed(new SymbolNavigationError(
                        DiagnosticIds.ExternalImplementationUnavailable,
                        "The selected project is no longer available.",
                        ExitCodes.UsageError));
                }

                ISymbol externalSymbol = source.Symbol;
                string externalMode = options.ExternalSource;
                if (source.Symbol is IPropertySymbol property && options.ExternalSource == "decompiled")
                {
                    if (options.View != "body")
                    {
                        // ILSpy emits a bare block for an accessor handle. Non-body property
                        // views come from the bound Roslyn metadata and say so in provenance.
                        externalMode = "metadata";
                    }
                    else
                    {
                        PropertyAccessKind access = ClassifyPropertyAccess(source, cancellationToken);
                        if (access == PropertyAccessKind.Ambiguous)
                        {
                            return SymbolSourceResolutionResult.Failed(new SymbolNavigationError(
                                DiagnosticIds.ExternalMemberAmbiguous,
                                ExternalMemberDiagnosticMessage(DiagnosticIds.ExternalMemberAmbiguous),
                                ExitCodes.UsageError));
                        }

                        externalSymbol = access == PropertyAccessKind.Write
                            ? (ISymbol?)property.SetMethod ?? property
                            : (ISymbol?)property.GetMethod ?? property;
                    }
                }

                ExternalMemberResolutionResult external = await new ExternalMemberSourceResolver().ResolveAsync(
                    externalProject,
                    externalSymbol,
                    options.View,
                    externalMode,
                    options.MaxLines,
                    options.BudgetTokens,
                    cancellationToken);
                if (external.DiagnosticId is int diagnosticId)
                {
                    return SymbolSourceResolutionResult.Failed(new SymbolNavigationError(
                        diagnosticId,
                        ExternalMemberDiagnosticMessage(diagnosticId, externalSymbol),
                        ExitCodes.UsageError));
                }

                ExternalMemberResolution externalResolution = external.Resolution!;
                return SymbolSourceResolutionResult.Succeeded(new SymbolSourceResolution(
                    source.File,
                    source.Line,
                    source.Column,
                    symbol,
                    options.View,
                    new SymbolSourceLimits(options.MaxLines, options.BudgetTokens),
                    externalResolution.Slices,
                    externalResolution.Slices.Any(slice => slice.Truncated),
                    Warnings: [],
                    SourceOrigin: externalResolution.SourceOrigin,
                    ExternalAssembly: externalResolution.ExternalAssembly,
                    ExternalSnapshot: externalResolution.Snapshot));
            }

            IReadOnlyList<string> warnings = source.Symbol.Locations.Any(location => location.IsInMetadata)
                ? ["metadata-only-symbol"]
                : ["source-location-not-found"];
            return SymbolSourceResolutionResult.Succeeded(new SymbolSourceResolution(
                source.File,
                source.Line,
                source.Column,
                symbol,
                options.View,
                new SymbolSourceLimits(options.MaxLines, options.BudgetTokens),
                Slices: [],
                Truncated: false,
                Warnings: warnings));
        }

        IReadOnlyList<SymbolSourceSlice> slices = [.. locations
            .SelectMany(location => CreateSlices(source.Symbol, location, options, cancellationToken))
            .OrderBy(slice => slice.Path, StringComparer.Ordinal)
            .ThenBy(slice => slice.StartLine)
            .ThenBy(slice => slice.StartColumn)
            .ThenBy(slice => slice.TextKind, StringComparer.Ordinal)];

        return SymbolSourceResolutionResult.Succeeded(new SymbolSourceResolution(
            source.File,
            source.Line,
            source.Column,
            symbol,
            options.View,
            new SymbolSourceLimits(options.MaxLines, options.BudgetTokens),
            slices,
            slices.Any(slice => slice.Truncated),
            Warnings: []));
    }

    private static PropertyAccessKind ClassifyPropertyAccess(SourceSymbolResolution source, CancellationToken cancellationToken)
    {
        if (source.SyntaxTree.Options.Language != LanguageNames.CSharp)
        {
            return PropertyAccessKind.Ambiguous;
        }

        SyntaxNode root = source.SyntaxTree.GetRoot(cancellationToken);
        SyntaxToken token = root.FindToken(source.Position);
        SimpleNameSyntax? name = token.Parent?.AncestorsAndSelf().OfType<SimpleNameSyntax>()
            .FirstOrDefault(candidate => candidate.Span.Contains(source.Position));
        if (name is null)
        {
            return PropertyAccessKind.Ambiguous;
        }

        ExpressionSyntax propertyExpression = name.Parent switch
        {
            MemberAccessExpressionSyntax member when member.Name == name => member,
            MemberBindingExpressionSyntax binding when binding.Name == name => binding,
            _ => name
        };

        ExpressionSyntax accessExpression = propertyExpression;
        while (true)
        {
            if (accessExpression.Parent is ParenthesizedExpressionSyntax parenthesized &&
                parenthesized.Expression == accessExpression)
            {
                accessExpression = parenthesized;
            }
            else if (accessExpression.Parent is ConditionalAccessExpressionSyntax conditional &&
                     conditional.WhenNotNull == accessExpression)
            {
                accessExpression = conditional;
            }
            else
            {
                break;
            }
        }

        foreach (SyntaxNode ancestor in accessExpression.Ancestors())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ancestor is AssignmentExpressionSyntax assignment &&
                assignment.Left.Span == accessExpression.Span)
            {
                return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    ? PropertyAccessKind.Write
                    : PropertyAccessKind.Ambiguous;
            }

            if (ancestor is AssignmentExpressionSyntax tupleAssignment &&
                tupleAssignment.Left is TupleExpressionSyntax &&
                tupleAssignment.Left.Span.Contains(propertyExpression.Span))
            {
                return PropertyAccessKind.Ambiguous;
            }

            if (ancestor is ArgumentSyntax argument && argument.Expression.Span.Contains(propertyExpression.Span) &&
                !argument.RefOrOutKeyword.IsKind(SyntaxKind.None))
            {
                return PropertyAccessKind.Ambiguous;
            }

            if (ancestor is PrefixUnaryExpressionSyntax prefix && prefix.Operand.Span == accessExpression.Span &&
                (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
            {
                return PropertyAccessKind.Ambiguous;
            }

            if (ancestor is PostfixUnaryExpressionSyntax postfix && postfix.Operand.Span == accessExpression.Span &&
                (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
            {
                return PropertyAccessKind.Ambiguous;
            }
        }

        return PropertyAccessKind.Read;
    }

    private enum PropertyAccessKind { Read, Write, Ambiguous }

    private static async Task<SourceSymbolResolutionResult> ResolveExternalConstructorAsync(
        Solution solution, SourceSymbolResolution source, CancellationToken cancellationToken)
    {
        Document? document = solution.GetDocument(source.DocumentId);
        SemanticModel? model = document is null ? null : await document.GetSemanticModelAsync(cancellationToken);
        if (model is null) return SourceSymbolResolutionResult.Succeeded(source);
        SyntaxToken token = source.SyntaxTree.GetRoot(cancellationToken).FindToken(source.Position);
        foreach (SyntaxNode node in token.Parent?.AncestorsAndSelf() ?? [])
        {
            bool typePosition = node switch
            {
                ObjectCreationExpressionSyntax creation => creation.Type.Span.Contains(source.Position),
                Microsoft.CodeAnalysis.VisualBasic.Syntax.ObjectCreationExpressionSyntax creation => creation.Type.Span.Contains(source.Position),
                _ => false
            };
            if (!typePosition) continue;
            SymbolInfo info = model.GetSymbolInfo(node, cancellationToken);
            if (info.Symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
                return SourceSymbolResolutionResult.Succeeded(source with { Symbol = constructor, HasExactBinding = true });
            if (info.CandidateReason != CandidateReason.None)
                return SourceSymbolResolutionResult.Failed(DiagnosticIds.ExternalMemberAmbiguous,
                    "The object creation has no unique constructor binding.", ExitCodes.UsageError);
            break;
        }
        return SourceSymbolResolutionResult.Succeeded(source);
    }

    private static string ExternalMemberDiagnosticMessage(int diagnosticId, ISymbol? selected = null) => diagnosticId switch
    {
        DiagnosticIds.InvalidExternalSourceView => "External source supports only signature, declaration, or body views.",
        DiagnosticIds.ExternalImplementationUnavailable => "A matching local implementation assembly is unavailable.",
        DiagnosticIds.ExternalMemberBodyUnavailable => selected is null
            ? "The exact external member has no implementation body."
            : $"The selected {selected.Kind} '{selected.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}' has no implementation body. Use external-source metadata with signature or declaration for metadata facts; use ordinary dependency tools for members without a source call anchor.",
        DiagnosticIds.ExternalMemberAmbiguous => "The exact external member or implementation assembly is ambiguous.",
        DiagnosticIds.ExternalMemberStale => "The external assembly changed while the member was being read.",
        DiagnosticIds.ExternalMemberLimitExceeded => "The external assembly or decompilation exceeded the configured safety limits.",
        DiagnosticIds.ExternalMemberMalformedImage => "The external assembly is malformed or cannot be decompiled.",
        _ => "The external member could not be read."
    };

    private static IEnumerable<SymbolSourceSlice> CreateSlices(
        ISymbol symbol,
        Location location,
        SymbolSourceOptions options,
        CancellationToken cancellationToken)
    {
        SyntaxTree? syntaxTree = location.SourceTree;
        if (syntaxTree is null)
        {
            yield break;
        }

        SyntaxNode root = syntaxTree.GetRoot(cancellationToken);
        SourceText text = syntaxTree.GetText(cancellationToken);
        SyntaxNode? declaration = SourceLanguageFacts.FindDeclarationNode(root, symbol, location);
        if (declaration is null)
        {
            yield break;
        }

        foreach ((string textKind, TextSpan span) in GetViewSpans(declaration, options.View, symbol))
        {
            SymbolSourceSlice? slice = CreateSlice(text, syntaxTree, span, textKind, options);
            if (slice is not null)
            {
                yield return slice;
            }
        }

        if (options.View == "signature")
        {
            string? signature = SymbolFactsBuilder.Create(symbol).Signature;
            if (!string.IsNullOrWhiteSpace(signature))
            {
                FileLinePositionSpan lineSpan = location.GetLineSpan();
                yield return new SymbolSourceSlice(
                    TextKind: "signature",
                    Path: PathDisplay.FromCurrentDirectory(lineSpan.Path),
                    StartLine: lineSpan.StartLinePosition.Line + 1,
                    StartColumn: lineSpan.StartLinePosition.Character + 1,
                    EndLine: lineSpan.EndLinePosition.Line + 1,
                    EndColumn: lineSpan.EndLinePosition.Character + 1,
                    Lines: [signature],
                    Truncated: false);
            }
        }
    }

    private static IEnumerable<(string TextKind, TextSpan Span)> GetViewSpans(
        SyntaxNode declaration,
        string view,
        ISymbol symbol)
    {
        return view switch
        {
            "signature" => [],
            "body" => SourceLanguageFacts.GetBodySpans(declaration),
            "members" => SourceLanguageFacts.GetMemberSpans(declaration),
            "xml-doc" => SourceLanguageFacts.GetXmlDocSpans(declaration),
            "attributes" => SourceLanguageFacts.GetAttributeSpans(declaration),
            _ => [("declaration", declaration.Span)]
        };
    }

    private static SymbolSourceSlice? CreateSlice(
        SourceText text,
        SyntaxTree syntaxTree,
        TextSpan span,
        string textKind,
        SymbolSourceOptions options)
    {
        FileLinePositionSpan lineSpan = syntaxTree.GetLineSpan(span);
        if (!lineSpan.IsValid)
        {
            return null;
        }

        int startLine = lineSpan.StartLinePosition.Line + 1;
        int endLine = Math.Max(startLine, lineSpan.EndLinePosition.Line + 1);
        int lineCount = endLine - startLine + 1;
        int effectiveLineCount = Math.Min(lineCount, options.MaxLines);
        string[] lines = [.. text.Lines
            .Skip(startLine - 1)
            .Take(effectiveLineCount)
            .Select(line => line.ToString())];

        int charLimit = options.BudgetTokens * 4;
        bool budgetTruncated = false;
        int used = 0;
        List<string> boundedLines = [];
        foreach (string line in lines)
        {
            if (used + line.Length > charLimit)
            {
                int remaining = Math.Max(0, charLimit - used);
                if (remaining > 0)
                {
                    boundedLines.Add(line[..Math.Min(line.Length, remaining)]);
                }

                budgetTruncated = true;
                break;
            }

            boundedLines.Add(line);
            used += line.Length;
        }

        return new SymbolSourceSlice(
            TextKind: textKind,
            Path: PathDisplay.FromCurrentDirectory(lineSpan.Path),
            StartLine: startLine,
            StartColumn: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1,
            Lines: boundedLines,
            Truncated: lineCount > options.MaxLines || budgetTruncated);
    }

    private static SymbolSourceSymbol CreateSymbol(ISymbol symbol, string projectName)
    {
        SymbolSourceLocation? location = SymbolNavigationFacts.GetSourceLocations(symbol).FirstOrDefault();
        return new SymbolSourceSymbol(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString(),
            Container: SymbolNavigationFacts.GetContainer(symbol),
            Facts: SymbolFactsBuilder.Create(symbol, projectName),
            Path: location?.Path,
            Line: location?.Line,
            Column: location?.Column,
            EndLine: location?.EndLine,
            EndColumn: location?.EndColumn);
    }
}

internal sealed record SymbolSourceOptions(string View, int MaxLines, int BudgetTokens, string ExternalSource = "none");

internal sealed record SymbolSourceResolutionResult(SymbolSourceResolution? Resolution, SymbolNavigationError? Error)
{
    public static SymbolSourceResolutionResult Succeeded(SymbolSourceResolution resolution)
    {
        return new SymbolSourceResolutionResult(resolution, Error: null);
    }

    public static SymbolSourceResolutionResult Failed(SymbolNavigationError error)
    {
        return new SymbolSourceResolutionResult(Resolution: null, error);
    }
}

internal sealed record SymbolSourceResolution(
    string File,
    int Line,
    int Column,
    SymbolSourceSymbol Symbol,
    string View,
    SymbolSourceLimits Limits,
    IReadOnlyList<SymbolSourceSlice> Slices,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? SourceOrigin = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ExternalAssemblyProvenance? ExternalAssembly = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    ExternalMemberSnapshot? ExternalSnapshot = null);

internal sealed record SymbolSourceLimits(int MaxLines, int BudgetTokens);

internal sealed record SymbolSourceSymbol(
    string Name,
    string Kind,
    string? Container,
    SymbolFacts Facts,
    string? Path,
    int? Line,
    int? Column,
    int? EndLine,
    int? EndColumn);

internal sealed record SymbolSourceSlice(
    string TextKind,
    string Path,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    IReadOnlyList<string> Lines,
    bool Truncated,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Origin = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? Editable = null);
