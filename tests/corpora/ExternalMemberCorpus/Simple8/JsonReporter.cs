using Newtonsoft.Json;

namespace ExternalMemberCorpus.Simple8;

public sealed class JsonReporter(string prefix)
{
    public string Report(object value) => prefix + JsonConvert.SerializeObject(value, Formatting.Indented);

    public string Normalize(string value) => value.Trim();
}
