using Microsoft.Extensions.Primitives;

namespace ExternalMemberCorpus.Modern10;

public sealed class HeaderPolicy
{
    public string LastHeader { get; set => field = value.Trim(); } = string.Empty;

    public bool IsMissing(StringValues values) => StringValues.IsNullOrEmpty(values);
}
