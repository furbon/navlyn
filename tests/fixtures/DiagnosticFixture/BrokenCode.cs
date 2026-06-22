namespace DiagnosticFixture;

public sealed class BrokenCode
{
    public MissingType Create()
    {
        return new MissingType();
    }
}
