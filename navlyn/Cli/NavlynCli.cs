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
                DiagnosticReporter.WriteError(DiagnosticIds.ParseError, NormalizeParseErrorMessage(error.Message));
            }

            if (args.Length == 0)
            {
                Console.Error.WriteLine();
                WriteRootHelp(rootCommand);
            }

            return Task.FromResult(ExitCodes.UsageError);
        }

        return parseResult.InvokeAsync();
    }

    private static void WriteRootHelp(RootCommand rootCommand)
    {
        ParseResult helpParseResult = rootCommand.Parse(["--help"]);
        helpParseResult.Invoke(new InvocationConfiguration { Output = Console.Error });
    }

    private static string NormalizeParseErrorMessage(string message)
    {
        return message.EndsWith("。.", StringComparison.Ordinal) ? message[..^1] : message;
    }

    private static RootCommand CreateRootCommand()
    {
        RootCommand rootCommand = new("Semantic code navigation and investigation for agents and automation.");
        rootCommand.Subcommands.Add(CheckCommand.Create());
        rootCommand.Subcommands.Add(OverviewCommand.Create());
        rootCommand.Subcommands.Add(RepoGraphCommand.Create());
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
        rootCommand.Subcommands.Add(FrameworkEntrypointsCommand.Create());
        rootCommand.Subcommands.Add(ChangedSymbolsCommand.Create());
        rootCommand.Subcommands.Add(ImpactDiffCommand.Create());
        rootCommand.Subcommands.Add(DiagnosticsDiffCommand.Create());
        rootCommand.Subcommands.Add(ReviewDiffCommand.Create());
        rootCommand.Subcommands.Add(ReviewPackCommand.Create());
        rootCommand.Subcommands.Add(ContextPackCommand.Create());
        rootCommand.Subcommands.Add(PublicApiDiffCommand.Create());
        rootCommand.Subcommands.Add(TestsForSymbolCommand.Create());
        rootCommand.Subcommands.Add(TestsForDiffCommand.Create());
        rootCommand.Subcommands.Add(DiGraphCommand.Create());
        rootCommand.Subcommands.Add(WhereRegisteredCommand.Create());
        rootCommand.Subcommands.Add(DiImpactCommand.Create());
        rootCommand.Subcommands.Add(DiagnosticsCommand.Create());
        rootCommand.Subcommands.Add(BatchCommand.Create());
        return rootCommand;
    }
}
