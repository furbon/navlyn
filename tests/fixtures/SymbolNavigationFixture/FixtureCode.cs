using AliasRunner = SymbolNavigationFixture.Runner;
using static SymbolNavigationFixture.WidgetText;

namespace SymbolNavigationFixture;

public partial class Widget
{
    public string Name { get; }

    public Widget(string name)
    {
        Name = name;
    }

    public string Format(int count)
    {
        return $"{Name}:{count}";
    }

    public string Format(string suffix)
    {
        return $"{Name}:{suffix}";
    }
}

public partial class Widget // Factory members
{
    public static Widget CreateDefault()
    {
        return new Widget("default");
    }
}

public static class WidgetExtensions
{
    public static string Describe(this Widget widget)
    {
        return widget.Format("extension");
    }
}

public static class WidgetText
{
    public static string Label(Widget widget)
    {
        return widget.Format("static");
    }
}

public interface IWidgetFormatter
{
    string FormatWidget(Widget widget);
}

public sealed class DefaultWidgetFormatter : IWidgetFormatter
{
    public string FormatWidget(Widget widget)
    {
        return widget.Format("interface");
    }
}

public interface IWidgetIdentity
{
    string GetName(Widget widget);
}

public sealed class ExplicitWidgetIdentity : IWidgetIdentity
{
    string IWidgetIdentity.GetName(Widget widget)
    {
        return $"explicit:{widget.Name}";
    }
}

public interface IWidgetProjector<T>
{
    T Project(Widget widget);
}

public sealed class NameProjector : IWidgetProjector<string>
{
    public string Project(Widget widget)
    {
        return $"project:{widget.Name}";
    }
}

public abstract class WidgetRenderer
{
    public abstract string Render(Widget widget);

    public virtual string DescribeWidget(Widget widget)
    {
        return $"base:{widget.Name}";
    }
}

public sealed class HtmlWidgetRenderer : WidgetRenderer
{
    public override string Render(Widget widget)
    {
        return $"<span>{widget.Name}</span>";
    }

    public override string DescribeWidget(Widget widget)
    {
        return $"html:{widget.Name}";
    }
}

public sealed class Runner
{
    public string Run()
    {
        Widget widget = Widget.CreateDefault();
        string formatted = widget.Format(3);
        string extensionFormatted = widget.Describe();
        string staticFormatted = Label(widget);
        return $"{formatted}|{extensionFormatted}|{staticFormatted}";
    }
}

public sealed class AliasConsumer
{
    public AliasRunner CreateRunner()
    {
        return new AliasRunner();
    }
}

public sealed class GenericBox<T>
{
    public GenericBox(T value)
    {
        Value = value;
    }

    public T Value { get; }
}

public sealed class SemanticEdgeCases
{
    private int explicitCounter;
    private EventHandler? explicitChanged;

    public event EventHandler? Changed;

    public int Counter { get; private set; }

    public event EventHandler? ExplicitChanged
    {
        add
        {
            explicitChanged += value;
        }
        remove
        {
            explicitChanged -= value;
        }
    }

    public int ExplicitCounter
    {
        get
        {
            return explicitCounter;
        }
        set
        {
            explicitCounter = value;
        }
    }

    public string Exercise()
    {
        GenericBox<int> box = new GenericBox<int>(1);
        int echoed = Echo<int>(box.Value);
        string optional = FormatOptional();
        string joined = JoinItems("a", "b");
        Func<string, string> normalize = input => input.Trim();
        string normalized = normalize(" value ");
        string directTrimmed = " direct ".Trim();
        object candidate = Widget.CreateDefault();
        string patternName = candidate is Widget matchedWidget ? matchedWidget.Name : "none";
        IWidgetFormatter formatter = new DefaultWidgetFormatter();
        string viaInterface = formatter.FormatWidget(Widget.CreateDefault());
        WidgetRenderer renderer = new HtmlWidgetRenderer();
        string rendered = renderer.Render(Widget.CreateDefault());
        var (left, right) = CreatePair();
        NumberBox first = new NumberBox(1);
        NumberBox second = new NumberBox(2);
        NumberBox total = first + second;
        int indexed = total[0];
        int converted = total;
        WidgetRecord record = new("record", 3);
        string recordName = record.Name;
        record.Deconstruct(out string recordLabel, out int recordCount);
        PrimaryWidget primary = new("primary");
        string primaryName = primary.Name;
        Widget targetTypedWidget = new("target");
        var inferredName = targetTypedWidget.Name;
        MarkedWidget marked = new();
        IncrementCounter();
        string local = BuildLocal("local");
        return $"{echoed}|{optional}|{joined}|{normalized}|{directTrimmed}|{patternName}|{viaInterface}|{rendered}|{left}|{right}|{indexed}|{converted}|{recordName}|{recordLabel}|{recordCount}|{primaryName}|{inferredName}|{marked}|{local}";

        static string BuildLocal(string value)
        {
            return value;
        }
    }

    public T Echo<T>(T value)
    {
        return value;
    }

    public string FormatOptional(string text = "optional")
    {
        return text;
    }

    public string JoinItems(params string[] items)
    {
        return string.Join(",", items);
    }

    public (string Left, string Right) CreatePair()
    {
        return ("left", "right");
    }

    public void IncrementCounter()
    {
        Counter = Counter + 1;
        ExplicitCounter = ExplicitCounter + 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Subscribe(SemanticEdgeCases source)
    {
        source.Changed += HandleChanged;
        source.Changed -= HandleChanged;
        source.ExplicitChanged += HandleChanged;
        source.ExplicitChanged -= HandleChanged;
    }

    private void HandleChanged(object? sender, EventArgs args)
    {
    }
}

public sealed class NumberBox
{
    private readonly int value;

    public NumberBox(int value)
    {
        this.value = value;
    }

    public int this[int index]
    {
        get
        {
            return value + index;
        }
    }

    public static NumberBox operator +(NumberBox left, NumberBox right)
    {
        return new NumberBox(left.value + right.value);
    }

    public static implicit operator int(NumberBox box)
    {
        return box.value;
    }
}

public record WidgetRecord(string Name, int Count);

public sealed class PrimaryWidget(string name)
{
    public string Name => name;
}

[WidgetMarker]
public sealed class MarkedWidget;

public sealed class WidgetMarkerAttribute : Attribute;
