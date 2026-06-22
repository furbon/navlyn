namespace WorkspaceSemantics.Conditional;

public sealed class ConditionalEntry
{
    public string Read()
    {
#if NAVLYN_ACTIVE_BRANCH
        ActiveBranchSymbol active = new ActiveBranchSymbol();
        return active.Name;
#else
        InactiveBranchSymbol inactive = new InactiveBranchSymbol();
        return inactive.Name;
#endif
    }
}

#if NAVLYN_ACTIVE_BRANCH
public sealed class ActiveBranchSymbol
{
    public string Name => nameof(ActiveBranchSymbol);
}
#else
public sealed class InactiveBranchSymbol
{
    public string Name => nameof(InactiveBranchSymbol);
}
#endif
