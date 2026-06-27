namespace Navlyn.Tests.TestSupport;

internal static class ResolverAssert
{
    public static void Location(SourcePosition expected, int line, int column, int endLine, int endColumn)
    {
        Assert.Equal(expected.Line, line);
        Assert.Equal(expected.Column, column);
        Assert.Equal(expected.EndLine, endLine);
        Assert.Equal(expected.EndColumn, endColumn);
    }

    public static void PathEndsWith(string? actual, SourceFixtureFile expected)
    {
        Assert.False(string.IsNullOrWhiteSpace(actual));

        string normalizedActual = NormalizePath(actual);
        string normalizedExpected = NormalizePath(expected.RelativePath);
        Assert.EndsWith(normalizedExpected, normalizedActual, StringComparison.OrdinalIgnoreCase);
    }

    public static T NoError<T, TError>(T? resolution, TError? error)
        where T : class
    {
        Assert.Null(error);
        return Assert.IsType<T>(resolution);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
