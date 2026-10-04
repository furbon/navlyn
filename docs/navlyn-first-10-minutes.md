# Try Navlyn on Windows

[日本語](navlyn-first-10-minutes_ja.md)

You need [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) and a [.NET SDK](https://dotnet.microsoft.com/download) that can load the repository you want to inspect. If the repository has a `global.json`, check its requested SDK version.

## 1. Install the tools

Run this in PowerShell. The unique temporary directory keeps this installation separate from your existing .NET tools.

```powershell
$tools = Join-Path $env:TEMP "navlyn-tools-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $tools | Out-Null
dotnet tool install navlyn --tool-path $tools --version 0.9.1
dotnet tool list --tool-path $tools
```

Check that the list shows `navlyn` at `0.9.1`.

## 2. Run it in a repository

Replace `C:\path\to\your-repository` with the absolute path of a C# or Visual Basic repository.

```powershell
Set-Location 'C:\path\to\your-repository'
& (Join-Path $tools 'navlyn.exe') doctor --workspace auto
```

If `doctor` selects a workspace, search for a type that actually exists in that repository. Replace the example `PaymentService`:

```powershell
& (Join-Path $tools 'navlyn.exe') target --workspace auto --query PaymentService --assume-kind NamedType --limit 10
```

Copy a `candidateId` from the resulting JSON to read its declaration. Replace `sym:v1:...` with the returned value:

```powershell
& (Join-Path $tools 'navlyn.exe') read --workspace auto --candidate-id 'sym:v1:...' --view declaration --max-lines 80
```

## If something fails

- Check `dotnet --list-sdks`. If `NAVLYN1201` mentions `global.json`, install the SDK requested by that file.
- If discovery finds several workspaces, replace `--workspace auto` with an explicit path such as `--workspace .\YourRepo.slnx`. A `.sln`, `.csproj`, or `.vbproj` path also works.
- If the executable cannot be found, use the `Join-Path $tools` form above and check `dotnet tool list --tool-path $tools`.
- You can remove the temporary installation with `Remove-Item -LiteralPath $tools -Recurse` after checking the value of `$tools`.

To use Navlyn from an AI tool, continue with [client setup](navlyn-client-setup.md). VS Code users can choose the [setup bundle](navlyn-client-setup.md#vs-code-setup-bundle), review its no-write plan, then apply and check a live tool call. Manual configuration remains available. For repositories with several solutions, see [workspace configuration](navlyn-workspace.md).
