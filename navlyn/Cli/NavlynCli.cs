using System.CommandLine;
using System.CommandLine.Parsing;
using Navlyn.Cli.Commands;
using Navlyn.Diagnostics;

namespace Navlyn.Cli;

internal static class NavlynCli
{
    public static Task<int> RunAsync(string[] args)
    {
        RootCommand rootCommand = CreateRootCommand();
        ParseResult parseResult = rootCommand.Parse(args);

        if (parseResult.Errors.Count > 0)
        {
            foreach (ParseError error in parseResult.Errors)
            {
                DiagnosticReporter.WriteError(DiagnosticIds.ParseError, error.Message);
            }

            return Task.FromResult(ExitCodes.UsageError);
        }

        return parseResult.InvokeAsync();
    }

    private static RootCommand CreateRootCommand()
    {
        RootCommand rootCommand = new("Semantic code navigation for agents and automation.");
        rootCommand.Subcommands.Add(CheckCommand.Create());
        rootCommand.Subcommands.Add(OverviewCommand.Create());
        rootCommand.Subcommands.Add(SymbolsCommand.Create());
        rootCommand.Subcommands.Add(SymbolsInCommand.Create());
        rootCommand.Subcommands.Add(OutlineCommand.Create());
        rootCommand.Subcommands.Add(SymbolAtCommand.Create());
        rootCommand.Subcommands.Add(SymbolInfoCommand.Create());
        rootCommand.Subcommands.Add(DefinitionCommand.Create());
        rootCommand.Subcommands.Add(ReferencesCommand.Create());
        rootCommand.Subcommands.Add(ImplementationsCommand.Create());
        rootCommand.Subcommands.Add(TypeHierarchyCommand.Create());
        rootCommand.Subcommands.Add(CallersCommand.Create());
        rootCommand.Subcommands.Add(CallsCommand.Create());
        rootCommand.Subcommands.Add(FindCommand.Create());
        rootCommand.Subcommands.Add(WhereUsedCommand.Create());
        rootCommand.Subcommands.Add(AboutCommand.Create());
        rootCommand.Subcommands.Add(RelatedCommand.Create());
        rootCommand.Subcommands.Add(ImpactCommand.Create());
        rootCommand.Subcommands.Add(EntrypointsCommand.Create());
        rootCommand.Subcommands.Add(DiagnosticsCommand.Create());
        rootCommand.Subcommands.Add(BatchCommand.Create());
        return rootCommand;
    }
}
