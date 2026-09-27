using Navlyn.Cli;
using Navlyn.Symbols;

ConsoleEncoding.ConfigureUtf8();

if (await ExternalMemberWorker.RunIfRequestedAsync(args, CancellationToken.None))
{
    return 0;
}

return await NavlynCli.RunAsync(args);
