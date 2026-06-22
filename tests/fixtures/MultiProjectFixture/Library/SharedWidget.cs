namespace MultiProjectFixture.Library;

public sealed class SharedWidget
{
    public string Format(string value)
    {
        return $"shared:{value}";
    }
}
