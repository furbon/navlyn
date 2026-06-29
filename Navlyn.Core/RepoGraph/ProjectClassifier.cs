namespace Navlyn.RepoGraph;

internal static class ProjectClassifier
{
    public static RepoGraphProjectClassification Classify(
        string name,
        string? path,
        string? assemblyName,
        ProjectFileFacts facts)
    {
        List<string> reasonCodes = [];
        HashSet<string> packages = new(facts.PackageReferences.Select(package => package.Name), StringComparer.OrdinalIgnoreCase);
        string haystack = $"{name} {path} {assemblyName}";

        if (packages.Contains("Microsoft.NET.Test.Sdk"))
        {
            reasonCodes.Add("test-sdk-package");
        }

        if (packages.Contains("xunit") || packages.Contains("xunit.v3"))
        {
            reasonCodes.Add("xunit-package");
        }

        if (packages.Contains("NUnit"))
        {
            reasonCodes.Add("nunit-package");
        }

        if (packages.Contains("MSTest.TestFramework"))
        {
            reasonCodes.Add("mstest-package");
        }

        if (packages.Contains("BenchmarkDotNet"))
        {
            reasonCodes.Add("benchmarkdotnet-package");
        }

        if (facts.PackAsTool)
        {
            reasonCodes.Add("pack-as-tool");
        }

        if (string.Equals(facts.OutputType, "Exe", StringComparison.OrdinalIgnoreCase))
        {
            reasonCodes.Add("output-type-exe");
        }
        else if (string.Equals(facts.OutputType, "Library", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(facts.OutputType))
        {
            reasonCodes.Add("output-type-library");
        }

        if (ContainsTestToken(name))
        {
            reasonCodes.Add("project-name-test");
        }

        if (path is not null && ContainsTestToken(path))
        {
            reasonCodes.Add("project-path-test");
        }

        if (assemblyName is not null && ContainsTestToken(assemblyName))
        {
            reasonCodes.Add("assembly-name-test");
        }

        if (string.Equals(facts.Sdk, "Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
        {
            reasonCodes.Add("sdk-web");
        }

        if (reasonCodes.Count == 0)
        {
            return new RepoGraphProjectClassification("unknown", "low", ["no-classification-signals"]);
        }

        if (reasonCodes.Any(IsTestReason))
        {
            return new RepoGraphProjectClassification("test", Confidence(reasonCodes, packageBacked: true), Ordered(reasonCodes));
        }

        if (reasonCodes.Contains("benchmarkdotnet-package"))
        {
            return new RepoGraphProjectClassification("benchmark", "high", Ordered(reasonCodes));
        }

        if (reasonCodes.Contains("pack-as-tool"))
        {
            return new RepoGraphProjectClassification("tooling", "high", Ordered(reasonCodes));
        }

        if (reasonCodes.Contains("output-type-exe"))
        {
            return new RepoGraphProjectClassification("executable", "high", Ordered(reasonCodes));
        }

        if (reasonCodes.Contains("output-type-library"))
        {
            return new RepoGraphProjectClassification("library", "medium", Ordered(reasonCodes));
        }

        _ = haystack;
        return new RepoGraphProjectClassification("unknown", "low", Ordered(reasonCodes));
    }

    private static bool IsTestReason(string reason)
    {
        return reason is "test-sdk-package" or "xunit-package" or "nunit-package" or "mstest-package" or
            "project-name-test" or "project-path-test" or "assembly-name-test";
    }

    private static string Confidence(IReadOnlyList<string> reasons, bool packageBacked)
    {
        return reasons.Any(reason => reason is "test-sdk-package" or "xunit-package" or "nunit-package" or "mstest-package")
            ? "high"
            : packageBacked ? "medium" : "low";
    }

    private static bool ContainsTestToken(string value)
    {
        return value.Contains("test", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> Ordered(IReadOnlyList<string> reasons)
    {
        return [.. reasons
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Priority)
            .ThenBy(reason => reason, StringComparer.Ordinal)];
    }

    private static int Priority(string reason)
    {
        return reason switch
        {
            "test-sdk-package" => 0,
            "xunit-package" => 1,
            "nunit-package" => 2,
            "mstest-package" => 3,
            "benchmarkdotnet-package" => 4,
            "pack-as-tool" => 5,
            "output-type-exe" => 6,
            "output-type-library" => 7,
            "project-name-test" => 8,
            "project-path-test" => 9,
            "assembly-name-test" => 10,
            "sdk-web" => 11,
            _ => 100
        };
    }
}
