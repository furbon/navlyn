using System.Text.Json;

namespace Navlyn.Cli;

internal static class ConsoleJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, Options));
    }
}
