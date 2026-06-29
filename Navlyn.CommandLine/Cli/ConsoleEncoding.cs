using System.Text;

namespace Navlyn.Cli;

internal static class ConsoleEncoding
{
    public static void ConfigureUtf8()
    {
        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = utf8;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
    }
}
