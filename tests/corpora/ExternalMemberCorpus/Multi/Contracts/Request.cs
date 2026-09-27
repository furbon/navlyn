using Microsoft.Extensions.Primitives;

namespace ExternalMemberCorpus.Multi.Contracts;

public sealed record Request(string Name, StringValues Tags);
