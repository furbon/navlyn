# AI ツールから Navlyn を使う

[English](navlyn-client-setup.md)

MCP は、AI ツールが外部のコマンドを呼び出すための仕組みです。Navlyn では `navlyn-mcp` がそのコマンドです。設定後、AI ツールから `navlyn_target` などを呼べます。

以下は Windows の手順です。[PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) と、調べたいリポジトリに合う [.NET SDK](https://dotnet.microsoft.com/download) を用意します。

`dotnet tool list --global` で `navlyn-mcp` が導入済みか確認してください。未導入なら以下を実行します。同じ版があればインストールを省略し、古い版なら `dotnet tool update --global navlyn-mcp --version 0.8.1` を実行します。

```powershell
dotnet tool install --global navlyn-mcp --version 0.8.1
$mcpExe = Join-Path $HOME '.dotnet/tools/navlyn-mcp.exe'
Test-Path $mcpExe
$mcpExe
```

導入済みの場合も、上の `$mcpExe` 以降を実行します。`Test-Path` が `True` を返したら、最後に表示されたパスで以下の `C:\path\to\navlyn-mcp.exe` を置き換えます。調べたいリポジトリを開いてから設定してください。

## VS Code と GitHub Copilot

[VS Code の GitHub Copilot 導入ページ](https://code.visualstudio.com/docs/copilot/setup)に従ってサインインし、Copilot Chat を使える状態にします。MCP の操作は [VS Code の設定資料](https://code.visualstudio.com/docs/agent-customization/mcp-servers)にもあります。

1. VS Code で調べたいリポジトリを開きます。
2. そのリポジトリに `.vscode/mcp.json` を作り、以下を保存します。実際の実行ファイルのパスに置き換え、JSON 内の `\` はそのまま二つ重ねます。
3. コマンドパレットから `MCP: List Servers` を開き、`navlyn` を起動します。信頼確認が出たら、実行ファイルのパスを確認して承認します。
4. Copilot Chat を `Agent` モードにし、ツールの一覧で Navlyn を有効にします。「Navlyn の `navlyn_target` で、このリポジトリの実在する型を一つ調べて」と依頼し、ツール呼び出しと結果を確認します。

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "C:\\path\\to\\navlyn-mcp.exe",
      "args": ["--working-directory", "${workspaceFolder}"]
    }
  }
}
```

接続できない場合は、実行ファイルのパス、VS Code で開いたフォルダー、`MCP: List Servers` に表示されるエラーを確認してください。設定を外すには、`.vscode/mcp.json` の `navlyn` 項目を削除します。

## GitHub Copilot CLI

[Copilot CLI の導入ページ](https://docs.github.com/en/copilot/how-tos/copilot-cli/install-copilot-cli)で CLI を入れ、サインインします。[MCP の設定方法](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers)も参照してください。リポジトリのルートに `.mcp.json` を作り、次を保存します。パスと `C:\\path\\to\\your-repository` を置き換えます。

```json
{
  "mcpServers": {
    "navlyn": {
      "type": "local",
      "command": "C:\\path\\to\\navlyn-mcp.exe",
      "args": ["--working-directory", "C:\\path\\to\\your-repository"]
    }
  }
}
```

そのリポジトリで `copilot` を起動し、リポジトリの信頼確認に応じます。`/mcp list` で `navlyn` が表示されるか確認してから、`navlyn_target` で実在する型を調べさせ、ツールの結果を確認します。ターミナルからは `copilot mcp list` でも一覧を確認できます。

未信頼のフォルダーでプロンプトモードからリポジトリの MCP 設定を使う場合は、起動前に `$env:GITHUB_COPILOT_PROMPT_MODE_WORKSPACE_MCP = 'true'` を設定します。

`.vscode/mcp.json` は Copilot CLI の設定ファイルではありません。設定を外すには `.mcp.json` の項目を削除します。

## Codex

[Codex の導入ページ](https://developers.openai.com/codex/quickstart)に従って Codex を使える状態にします。PowerShell で次を実行し、`$mcpExe` を実際の `navlyn-mcp.exe` の絶対パスに置き換えます。

```powershell
$mcpExe = 'C:\path\to\navlyn-mcp.exe'
codex mcp add navlyn -- $mcpExe
codex mcp list
```

調べたいリポジトリから Codex を起動し、`navlyn_target` で実在する型を調べさせます。起動位置がリポジトリ外なら、登録時に `--working-directory C:\path\to\your-repository` を実行ファイルの後ろに付けます。設定を外すには `codex mcp remove navlyn` を使います。[Codex の MCP 設定資料](https://developers.openai.com/codex/mcp)にもコマンドがあります。

Codex が Navlyn を使う場面を判断するための補助ファイルは、[Codex 用スキルの導入](navlyn-codex-routing-skill_ja.md)を参照してください。

## Claude Code

[Claude Code の導入ページ](https://code.claude.com/docs/en/quickstart)に従って導入し、サインインします。調べたいリポジトリで次を実行します。二つのパスを置き換えてください。

```powershell
claude mcp add --transport stdio navlyn -- 'C:\path\to\navlyn-mcp.exe' --working-directory 'C:\path\to\your-repository'
claude mcp list
```

Claude Code 内で `/mcp` を開き、`navlyn` の接続を確認します。その後、`navlyn_target` で実在する型を調べさせ、ツールの結果を確認します。設定を外すには `claude mcp remove navlyn` を使います。詳しくは[公式 MCP 設定資料](https://code.claude.com/docs/en/mcp)を参照してください。

## 接続後

`navlyn_target` が対象を見つけられない場合は、リポジトリのルートで [`doctor` を実行](navlyn-first-10-minutes_ja.md#2-調べるリポジトリで実行する)してください。対象候補が複数なら、[ワークスペース設定](navlyn-workspace_ja.md)で選びます。Navlyn の全ツールは [MCP リファレンス](navlyn-mcp-server.md)にあります。
