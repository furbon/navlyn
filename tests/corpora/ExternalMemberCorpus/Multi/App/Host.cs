using ExternalMemberCorpus.Multi.Contracts;
using ExternalMemberCorpus.Multi.Service;
using Microsoft.Extensions.Primitives;

namespace ExternalMemberCorpus.Multi.App;

public sealed class Host
{
    public string Run(string name) => new RequestProcessor().Process(new Request(name, new StringValues("ready")));
}
