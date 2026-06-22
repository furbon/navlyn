using MultiProjectFixture.Library;

namespace MultiProjectFixture.App;

public sealed class CrossProjectRunner
{
    public string Run()
    {
        SharedWidget widget = new SharedWidget();
        return widget.Format("agent");
    }
}
