using System.CommandLine;

using Navlyn.Symbols;

namespace Navlyn.Cli.Commands;

internal static class SharedOptions
{
    public static Option<FileInfo> CreateWorkspaceOption()
    {
        return new Option<FileInfo>("--workspace")
        {
            Description = "Path to a navlyn.workspace.json, .code-workspace, .slnx, .sln, or .csproj workspace, or auto.",
            Required = true
        };
    }

    public static Option<string?> CreateWorkspaceRootPolicyOption()
    {
        Option<string?> option = new("--workspace-root-policy")
        {
            Description = "Workspace root policy: repo-relative, allow-listed, or all."
        };

        option.AcceptOnlyFromAmong("repo-relative", "allow-listed", "all");
        return option;
    }

    public static Option<string> CreateQueryOption()
    {
        return new Option<string>("--query")
        {
            Description = "Symbol name query.",
            Required = true
        };
    }

    public static Option<string> CreateMatchOption()
    {
        Option<string> option = new("--match")
        {
            Description = "Symbol name match mode: contains, exact, or regex.",
            DefaultValueFactory = _ => "contains"
        };

        option.AcceptOnlyFromAmong("contains", "exact", "regex");
        return option;
    }

    public static Option<bool> CreateCaseSensitiveOption()
    {
        return new Option<bool>("--case-sensitive")
        {
            Description = "Use case-sensitive symbol name matching."
        };
    }

    public static Option<int?> CreateLimitOption()
    {
        return new Option<int?>("--limit")
        {
            Description = "Maximum number of symbol matches to return. Must be 1 or greater."
        };
    }

    public static Option<string> CreateSearchScopeOption()
    {
        Option<string> option = new("--scope")
        {
            Description = "Search scope for expensive navigation: file, project, dependent-projects, workspace-set, or solution.",
            DefaultValueFactory = _ => SymbolNavigationSearchOptions.DefaultScope
        };

        option.AcceptOnlyFromAmong([.. SymbolNavigationSearchScopes.Values]);
        return option;
    }

    public static Option<int?> CreateMaxDocumentsOption()
    {
        return new Option<int?>("--max-documents")
        {
            Description = $"Maximum lexically matching documents to search before returning partial results. Defaults to {SymbolNavigationSearchOptions.DefaultMaxDocuments}."
        };
    }

    public static Option<string[]> CreateKindOption()
    {
        return new Option<string[]>("--kind")
        {
            Description = "Restrict symbol matches to a case-sensitive symbol kind string. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    public static Option<string[]> CreateProjectFiltersOption()
    {
        return new Option<string[]>("--project")
        {
            Description = "Restrict results to a project name or repository-relative .csproj path. Can be specified more than once.",
            AllowMultipleArgumentsPerToken = true
        };
    }

    public static Option<string?> CreateProjectFilterOption()
    {
        return new Option<string?>("--project")
        {
            Description = "Resolve the source position in the context of a project name or repository-relative .csproj path."
        };
    }

    public static Option<bool> CreateExcludeGeneratedOption()
    {
        return new Option<bool>("--exclude-generated")
        {
            Description = "Exclude generated source files or source locations where applicable."
        };
    }

    public static Option<bool> CreateIncludeMetadataOption()
    {
        return new Option<bool>("--include-metadata")
        {
            Description = "Include metadata-only symbol facts where the command supports them."
        };
    }

    public static Option<FileInfo> CreateFileOption()
    {
        return new Option<FileInfo>("--file")
        {
            Description = "Path to a C# source file in the workspace.",
            Required = true
        };
    }

    public static Option<int> CreateLineOption()
    {
        return new Option<int>("--line")
        {
            Description = "1-based source line.",
            Required = true
        };
    }

    public static Option<int> CreateColumnOption()
    {
        return new Option<int>("--column")
        {
            Description = "1-based source column.",
            Required = true
        };
    }
}
