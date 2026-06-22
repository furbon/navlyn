namespace Navlyn.Diagnostics;

internal static class DiagnosticReporter
{
    public static void WriteError(int id, string message)
    {
        Console.Error.WriteLine($"{DiagnosticIds.Prefix}{id:D4}: {message}");
    }

    public static void WriteError(int id, string kind, string message)
    {
        WriteError(id, $"{kind}: {message}");
    }
}
