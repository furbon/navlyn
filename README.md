# Navlyn

日本語はこちら: [`README_ja.md`](README_ja.md)

Navlyn is a semantic code navigation CLI for C#/.NET repositories, built for agents, automation, and developers who need machine-readable answers about source code.

It does not replace text search. Use tools like `rg` for comments, strings, documentation, and non-C# files. Use Navlyn when you need Roslyn-backed facts about C# symbols, definitions, references, implementations, diagnostics, calls, and workspace structure.

## What It Can Do

- Load `.slnx`, `.sln`, and `.csproj` workspaces.
- Report workspace summaries and compiler diagnostics as JSON.
- Find C# symbols by name, position, file span, or fuzzy query.
- Resolve definitions, references, implementations, type hierarchy, callers, and outgoing calls.
- Inspect semantic outlines and detailed symbol facts.
- Run multiple navigation requests in one workspace load with `batch`.
- Include generated code by default, with `--exclude-generated` where filtering applies.

## Getting Started

Run Navlyn from the repository root:

```powershell
dotnet restore navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
```

Pack it as a local .NET tool:

```powershell
dotnet pack navlyn\navlyn.csproj
dotnet new tool-manifest
dotnet tool install navlyn --add-source .\navlyn\bin\Release --version 0.1.0
dotnet tool run navlyn check --workspace navlyn.slnx
```

## Basic Usage

```powershell
dotnet run --no-launch-profile --project navlyn -- overview --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- diagnostics --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- find --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
dotnet run --no-launch-profile --project navlyn -- definition --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --column 37
dotnet run --no-launch-profile --project navlyn -- references --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --column 37
```

For the full command contract and JSON shapes, see [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md).

## Usage Hints

- Navlyn writes command results to stdout as JSON.
- Errors, warnings, progress, and diagnostics go to stderr.
- Paths are repository-relative where possible.
- User-facing line and column values are 1-based.
- Source location end positions use 1-based exclusive `endLine` and `endColumn` fields where available.

## Japanese README

日本語版は [`README_ja.md`](README_ja.md) にあります。
