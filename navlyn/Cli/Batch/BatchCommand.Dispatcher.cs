using Navlyn.Diagnostics;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal static partial class BatchCommand
{
    private static async Task<BatchRequestResult> ExecuteRequestAsync(
        LoadedWorkspace loadedWorkspace,
        BatchDefaults defaults,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        return request.Command switch
        {
            "overview" => BatchRequestResult.Success(
                request.Id,
                request.Command,
                CreateOverviewResult(loadedWorkspace)),
            "diagnostics" => await ExecuteDiagnosticsAsync(loadedWorkspace, defaults, request, cancellationToken),
            "symbols" => await ExecuteSymbolsAsync(loadedWorkspace, defaults, request, cancellationToken),
            "symbols-in" => await ExecuteSymbolsInAsync(loadedWorkspace, defaults, request, cancellationToken),
            "outline" => await ExecuteOutlineAsync(loadedWorkspace, defaults, request, cancellationToken),
            "symbol-at" => await ExecuteSymbolAtAsync(loadedWorkspace, defaults, request, cancellationToken),
            "symbol-info" => await ExecuteSymbolInfoAsync(loadedWorkspace, defaults, request, cancellationToken),
            "definition" => await ExecuteDefinitionAsync(loadedWorkspace, defaults, request, cancellationToken),
            "references" => await ExecuteReferencesAsync(loadedWorkspace, defaults, request, cancellationToken),
            "implementations" => await ExecuteImplementationsAsync(loadedWorkspace, defaults, request, cancellationToken),
            "type-hierarchy" => await ExecuteTypeHierarchyAsync(loadedWorkspace, defaults, request, cancellationToken),
            "callers" => await ExecuteCallersAsync(loadedWorkspace, defaults, request, cancellationToken),
            "calls" => await ExecuteCallsAsync(loadedWorkspace, defaults, request, cancellationToken),
            "find" => await ExecuteFuzzyFindAsync(loadedWorkspace, defaults, request, cancellationToken),
            "where-used" => await ExecuteFuzzyWhereUsedAsync(loadedWorkspace, defaults, request, cancellationToken),
            "about" => await ExecuteFuzzyAboutAsync(loadedWorkspace, defaults, request, cancellationToken),
            "related" => await ExecuteFuzzyFilesAsync(loadedWorkspace, defaults, request, "related", cancellationToken),
            "impact" => await ExecuteFuzzyFilesAsync(loadedWorkspace, defaults, request, "impact", cancellationToken),
            "entrypoints" => await ExecuteFuzzyEntrypointsAsync(loadedWorkspace, defaults, request, cancellationToken),
            "review-diff" => await ExecuteReviewDiffAsync(loadedWorkspace, defaults, request, cancellationToken),
            "review-pack" => await ExecuteReviewPackAsync(loadedWorkspace, defaults, request, cancellationToken),
            "context-pack" => await ExecuteContextPackAsync(loadedWorkspace, defaults, request, cancellationToken),
            "repo-graph" => ExecuteRepoGraph(loadedWorkspace, defaults, request),
            "public-api-diff" => await ExecutePublicApiDiffAsync(loadedWorkspace, defaults, request, cancellationToken),
            "tests-for-symbol" => await ExecuteTestsForSymbolAsync(loadedWorkspace, defaults, request, cancellationToken),
            "tests-for-diff" => await ExecuteTestsForDiffAsync(loadedWorkspace, defaults, request, cancellationToken),
            "framework-entrypoints" => await ExecuteFrameworkEntrypointsAsync(loadedWorkspace, defaults, request, cancellationToken),
            "route-map" => await ExecuteRouteMapAsync(loadedWorkspace, defaults, request, cancellationToken),
            "route-impact" => await ExecuteRouteImpactAsync(loadedWorkspace, defaults, request, cancellationToken),
            "di-graph" => await ExecuteDiGraphAsync(loadedWorkspace, defaults, request, cancellationToken),
            "where-registered" => await ExecuteWhereRegisteredAsync(loadedWorkspace, defaults, request, cancellationToken),
            "di-impact" => await ExecuteDiImpactAsync(loadedWorkspace, defaults, request, cancellationToken),
            "options-graph" => await ExecuteOptionsGraphAsync(loadedWorkspace, defaults, request, cancellationToken),
            "config-impact" => await ExecuteConfigImpactAsync(loadedWorkspace, defaults, request, cancellationToken),
            "where-handled" => await ExecuteMessageDomainAsync(loadedWorkspace, defaults, request, includeCallSites: false, cancellationToken),
            "message-flow" => await ExecuteMessageDomainAsync(loadedWorkspace, defaults, request, includeCallSites: true, cancellationToken),
            "ef-model" => await ExecuteEfModelAsync(loadedWorkspace, defaults, request, cancellationToken),
            "entity-impact" => await ExecuteEntityImpactAsync(loadedWorkspace, defaults, request, cancellationToken),
            "package-usage" => await ExecutePackageDomainAsync(loadedWorkspace, defaults, request, impact: false, cancellationToken),
            "package-impact" => await ExecutePackageDomainAsync(loadedWorkspace, defaults, request, impact: true, cancellationToken),
            _ => BatchRequestResult.Failed(
                request.Id,
                request.Command,
                DiagnosticIds.InvalidBatchInput,
                $"Unsupported batch request command: {request.Command}.")
        };
    }

}
