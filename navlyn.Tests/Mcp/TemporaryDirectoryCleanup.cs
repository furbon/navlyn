namespace Navlyn.Tests.Mcp;

internal static class TemporaryDirectoryCleanup
{
    public static void Delete(string path)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (OperatingSystem.IsWindows() && attempt < 39)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && attempt < 39)
            {
                Thread.Sleep(250);
            }
        }
    }
}
