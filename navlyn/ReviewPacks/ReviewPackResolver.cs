using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Navlyn.DependencyInjection;
using Navlyn.Diagnostics;
using Navlyn.Diffs;
using Navlyn.GeneratedCode;
using Navlyn.Paths;
using Navlyn.Workspaces;

namespace Navlyn.ReviewPacks;

internal sealed class ReviewPackResolver
{
    private static readonly string[] SensitiveNames = ["password", "passwd", "pwd", "secret", "token", "apikey", "apiKey", "connectionString"];

    public async Task<ReviewPackExecutionResult> ResolveAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        IReadOnlyList<ReviewPackProjectFilter>? projectFilters,
        ReviewPackOptions options,
        CancellationToken cancellationToken)
    {
        ReviewPackInput input = await CreateInputAsync(workspace, projects, options, cancellationToken);
        if (input.Error is not null)
        {
            return ReviewPackExecutionResult.Failed(input.Error.DiagnosticId, input.Error.Message, input.Error.ExitCode);
        }

        List<ReviewPackFinding> findings = [];
        List<ReviewPackPackResult> packResults = [];
        List<ReviewPackSkippedPack> skipped = [];
        List<string> warnings = [];

        foreach (string pack in options.Packs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ReviewPackFinding> packFindings;
            IReadOnlyList<string> packWarnings = [];
            IReadOnlyList<ArchitectureRule> architectureRules = [];
            if (pack == "architecture" && !TryLoadArchitectureRules(workspace, options, out architectureRules, out string? architectureWarning, out ReviewPackError? architectureError))
            {
                if (architectureError is not null)
                {
                    return ReviewPackExecutionResult.Failed(architectureError.DiagnosticId, architectureError.Message, architectureError.ExitCode);
                }

                skipped.Add(new ReviewPackSkippedPack(pack, "skipped", architectureWarning ?? "architecture-config-not-found"));
                warnings.Add(architectureWarning ?? "architecture-config-not-found");
                continue;
            }

            packFindings = pack switch
            {
                "async" => AnalyzeAsyncPack(input, options),
                "disposal" => AnalyzeDisposalPack(input, options),
                "nullability" => await AnalyzeNullabilityPackAsync(input, options, cancellationToken),
                "security" => AnalyzeSecurityPack(input, options),
                "architecture" => AnalyzeArchitecturePack(input, architectureRules, options),
                _ => []
            };

            findings.AddRange(packFindings);
            packResults.Add(CreatePackResult(pack, packFindings, options.FindingLimit, packWarnings));
        }

        IReadOnlyList<ReviewPackFinding> orderedFindings = [.. findings
            .OrderBy(finding => SeverityPriority(finding.Severity))
            .ThenBy(finding => ConfidencePriority(finding.Confidence))
            .ThenBy(finding => Array.IndexOf(ReviewPackNames.Ordered, finding.Pack))
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ThenBy(finding => finding.Evidence.FirstOrDefault()?.Path, StringComparer.Ordinal)
            .ThenBy(finding => finding.Evidence.FirstOrDefault()?.Line)
            .ThenBy(finding => finding.Evidence.FirstOrDefault()?.Column)
            .ThenBy(finding => finding.Claim, StringComparer.Ordinal)];

        bool truncated = orderedFindings.Count > options.FindingLimit ||
            input.DocumentsTruncated ||
            packResults.Any(result => result.Truncated);
        IReadOnlyList<ReviewPackFinding> limitedFindings = [.. orderedFindings.Take(options.FindingLimit)];

        ReviewPackResult result = new(
            Workspace: workspace.DisplayPath,
            Kind: workspace.Kind,
            Command: "review-pack",
            Scope: new ReviewPackScope(
                options.Scope,
                input.Diff,
                input.Documents.Count,
                [.. input.Documents.Select(document => document.DisplayPath).OrderBy(path => path, StringComparer.Ordinal)]),
            Projects: projectFilters,
            ExcludeGenerated: options.ExcludeGenerated,
            Packs: new ReviewPackPacksSection(options.Packs, [.. packResults.Select(result => result.Pack)], skipped),
            Summary: CreateSummary(limitedFindings),
            Findings: limitedFindings,
            PackResults: packResults,
            Limits: new ReviewPackLimits(options.FindingLimit, options.EvidenceLimit, options.SymbolLimit, options.FileLimit),
            Truncated: truncated,
            Warnings: [.. warnings.Distinct(StringComparer.Ordinal).OrderBy(warning => warning, StringComparer.Ordinal)],
            NextActions: CreateNextActions(workspace.DisplayPath, limitedFindings));

        return ReviewPackExecutionResult.Succeeded(result);
    }

    private static async Task<ReviewPackInput> CreateInputAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<Project> projects,
        ReviewPackOptions options,
        CancellationToken cancellationToken)
    {
        DiffSet? diff = null;
        HashSet<string>? diffPaths = null;
        if (options.Scope == "diff")
        {
            string? repositoryRoot = PathDisplay.FindRepositoryRoot(workspace.FullPath);
            if (repositoryRoot is null)
            {
                return ReviewPackInput.Failed(DiagnosticIds.GitRepositoryNotFound, "Git repository root was not found for review-pack diff scope.", ExitCodes.UsageError);
            }

            DiffReadResult diffResult = await new GitDiffProvider().ReadAsync(repositoryRoot, options.DiffRequest!, cancellationToken);
            if (diffResult.Error is not null)
            {
                return ReviewPackInput.Failed(diffResult.Error.DiagnosticId, diffResult.Error.Message, diffResult.Error.ExitCode);
            }

            diff = diffResult.Diff!;
            diffPaths = [.. diff.Files
                .Where(file => file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(file => file.Path)];
        }

        List<ReviewPackDocument> documents = [];
        foreach (Project project in projects.OrderBy(project => project.FilePath, StringComparer.Ordinal).ThenBy(project => project.Name, StringComparer.Ordinal))
        {
            foreach (Document document in project.Documents.OrderBy(document => document.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (document.FilePath is null ||
                    !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string displayPath = PathDisplay.FromCurrentDirectory(document.FilePath);
                if (options.ExcludeGenerated && GeneratedCodeFacts.IsGeneratedPath(displayPath))
                {
                    continue;
                }

                if (diffPaths is not null && !diffPaths.Contains(displayPath))
                {
                    continue;
                }

                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                documents.Add(new ReviewPackDocument(project, document, displayPath, root, semanticModel));
            }
        }

        IReadOnlyList<ReviewPackDocument> ordered = [.. documents
            .GroupBy(document => (document.Project.Id, document.Document.Id))
            .Select(group => group.First())
            .OrderBy(document => document.DisplayPath, StringComparer.Ordinal)
            .ThenBy(document => document.Project.Name, StringComparer.Ordinal)];

        return ReviewPackInput.Succeeded(
            diff,
            [.. ordered.Take(options.FileLimit)],
            ordered.Count > options.FileLimit,
            projects);
    }

    private static IReadOnlyList<ReviewPackFinding> AnalyzeAsyncPack(ReviewPackInput input, ReviewPackOptions options)
    {
        List<ReviewPackFinding> findings = [];
        foreach (ReviewPackDocument document in input.Documents)
        {
            foreach (MethodDeclarationSyntax method in document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (method.Modifiers.Any(SyntaxKind.AsyncKeyword) &&
                    method.ReturnType is PredefinedTypeSyntax predefined &&
                    predefined.Keyword.IsKind(SyntaxKind.VoidKeyword) &&
                    !LooksLikeEventHandler(method, document.SemanticModel))
                {
                    AddFinding(findings, document, "async", "async.async-void", "warning", "high", "Async void methods are hard for callers and tests to observe.", method.Identifier, method, options, ["async-void-method"]);
                }

                if (AcceptsCancellationToken(method, document.SemanticModel))
                {
                    foreach (InvocationExpressionSyntax invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        IMethodSymbol? target = document.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                        if (target is not null &&
                            target.Parameters.Any(parameter => IsCancellationToken(parameter.Type)) &&
                            !invocation.ArgumentList.Arguments.Any(argument => IsCancellationTokenExpression(argument.Expression, document.SemanticModel)))
                        {
                            AddFinding(findings, document, "async", "async.cancellation-token-not-forwarded", "info", "medium", "A method accepts CancellationToken but calls an overload with a token parameter without forwarding one.", invocation.GetLocation(), ContainingSymbolName(invocation, document.SemanticModel), options, ["callee-has-cancellation-token-parameter", "caller-has-cancellation-token-parameter"]);
                        }
                    }
                }
            }

            foreach (InvocationExpressionSyntax invocation in document.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                IMethodSymbol? symbol = document.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                string methodName = symbol?.Name ?? GetInvocationName(invocation);
                if (((methodName is "Wait" or "GetResult") && IsTaskLike(symbol?.ContainingType)) ||
                    IsGetAwaiterGetResult(invocation, document.SemanticModel))
                {
                    AddFinding(findings, document, "async", "async.sync-over-async", "warning", "high", "Synchronous wait on a Task-like value can block async call paths.", invocation.GetLocation(), ContainingSymbolName(invocation, document.SemanticModel), options, ["task-like-sync-wait"]);
                }

                if (IsTaskLike(symbol?.ReturnType) && invocation.Parent is ExpressionStatementSyntax)
                {
                    AddFinding(findings, document, "async", "async.fire-and-forget", "info", "medium", "A Task-like invocation result is not awaited, returned, or assigned.", invocation.GetLocation(), ContainingSymbolName(invocation, document.SemanticModel), options, ["discarded-task-like-invocation"]);
                }
            }

            foreach (MemberAccessExpressionSyntax access in document.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (access.Name.Identifier.ValueText == "Result" &&
                    IsTaskLike(document.SemanticModel.GetTypeInfo(access.Expression).Type))
                {
                    AddFinding(findings, document, "async", "async.sync-over-async", "warning", "high", "Synchronous wait on a Task-like value can block async call paths.", access.GetLocation(), ContainingSymbolName(access, document.SemanticModel), options, ["task-like-result-property"]);
                }
            }
        }

        return DistinctFindings(findings);
    }

    private static IReadOnlyList<ReviewPackFinding> AnalyzeDisposalPack(ReviewPackInput input, ReviewPackOptions options)
    {
        List<ReviewPackFinding> findings = [];
        foreach (ReviewPackDocument document in input.Documents)
        {
            foreach (LocalDeclarationStatementSyntax local in document.Root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                bool isUsing = local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword);
                bool isAwaitUsing = local.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword);
                foreach (VariableDeclaratorSyntax variable in local.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is null)
                    {
                        continue;
                    }

                    ITypeSymbol? type = document.SemanticModel.GetTypeInfo(variable.Initializer.Value).Type;
                    if (type is null)
                    {
                        continue;
                    }

                    if (isUsing && !isAwaitUsing && ImplementsInterface(type, "System.IAsyncDisposable"))
                    {
                        AddFinding(findings, document, "disposal", "disposal.async-disposable-sync-dispose", "warning", "medium", "An async disposable value is used with synchronous disposal syntax.", variable.GetLocation(), ContainingSymbolName(variable, document.SemanticModel), options, ["async-disposable-sync-using"]);
                    }

                    if (!isUsing &&
                        ImplementsInterface(type, "System.IDisposable") &&
                        !LooksDisposedOrTransferred(variable.Identifier.ValueText, local.Parent))
                    {
                        AddFinding(findings, document, "disposal", "disposal.created-disposable-not-disposed", "info", "medium", "A disposable value is created without obvious local disposal or ownership transfer.", variable.GetLocation(), ContainingSymbolName(variable, document.SemanticModel), options, ["local-disposable-created"]);
                    }
                }
            }

            foreach (UsingStatementSyntax usingStatement in document.Root.DescendantNodes().OfType<UsingStatementSyntax>())
            {
                if (usingStatement.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword))
                {
                    continue;
                }

                ExpressionSyntax? expression = usingStatement.Expression ?? usingStatement.Declaration?.Variables.FirstOrDefault()?.Initializer?.Value;
                ITypeSymbol? type = expression is null ? null : document.SemanticModel.GetTypeInfo(expression).Type;
                if (ImplementsInterface(type, "System.IAsyncDisposable"))
                {
                    AddFinding(findings, document, "disposal", "disposal.async-disposable-sync-dispose", "warning", "medium", "An async disposable value is used with synchronous disposal syntax.", usingStatement.UsingKeyword.GetLocation(), ContainingSymbolName(usingStatement, document.SemanticModel), options, ["async-disposable-sync-using"]);
                }
            }
        }

        return DistinctFindings(findings);
    }

    private static async Task<IReadOnlyList<ReviewPackFinding>> AnalyzeNullabilityPackAsync(
        ReviewPackInput input,
        ReviewPackOptions options,
        CancellationToken cancellationToken)
    {
        List<ReviewPackFinding> findings = [];
        foreach (ReviewPackDocument document in input.Documents)
        {
            foreach (PostfixUnaryExpressionSyntax suppression in document.Root.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>().Where(node => node.IsKind(SyntaxKind.SuppressNullableWarningExpression)))
            {
                AddFinding(findings, document, "nullability", "nullability.null-forgiving", "info", "high", "A null-forgiving suppression hides nullable analysis at this source location.", suppression.OperatorToken.GetLocation(), ContainingSymbolName(suppression, document.SemanticModel), options, ["null-forgiving-operator"]);
            }

            Compilation? compilation = await document.Project.GetCompilationAsync(cancellationToken);
            if (compilation?.Options.NullableContextOptions is NullableContextOptions.Disable)
            {
                foreach (MemberDeclarationSyntax member in document.Root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsPublicApiDeclaration).Take(options.SymbolLimit))
                {
                    AddFinding(findings, document, "nullability", "nullability.oblivious-public-api", "info", "medium", "A public API declaration is in a project with nullable annotations disabled.", member.GetLocation(), ContainingSymbolName(member, document.SemanticModel), options, ["nullable-disabled-public-api"]);
                }
            }

            foreach (PropertyDeclarationSyntax property in document.Root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(property => property.Modifiers.Any(SyntaxKind.RequiredKeyword)))
            {
                AddFinding(findings, document, "nullability", "nullability.required-member-signal", "info", "medium", "A required member is part of object initialization contract and may need review when public constructors change.", property.Identifier.GetLocation(), ContainingSymbolName(property, document.SemanticModel), options, ["required-member"]);
            }
        }

        return DistinctFindings(findings);
    }

    private static IReadOnlyList<ReviewPackFinding> AnalyzeSecurityPack(ReviewPackInput input, ReviewPackOptions options)
    {
        List<ReviewPackFinding> findings = [];
        foreach (ReviewPackDocument document in input.Documents)
        {
            foreach (MethodDeclarationSyntax method in document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (LooksLikeAspNetEndpoint(method, document.SemanticModel) &&
                    !HasSecurityAttribute(method) &&
                    !HasSecurityAttribute(method.Parent as MemberDeclarationSyntax))
                {
                    AddFinding(findings, document, "security", "security.auth-surface-signal", "info", "medium", "A framework-facing endpoint has no nearby authorization metadata in source.", method.Identifier.GetLocation(), ContainingSymbolName(method, document.SemanticModel), options, ["endpoint-without-auth-metadata"]);
                }
            }

            foreach (InvocationExpressionSyntax invocation in document.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                IMethodSymbol? symbol = document.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                string name = symbol?.Name ?? GetInvocationName(invocation);
                string containing = symbol?.ContainingType?.ToDisplayString() ?? string.Empty;
                string invocationText = invocation.ToString();

                if ((name.Contains("Sql", StringComparison.OrdinalIgnoreCase) || containing.Contains("Sql", StringComparison.OrdinalIgnoreCase)) &&
                    HasInterpolatedOrConcatenatedString(invocation.ArgumentList))
                {
                    AddFinding(findings, document, "security", "security.sql-string-construction", "warning", "medium", "SQL-like invocation uses constructed string input that should be reviewed.", invocation.GetLocation(), ContainingSymbolName(invocation, document.SemanticModel), options, ["sql-like-constructed-string"]);
                }

                if (containing is "System.IO.File" or "System.Diagnostics.Process" ||
                    containing.StartsWith("System.Reflection.", StringComparison.Ordinal) ||
                    (containing == "System.Type" && name == "GetType"))
                {
                    AddFinding(findings, document, "security", "security.file-process-reflection-signal", "info", "medium", "File, process, or reflection API usage may need review near external input boundaries.", invocation.GetLocation(), ContainingSymbolName(invocation, document.SemanticModel), options, ["sensitive-api-family"]);
                }

                if (name.Contains("Deserialize", StringComparison.OrdinalIgnoreCase) ||
                    containing.Contains("JsonSerializer", StringComparison.Ordinal) ||
                    containing.Contains("XmlSerializer", StringComparison.Ordinal))
                {
                    AddFinding(findings, document, "security", "security.deserialization-signal", "info", "medium", "Deserialization API usage should be reviewed for input trust and target type.", invocation.GetLocation(), ContainingSymbolName(invocation, document.SemanticModel), options, ["deserialization-api"]);
                }

                if ((name.StartsWith("Log", StringComparison.Ordinal) || containing.Contains("ILogger", StringComparison.Ordinal)) &&
                    SensitiveNames.Any(token => invocationText.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    AddFinding(findings, document, "security", "security.sensitive-logging-signal", "warning", "medium", "Logging call arguments contain sensitive-looking names.", invocation.GetLocation(), ContainingSymbolName(invocation, document.SemanticModel), options, ["sensitive-name-in-logging-call"]);
                }
            }

            foreach (ObjectCreationExpressionSyntax creation in document.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                ITypeSymbol? type = document.SemanticModel.GetTypeInfo(creation).Type;
                if (type?.ToDisplayString().Contains("SqlCommand", StringComparison.Ordinal) == true &&
                    creation.ArgumentList is not null &&
                    HasInterpolatedOrConcatenatedString(creation.ArgumentList))
                {
                    AddFinding(findings, document, "security", "security.sql-string-construction", "warning", "medium", "SQL command construction uses constructed string input that should be reviewed.", creation.GetLocation(), ContainingSymbolName(creation, document.SemanticModel), options, ["sql-command-constructed-string"]);
                }
            }
        }

        return DistinctFindings(findings);
    }

    private static IReadOnlyList<ReviewPackFinding> AnalyzeArchitecturePack(
        ReviewPackInput input,
        IReadOnlyList<ArchitectureRule> rules,
        ReviewPackOptions options)
    {
        List<ReviewPackFinding> findings = [];
        foreach (ArchitectureRule rule in rules)
        {
            if (rule.Kind == "project-dependency")
            {
                foreach (Project project in input.Projects.Where(project => MatchesRule(rule.From, project.Name, project.FilePath)))
                {
                    foreach (ProjectReference reference in project.ProjectReferences)
                    {
                        Project? referenced = project.Solution.GetProject(reference.ProjectId);
                        if (referenced is not null && rule.Disallow.Any(disallowed => MatchesRule(disallowed, referenced.Name, referenced.FilePath)))
                        {
                            ReviewPackEvidence evidence = ProjectEvidence(project);
                            findings.Add(new ReviewPackFinding(
                                "architecture",
                                "architecture.project-dependency-violation",
                                "review-signal",
                                "warning",
                                "high",
                                "A project reference violates a configured architecture rule.",
                                [evidence],
                                [evidence],
                                [],
                                ["project-reference-disallowed", $"architecture-rule:{rule.Id}"],
                                []));
                        }
                    }
                }
            }

            if (rule.Kind == "namespace-dependency" || rule.Kind == "layer-dependency")
            {
                foreach (ReviewPackDocument document in input.Documents)
                {
                    string namespaceName = document.Root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? string.Empty;
                    if (!MatchesNamespace(rule.From, namespaceName))
                    {
                        continue;
                    }

                    foreach (UsingDirectiveSyntax usingDirective in document.Root.DescendantNodes().OfType<UsingDirectiveSyntax>())
                    {
                        string usedNamespace = usingDirective.Name?.ToString() ?? string.Empty;
                        if (rule.Disallow.Any(disallowed => MatchesNamespace(disallowed, usedNamespace)))
                        {
                            AddFinding(
                                findings,
                                document,
                                "architecture",
                                rule.Kind == "layer-dependency" ? "architecture.layer-dependency-violation" : "architecture.namespace-dependency-violation",
                                "warning",
                                "high",
                                "A namespace dependency violates a configured architecture rule.",
                                usingDirective.GetLocation(),
                                namespaceName,
                                options,
                                ["namespace-dependency-disallowed", $"architecture-rule:{rule.Id}"]);
                        }
                    }
                }
            }
        }

        return DistinctFindings(findings);
    }

    private static bool TryLoadArchitectureRules(
        LoadedWorkspace workspace,
        ReviewPackOptions options,
        out IReadOnlyList<ArchitectureRule> rules,
        out string? warning,
        out ReviewPackError? error)
    {
        rules = [];
        warning = null;
        error = null;

        string? configPath = options.ArchitectureConfig;
        bool explicitConfig = !string.IsNullOrWhiteSpace(configPath);
        if (string.IsNullOrWhiteSpace(configPath))
        {
            configPath = ".navlyn.yml";
        }

        string? resolvedConfigPath = PathDisplay.GetInputPathCandidates(configPath, workspace.FullPath)
            .FirstOrDefault(File.Exists);
        if (resolvedConfigPath is null)
        {
            warning = explicitConfig ? $"architecture-config-not-found:{configPath}" : "architecture-config-not-found";
            if (explicitConfig)
            {
                error = new ReviewPackError(DiagnosticIds.ParseError, $"Architecture config was not found: {configPath}", ExitCodes.UsageError);
                return false;
            }

            return false;
        }

        try
        {
            rules = ParseArchitectureRules(File.ReadAllLines(resolvedConfigPath));
            return true;
        }
        catch (InvalidDataException ex)
        {
            if (explicitConfig)
            {
                error = new ReviewPackError(DiagnosticIds.ParseError, $"Invalid architecture config: {ex.Message}", ExitCodes.UsageError);
                return false;
            }

            warning = "architecture-config-invalid";
            return false;
        }
    }

    private static IReadOnlyList<ArchitectureRule> ParseArchitectureRules(IReadOnlyList<string> lines)
    {
        List<ArchitectureRule> rules = [];
        ArchitectureRuleBuilder? current = null;
        bool inDisallow = false;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("- id:", StringComparison.Ordinal))
            {
                Flush();
                current = new ArchitectureRuleBuilder { Id = Unquote(line["- id:".Length..].Trim()) };
                inDisallow = false;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.StartsWith("kind:", StringComparison.Ordinal))
            {
                current.Kind = Unquote(line["kind:".Length..].Trim());
                inDisallow = false;
            }
            else if (line.StartsWith("from:", StringComparison.Ordinal))
            {
                current.From = Unquote(line["from:".Length..].Trim());
                inDisallow = false;
            }
            else if (line.StartsWith("disallow:", StringComparison.Ordinal))
            {
                inDisallow = true;
            }
            else if (inDisallow && line.StartsWith("-", StringComparison.Ordinal))
            {
                current.Disallow.Add(Unquote(line[1..].Trim()));
            }
        }

        Flush();
        return rules;

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(current.Id) ||
                string.IsNullOrWhiteSpace(current.Kind) ||
                string.IsNullOrWhiteSpace(current.From) ||
                current.Disallow.Count == 0)
            {
                throw new InvalidDataException("Each rule requires id, kind, from, and at least one disallow item.");
            }

            if (current.Kind is not ("project-dependency" or "namespace-dependency" or "layer-dependency"))
            {
                throw new InvalidDataException($"Unsupported architecture rule kind: {current.Kind}");
            }

            rules.Add(new ArchitectureRule(current.Id, current.Kind, current.From, [.. current.Disallow]));
        }
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\''))
            ? trimmed[1..^1]
            : trimmed;
    }

    private static void AddFinding(
        List<ReviewPackFinding> findings,
        ReviewPackDocument document,
        string pack,
        string ruleId,
        string severity,
        string confidence,
        string claim,
        SyntaxToken token,
        SyntaxNode containingNode,
        ReviewPackOptions options,
        IReadOnlyList<string> reasonCodes)
    {
        AddFinding(findings, document, pack, ruleId, severity, confidence, claim, token.GetLocation(), ContainingSymbolName(containingNode, document.SemanticModel), options, reasonCodes);
    }

    private static void AddFinding(
        List<ReviewPackFinding> findings,
        ReviewPackDocument document,
        string pack,
        string ruleId,
        string severity,
        string confidence,
        string claim,
        Location location,
        string? containingSymbol,
        ReviewPackOptions options,
        IReadOnlyList<string> reasonCodes)
    {
        ReviewPackEvidence evidence = CreateEvidence(location, containingSymbol, options);
        findings.Add(new ReviewPackFinding(
            pack,
            ruleId,
            "review-signal",
            severity,
            confidence,
            claim,
            [evidence],
            [evidence],
            [],
            reasonCodes,
            []));
    }

    private static ReviewPackEvidence CreateEvidence(Location location, string? containingSymbol, ReviewPackOptions options)
    {
        FileLinePositionSpan span = location.GetLineSpan();
        string path = PathDisplay.FromCurrentDirectory(span.Path);
        int line = span.StartLinePosition.Line + 1;
        return new ReviewPackEvidence(
            path,
            line,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            span.EndLinePosition.Character + 1,
            containingSymbol,
            options.IncludeSnippets ? TryReadSnippet(path, line, options.SnippetLines) : null);
    }

    private static ReviewPackSnippet? TryReadSnippet(string path, int line, int contextLines)
    {
        try
        {
            string[] lines = File.ReadAllLines(path);
            if (line < 1 || line > lines.Length)
            {
                return null;
            }

            int safeContext = Math.Max(0, contextLines);
            int startLine = Math.Max(1, line - safeContext);
            int endLine = Math.Min(lines.Length, line + safeContext);
            return new ReviewPackSnippet(startLine, endLine, [.. lines.Skip(startLine - 1).Take(endLine - startLine + 1)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static ReviewPackPackResult CreatePackResult(string pack, IReadOnlyList<ReviewPackFinding> findings, int limit, IReadOnlyList<string> warnings)
    {
        IReadOnlyList<ReviewPackRuleSummary> rules = [.. findings
            .GroupBy(finding => finding.RuleId, StringComparer.Ordinal)
            .Select(group => new ReviewPackRuleSummary(group.Key, group.Count(), group.Select(item => item.Severity).OrderBy(SeverityPriority).First()))
            .OrderBy(rule => rule.RuleId, StringComparer.Ordinal)];

        return new ReviewPackPackResult(pack, "completed", findings.Count, limit, findings.Count > limit, warnings, rules);
    }

    private static ReviewPackSummary CreateSummary(IReadOnlyList<ReviewPackFinding> findings)
    {
        return new ReviewPackSummary(
            findings.Count,
            CountBy(findings, finding => finding.Pack),
            CountBy(findings, finding => finding.Severity),
            CountBy(findings, finding => finding.Confidence));
    }

    private static IReadOnlyDictionary<string, int> CountBy(IReadOnlyList<ReviewPackFinding> findings, Func<ReviewPackFinding, string> keySelector)
    {
        return findings
            .GroupBy(keySelector, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static IReadOnlyList<ReviewPackNextAction> CreateNextActions(string workspace, IReadOnlyList<ReviewPackFinding> findings)
    {
        ReviewPackFinding? first = findings.FirstOrDefault();
        if (first is null)
        {
            return [];
        }

        ReviewPackEvidence? evidence = first.Evidence.FirstOrDefault();
        return evidence is null
            ? []
            : [new ReviewPackNextAction("context-pack", workspace, Query: null, evidence.Path, evidence.Line, evidence.Column, "Inspect context around the first review-pack finding.")];
    }

    private static IReadOnlyList<ReviewPackFinding> DistinctFindings(IReadOnlyList<ReviewPackFinding> findings)
    {
        return [.. findings
            .GroupBy(finding =>
            {
                ReviewPackEvidence? evidence = finding.Evidence.FirstOrDefault();
                return (finding.RuleId, evidence?.Path, evidence?.Line, evidence?.Column);
            })
            .Select(group => group.First())];
    }

    private static bool IsTaskLike(ITypeSymbol? type)
    {
        string? name = type?.OriginalDefinition.ToDisplayString();
        return name is "System.Threading.Tasks.Task" or "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask" or "System.Threading.Tasks.ValueTask<TResult>";
    }

    private static bool IsGetAwaiterGetResult(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax getResultAccess ||
            getResultAccess.Name.Identifier.ValueText != "GetResult" ||
            getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation ||
            getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess ||
            getAwaiterAccess.Name.Identifier.ValueText != "GetAwaiter")
        {
            return false;
        }

        return IsTaskLike(model.GetTypeInfo(getAwaiterAccess.Expression).Type);
    }

    private static bool IsCancellationToken(ITypeSymbol? type)
    {
        return type?.ToDisplayString() == "System.Threading.CancellationToken";
    }

    private static bool IsCancellationTokenExpression(ExpressionSyntax expression, SemanticModel model)
    {
        return IsCancellationToken(model.GetTypeInfo(expression).Type);
    }

    private static bool AcceptsCancellationToken(MethodDeclarationSyntax method, SemanticModel model)
    {
        return method.ParameterList.Parameters.Any(parameter => IsCancellationToken(model.GetTypeInfo(parameter.Type!).Type));
    }

    private static bool LooksLikeEventHandler(MethodDeclarationSyntax method, SemanticModel model)
    {
        SeparatedSyntaxList<ParameterSyntax> parameters = method.ParameterList.Parameters;
        return parameters.Count >= 2 &&
            parameters[0].Identifier.ValueText is "sender" or "_" &&
            (model.GetTypeInfo(parameters[1].Type!).Type?.ToDisplayString().EndsWith("EventArgs", StringComparison.Ordinal) == true ||
                parameters[1].Identifier.ValueText is "e" or "args");
    }

    private static string GetInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => invocation.Expression.ToString()
        };
    }

    private static string? ContainingSymbolName(SyntaxNode node, SemanticModel model)
    {
        SyntaxNode? current = node;
        while (current is not null)
        {
            ISymbol? symbol = current switch
            {
                MemberDeclarationSyntax member => model.GetDeclaredSymbol(member),
                AnonymousFunctionExpressionSyntax anonymous => model.GetSymbolInfo(anonymous).Symbol,
                _ => null
            };
            if (symbol is not null)
            {
                return symbol.ToDisplayString();
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool ImplementsInterface(ITypeSymbol? type, string metadataName)
    {
        return type is not null &&
            type.AllInterfaces.Any(@interface => @interface.ToDisplayString() == metadataName);
    }

    private static bool LooksDisposedOrTransferred(string variableName, SyntaxNode? scope)
    {
        if (scope is null)
        {
            return false;
        }

        foreach (SyntaxNode node in scope.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax access &&
                access.Expression.ToString() == variableName &&
                access.Name.Identifier.ValueText is "Dispose" or "DisposeAsync")
            {
                return true;
            }

            if (node is ReturnStatementSyntax returnStatement && returnStatement.Expression?.ToString() == variableName)
            {
                return true;
            }

            if (node is AssignmentExpressionSyntax assignment && assignment.Right.ToString() == variableName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPublicApiDeclaration(MemberDeclarationSyntax member)
    {
        SyntaxTokenList modifiers = GetModifiers(member);
        return modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword);
    }

    private static SyntaxTokenList GetModifiers(MemberDeclarationSyntax member)
    {
        return member switch
        {
            BaseTypeDeclarationSyntax type => type.Modifiers,
            BaseMethodDeclarationSyntax method => method.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            FieldDeclarationSyntax field => field.Modifiers,
            EventFieldDeclarationSyntax eventField => eventField.Modifiers,
            _ => []
        };
    }

    private static bool LooksLikeAspNetEndpoint(MethodDeclarationSyntax method, SemanticModel model)
    {
        string typeName = model.GetDeclaredSymbol(method)?.ContainingType?.Name ?? string.Empty;
        return typeName.EndsWith("Controller", StringComparison.Ordinal) ||
            HasAttribute(method, "Http") ||
            HasAttribute(method, "Route");
    }

    private static bool HasSecurityAttribute(MemberDeclarationSyntax? member)
    {
        return member is not null &&
            (HasAttribute(member, "Authorize") || HasAttribute(member, "AllowAnonymous"));
    }

    private static bool HasAttribute(MemberDeclarationSyntax member, string nameFragment)
    {
        return member.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString().Contains(nameFragment, StringComparison.Ordinal));
    }

    private static bool HasInterpolatedOrConcatenatedString(BaseArgumentListSyntax arguments)
    {
        return arguments.Arguments.Any(argument =>
            argument.Expression is InterpolatedStringExpressionSyntax ||
            argument.Expression.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Any(node => node.IsKind(SyntaxKind.AddExpression)));
    }

    private static bool MatchesRule(string pattern, string name, string? path)
    {
        return string.Equals(pattern, name, StringComparison.Ordinal) ||
            path is not null && string.Equals(pattern.Replace('\\', '/'), PathDisplay.FromCurrentDirectory(path), StringComparison.Ordinal);
    }

    private static bool MatchesNamespace(string pattern, string namespaceName)
    {
        return string.Equals(pattern, namespaceName, StringComparison.Ordinal) ||
            namespaceName.StartsWith(pattern + ".", StringComparison.Ordinal);
    }

    private static ReviewPackEvidence ProjectEvidence(Project project)
    {
        string path = project.FilePath is null ? project.Name : PathDisplay.FromCurrentDirectory(project.FilePath);
        return new ReviewPackEvidence(path, 1, 1, 1, 1, project.Name, Snippet: null);
    }

    private static int SeverityPriority(string severity)
    {
        return severity switch
        {
            "error" => 0,
            "warning" => 1,
            "info" => 2,
            _ => 3
        };
    }

    private static int ConfidencePriority(string confidence)
    {
        return confidence switch
        {
            "high" => 0,
            "medium" => 1,
            "low" => 2,
            _ => 3
        };
    }

    private sealed record ReviewPackDocument(
        Project Project,
        Document Document,
        string DisplayPath,
        SyntaxNode Root,
        SemanticModel SemanticModel);

    private sealed record ReviewPackInput(
        DiffSet? Diff,
        IReadOnlyList<ReviewPackDocument> Documents,
        bool DocumentsTruncated,
        IReadOnlyList<Project> Projects,
        ReviewPackError? Error)
    {
        public static ReviewPackInput Succeeded(DiffSet? diff, IReadOnlyList<ReviewPackDocument> documents, bool documentsTruncated, IReadOnlyList<Project> projects)
        {
            return new ReviewPackInput(diff, documents, documentsTruncated, projects, Error: null);
        }

        public static ReviewPackInput Failed(int diagnosticId, string message, int exitCode)
        {
            return new ReviewPackInput(Diff: null, Documents: [], DocumentsTruncated: false, Projects: [], new ReviewPackError(diagnosticId, message, exitCode));
        }
    }

    private sealed record ArchitectureRule(string Id, string Kind, string From, IReadOnlyList<string> Disallow);

    private sealed class ArchitectureRuleBuilder
    {
        public string Id { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public string From { get; set; } = string.Empty;

        public List<string> Disallow { get; } = [];
    }
}
