# Navlyn VS Code setup helper

This self-contained bundle configures the Navlyn MCP server for a VS Code workspace. PowerShell 7 and the .NET SDK are required.

Start with a no-write plan:

```powershell
./setup-navlyn.ps1 -Workspace 'C:/src/my project' -WorkspaceFile 'C:/src/my project/MyApp.slnx' -Version 0.8.4
```

Apply only after reviewing the displayed workspace, exact version, feed, config path and effects:

```powershell
./setup-navlyn.ps1 -Workspace 'C:/src/my project' -WorkspaceFile 'C:/src/my project/MyApp.slnx' -Version 0.8.4 -Apply
```

Use `-Action Update`, `-Action Remove`, or `-Action Undo` for lifecycle operations. A local package feed can be selected with `-Feed <directory>`. The default installation is task-owned under the current user's local application data, keyed by workspace path. The helper edits `.vscode/mcp.json`, keeps unrelated JSONC text intact, and refuses entries it cannot prove it owns. It does not change client trust settings.

`-Target Local` is the default. `-Target Global` installs or updates the current user's global `navlyn-mcp` package, or registers an existing version. It resolves the global tools directory from `DOTNET_CLI_HOME/.dotnet/tools` when set, or the user profile's `.dotnet/tools` otherwise. Newer installed versions are retained unless `-AllowDowngrade` is supplied. Apply uses the exact requested version and selected feed, confirms SDK package metadata, and tests the exact resolved shim before committing the workspace registration.

Global changes retain verified snapshots of only `.store/navlyn-mcp` and package-declared command shims. Undo restores the previous package and registration; repeated Undo walks prior transactions. Remove restores the package state from before this workspace's owned changes and keeps unrelated tools and JSONC entries. Registration of a reused package does not transfer package ownership. Package restoration or removal is refused while another owned workspace registration references it, or if package/config hashes or retained snapshots have changed. An interrupted SDK operation whose resulting files were not inventoried requires explicit manual reconciliation; the helper retains its pending record rather than guessing.

The included `integrity.json` identifies and hashes every shipped source file and records the reviewed source commit. Verify these hashes before distributing the bundle.
