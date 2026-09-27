using Microsoft.Extensions.Primitives;

namespace ExternalMemberCorpus.Scale;

public sealed class Entry
{
    public bool Check(StringValues value) => StringValues.IsNullOrEmpty(value);
}
