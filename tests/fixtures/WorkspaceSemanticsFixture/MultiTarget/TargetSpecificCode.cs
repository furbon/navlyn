namespace WorkspaceSemantics.MultiTarget;

public sealed class TargetSpecificWidget
{
    public string ReadTarget()
    {
#if NAVLYN_TFM_NET10
        Net10OnlyValue value = new Net10OnlyValue();
        return value.Name;
#elif NAVLYN_TFM_NETSTANDARD
        NetStandardOnlyValue value = new NetStandardOnlyValue();
        return value.Name;
#else
        return "unknown";
#endif
    }
}

#if NAVLYN_TFM_NET10
public sealed class Net10OnlyValue
{
    public string Name => nameof(Net10OnlyValue);
}
#elif NAVLYN_TFM_NETSTANDARD
public sealed class NetStandardOnlyValue
{
    public string Name => nameof(NetStandardOnlyValue);
}
#endif
