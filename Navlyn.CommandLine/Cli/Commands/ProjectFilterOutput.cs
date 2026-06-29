using System.Text.Json.Serialization;
using Navlyn.Workspaces;

namespace Navlyn.Cli.Commands;

internal sealed record ProjectFilterOutput(
    string Filter,
    string Name,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetFramework)
{
    public static ProjectFilterOutput FromAppliedFilter(AppliedProjectFilter filter)
    {
        return new ProjectFilterOutput(filter.Filter, filter.Name, filter.Path, filter.TargetFramework);
    }
}
