namespace Navlyn.ExternalFixture;

public sealed class Probe
{
    private string mutableValue = string.Empty;
    private int counterValue;
    public Probe() { }
    public Probe(int value) { ConstructorMarker = "FIXTURE_CONSTRUCTOR_BODY"; }
    public Probe(string value) { ConstructorMarker = "FIXTURE_STRING_CONSTRUCTOR_BODY"; }
    public Probe(System.Uri value) { ConstructorMarker = "FIXTURE_URI_CONSTRUCTOR_BODY"; }
    public enum BrokerPlatforms { None = 0, Windows = 1, Linux = 2 }
    public string Pick(int value) => RuntimeMarker;
    public string Pick(string value) => "FIXTURE_STRING_OVERLOAD_BODY";
    public int Normalize(short value) => checked((value < 0 ? -value : value) * 37 + 211);
    public long Normalize(long value) => value % 17 + 911;
    public int NormalizeWide(short value) => checked((value < 0 ? -value : value) * 100000 + 211);
    public int NormalizeNegative(short value) => checked((value < 0 ? -value : value) * -37 + 211);
    private static int HiddenNormalize(short value) => checked((value < 0 ? -value : value) * 41 + 19);
    private static long HiddenNormalize(long value) => value % 19;
    public string Adjust(ref int value, out int copy) { copy = value; return "FIXTURE_BYREF_BODY"; }
    public string Adjust(string value) => "FIXTURE_BYREF_STRING_OVERLOAD_BODY";
    public string Optional(int value = 7) => "FIXTURE_OPTIONAL_INT_BODY";
    public string Optional(string value) => "FIXTURE_OPTIONAL_STRING_BODY";
    public string Accessed => "FIXTURE_PROPERTY_GETTER_BODY";
    public string Mutable
    {
        get => "FIXTURE_MUTABLE_GETTER_BODY";
        set { mutableValue = "FIXTURE_MUTABLE_SETTER_BODY"; }
    }
    public int Counter
    {
        get => "FIXTURE_COUNTER_GETTER_BODY".Length;
        set { counterValue = "FIXTURE_COUNTER_SETTER_BODY".Length; }
    }
    public string NestedAccessor
    {
        get
        {
            string Local() => "FIXTURE_NESTED_LOCAL_FUNCTION_BODY";
            return "FIXTURE_NESTED_ACCESSOR_OUTER_BODY" + Local();
        }
    }
    public string ConstructorMarker { get; } = string.Empty;
#if WINDOWS
    private const string RuntimeMarker = "FIXTURE_WINDOWS_INT_OVERLOAD_BODY";
#else
    private const string RuntimeMarker = "FIXTURE_NET10_INT_OVERLOAD_BODY";
#endif
}

public static class ProbeExtensions
{
    public static string Extend(this Probe probe, int value) => "FIXTURE_EXTENSION_INT_BODY";
    public static string Extend(this Probe probe, string value) => "FIXTURE_EXTENSION_STRING_BODY";
}

public sealed class GenericProbe<T>
{
    public GenericProbe() { ConstructorMarker = "FIXTURE_GENERIC_CONSTRUCTOR_BODY"; }
    public string ConstructorMarker { get; }
    public T Echo(T value) => value;
    public string Select<TValue>(int value) => "FIXTURE_GENERIC_INT_OVERLOAD_BODY";
    public string Select<TValue>(string value) => "FIXTURE_GENERIC_STRING_OVERLOAD_BODY";
}

public abstract class NoBody
{
    public abstract string Read(int value);
}
