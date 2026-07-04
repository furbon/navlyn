using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Navlyn.Workspaces;

internal static class DocumentIndexProvider
{
    private static readonly ConditionalWeakTable<Solution, DocumentIndex> Indexes = new();

    public static DocumentIndex GetOrCreate(Solution solution)
    {
        return Indexes.GetValue(solution, DocumentIndex.Create);
    }
}
