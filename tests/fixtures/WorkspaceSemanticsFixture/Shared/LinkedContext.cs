namespace WorkspaceSemantics.Linked;

public sealed class LinkedContext
{
    public string ReadProject()
    {
#if LINKED_ALPHA
        AlphaLinkedOnly value = new AlphaLinkedOnly();
        return value.Name;
#elif LINKED_BETA
        BetaLinkedOnly value = new BetaLinkedOnly();
        return value.Name;
#else
        return "unknown";
#endif
    }
}

#if LINKED_ALPHA
public sealed class AlphaLinkedOnly
{
    public string Name => nameof(AlphaLinkedOnly);
}
#elif LINKED_BETA
public sealed class BetaLinkedOnly
{
    public string Name => nameof(BetaLinkedOnly);
}
#endif
