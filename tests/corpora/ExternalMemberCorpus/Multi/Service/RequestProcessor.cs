using ExternalMemberCorpus.Multi.Contracts;
using Serilog;

namespace ExternalMemberCorpus.Multi.Service;

public sealed class RequestProcessor
{
    public string Process(Request request)
    {
        Log.Information("Processing {Name}", request.Name);
        return request.Tags.ToString();
    }
}
