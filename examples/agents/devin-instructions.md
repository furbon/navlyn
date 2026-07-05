# Agent Instructions

- Install Navlyn as repository-local .NET tools and run `dotnet tool restore` before invoking commands.
- Use normal file reads and `rg` first when text is enough.
- Run `dotnet tool run navlyn -- doctor --workspace <workspace>` before first semantic work or when setup looks unhealthy.
- Use Navlyn only when C# or Visual Basic semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- Before a non-trivial C# or Visual Basic edit, run `dotnet tool run navlyn -- edit-preflight --workspace <workspace> --query <SymbolName> --assume-kind <kind> --goal modify`.
- After editing, run `dotnet tool run navlyn -- post-edit-guard --workspace <workspace> --candidate-id <candidateId> --fail-on-risk high` or `wrong-symbol-guard`.
- For MCP, start `dotnet tool run navlyn-mcp -- --workspace <workspace> --tool-profile reader`; restart with `edit` or `review` only when the task needs that broader surface.
- Keep generated reports under ignored artifact paths and keep Navlyn stdout as JSON.
