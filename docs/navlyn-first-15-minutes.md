# Navlyn First 15 Minutes

This is an optional CLI walkthrough of several capabilities, not a required edit workflow. For a supplied literal or configuration value, use a normal read/search and stop. For an unresolved binding or relationship, ask for that precise semantic fact and stop when it is established. MCP defaults to four focused tools; preparation and verification below use the CLI or `--surface full`.

Use the [Windows first 10 minutes guide](navlyn-first-10-minutes.md) to install 0.9.1 into an isolated tool path and ask one semantic question using the installed absolute executable. This guide starts after that install.

## 0–3 Minutes: Diagnose

From the C# or Visual Basic repository you want to inspect:

```powershell
& 'C:\path\to\navlyn.exe' doctor --workspace auto
```

If `ok` is false, read `checks`, `workspace.diagnostics`, and `nextAction`. If automatic discovery is ambiguous, pass the intended `.slnx`, `.sln`, `.csproj`, or `.vbproj` path.

## 3–6 Minutes: Choose One Target

```powershell
& 'C:\path\to\navlyn.exe' target --workspace auto --query PaymentService --assume-kind NamedType --limit 10
```

Continue with one selected target or a deliberate disambiguation choice. Reuse its `candidateId`.

## 6–9 Minutes: Read Bounded Source

```powershell
& 'C:\path\to\navlyn.exe' read --workspace auto --candidate-id sym:v1:... --view declaration --max-lines 80
```

Use regular file reads or `rg` for prose, comments, strings, and docs. Use Navlyn when symbol identity or project context changes the edit.

## 9–12 Minutes: Prepare One Edit

```powershell
& 'C:\path\to\navlyn.exe' prepare-edit --workspace auto --candidate-id sym:v1:... --goal modify --change-kind behavior
```

Inspect `anchor`, `confidence`, `source`, `context`, `tests`, `knownUnknowns`, and `nextCommands`.

## 12–15 Minutes: Verify The Diff

```powershell
& 'C:\path\to\navlyn.exe' verify-edit --workspace auto --candidate-id sym:v1:... --fail-on-risk high
& 'C:\path\to\navlyn.exe' review --workspace auto --profile evidence
```

Guard policy failures are useful stop signals. Navlyn provides static source evidence; run the relevant tests separately.

Package and client support evidence is recorded in the [release contract](navlyn-release-contract.md#client-support-claims); configuration examples alone do not establish tested client support.
