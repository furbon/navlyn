using Navlyn.ExternalFixture;

Probe probe = new();
string selected = probe.Pick(7);
string genericSelected = new GenericProbe<int>().Select<long>(7);
NoBody noBody = null!;
string abstractSelected = noBody.Read(7);
string frameworkSelected = " fixture ".Trim();
string localSelected = new LocalProbe().Pick(7);
string extensionSelected = probe.Extend(7);
int byRefValue = 7;
string byRefSelected = probe.Adjust(ref byRefValue, out int byRefCopy);
string optionalSelected = probe.Optional();
Probe constructed = new(7);
string constructorSelected = constructed.ConstructorMarker;
string accessorSelected = probe.Accessed;
string mutableRead = probe.Mutable;
probe.Mutable = "one";
probe.Mutable += "two";
(probe.Mutable) = "wrapped";
((probe.Mutable)) = "double wrapped";
(probe.Mutable) += "wrapped";
(probe.Counter)++;
((probe.Counter))--;
Probe? maybeProbe = probe;
maybeProbe?.Mutable = "conditional";
maybeProbe?.Mutable += "conditional";
string nestedAccessorRead = probe.NestedAccessor;
int normalizedShort = probe.Normalize((short)-1);
long normalizedLong = probe.Normalize((long)-1);
int normalizedWide = probe.NormalizeWide((short)-1);
int normalizedNegative = probe.NormalizeNegative((short)-1);
var explicitConstructed = new Probe(7);
var ambiguousConstructed = new Probe((string)null!);
var brokerPlatforms = Probe.BrokerPlatforms.None;

public sealed class LocalProbe
{
    public string Pick(int value) => "LOCAL_SOURCE_PRIORITY_BODY";
}
