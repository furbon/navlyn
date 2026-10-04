using Navlyn.Symbols;

namespace Navlyn.Mcp.Tools;

// Request-local bounds reach the direct resolver before facts and JSON are built.
internal sealed class NavlynMcpResponseScope : IDisposable
{
    private static readonly AsyncLocal<NavlynMcpResponseScope?> Slot = new();
    private readonly NavlynMcpResponseScope? previous;
    private readonly OutlinePage? page;

    private NavlynMcpResponseScope(int? entryLimit, int entryOffset)
    {
        previous = Slot.Value;
        page = entryLimit is not null || entryOffset != 0
            ? new OutlinePage(entryOffset, entryLimit ?? int.MaxValue) : null;
        Slot.Value = this;
    }

    public static OutlinePage? CurrentOutlinePage => Slot.Value?.page;
    public static NavlynMcpResponseScope Begin(int? entryLimit, int entryOffset) => new(entryLimit, entryOffset);
    public void Dispose() => Slot.Value = previous;
}
