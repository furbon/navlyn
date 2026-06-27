using System.Reflection;
using Navlyn.Diagnostics;

namespace Navlyn.Tests.Diagnostics;

public sealed class DiagnosticIdsTests
{
    [Fact]
    public void NumericDiagnosticIds_AreUniqueAndPositive()
    {
        FieldInfo[] fields = typeof(DiagnosticIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(int))
            .ToArray();

        int[] ids = fields
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToArray();

        Assert.All(ids, id => Assert.True(id > 0));
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void Prefix_UsesStableNavlynPrefix()
    {
        Assert.Equal("NAVLYN", DiagnosticIds.Prefix);
    }
}
