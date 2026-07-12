# Agent Instructions

- Install Navlyn as repository-local .NET tools and run `dotnet tool restore` before invoking commands.
- Use normal file reads and `rg` first when text is enough.
- Run `dotnet tool run navlyn -- doctor --workspace auto` before first semantic work or when setup looks unhealthy.
- Use Navlyn only when C# or Visual Basic semantic identity, project context, source relationships, diff facts, or bounded evidence would change the answer.
- Before a non-trivial C# or Visual Basic edit, run `dotnet tool run navlyn -- prepare-edit --workspace auto --query <SymbolName> --assume-kind <kind> --goal modify`.
- After editing, run `dotnet tool run navlyn -- verify-edit --workspace auto --candidate-id <candidateId> --fail-on-risk high` or `wrong-symbol-guard`.
- For MCP, start `dotnet tool run navlyn-mcp`; use the unified read-only tool surface and call edit or review evidence tools only when the task needs them.
- Keep generated reports under ignored artifact paths and keep Navlyn stdout as JSON.
