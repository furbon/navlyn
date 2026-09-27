using Newtonsoft.Json;

namespace ExternalMemberCorpus.Direct;

public sealed class DirectFormatter
{
    public string Format(object value) => JsonConvert.SerializeObject(value);
}
