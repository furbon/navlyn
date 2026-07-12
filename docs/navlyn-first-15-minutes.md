# Navlyn First 15 Minutes

This path proves Navlyn can anchor one C# or Visual Basic edit before an agent changes code.

## 0-3 Minutes: Install And Diagnose

```powershell
dotnet tool install --global navlyn --version 0.7.0
dotnet tool install --global navlyn-mcp --version 0.7.0
navlyn doctor --workspace path/to/YourRepo.sln
```

`doctor` should return JSON. If `ok` is false, inspect `checks`, `workspace.diagnostics`, and `nextAction` before continuing.

## 3-6 Minutes: Choose One Target

```powershell
navlyn target --workspace path/to/YourRepo.sln --query PaymentService --assume-kind NamedType --limit 10
```

Continue only with one selected target or a deliberate disambiguation choice. Reuse the returned `candidateId`.

## 6-9 Minutes: Read Bounded Source

```powershell
navlyn read --workspace path/to/YourRepo.sln --candidate-id sym:v1:... --view declaration --max-lines 80
```

Use normal file reads or `rg` for prose, comments, strings, and docs. Use Navlyn when symbol identity or project context changes the edit.

## 9-12 Minutes: Prepare One Edit

```powershell
navlyn prepare-edit --workspace path/to/YourRepo.sln --candidate-id sym:v1:... --goal modify --change-kind behavior
```

Inspect `anchor`, `confidence`, `source`, `context`, `tests`, `knownUnknowns`, and `nextCommands`.

## 12-15 Minutes: Verify The Diff

```powershell
navlyn verify-edit --workspace path/to/YourRepo.sln --candidate-id sym:v1:... --fail-on-risk high
navlyn review --workspace path/to/YourRepo.sln --profile evidence
```

Guard policy failures are useful stop signals. Navlyn is static source evidence; run the relevant tests separately.
