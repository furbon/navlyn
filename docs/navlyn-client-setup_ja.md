# AI ツールから Navlyn を使う

[English](navlyn-client-setup.md)

MCP は、AI ツールが外部のコマンドを呼び出すための仕組みです。Navlyn では `navlyn-mcp` がそのコマンドです。設定後、AI ツールから `navlyn_target` などを呼べます。

以下は Windows の手順です。[PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) と、調べたいリポジトリに合う [.NET 10 SDK](https://dotnet.microsoft.com/download) を用意します。

VS Code では、設定バンドルを使って計画の確認、インストール、接続確認、取り消しを行えます。ほかのクライアントでは、後述の手動インストールを使います。

0.9.1の既定値はtarget・read・file outline・navigateの4ツールです。局所的な文字列や設定の確認には通常の読取りや`rg`を使います。従来の25ツールと完全な応答が必要な場合は、サーバー引数に`--surface full`を追加してください。[移行と応答設定](navlyn-mcp-server.md#v091-defaults-and-migration)を参照してください。

公開済みの版がNuGetで確認できるのにインストール時に見つからない場合、同じコマンドに`--no-http-cache`を追加して再試行します。全体のキャッシュ削除は不要です。

## VS Code の設定バンドル

リリースの添付ファイル `navlyn-setup-0.9.1.zip` を専用ディレクトリへ展開します。`integrity.json` にはソースのコミットと各ファイルのハッシュがあります。展開先で、次の二つのパスを実際のものに置き換えて実行します。

```powershell
./setup-navlyn.ps1 -Workspace 'C:/src/my project' -WorkspaceFile 'C:/src/my project/MyApp.slnx' -Version 0.9.1
./setup-navlyn.ps1 -Workspace 'C:/src/my project' -WorkspaceFile 'C:/src/my project/MyApp.slnx' -Version 0.9.1 -Apply
```

最初のコマンドは計画を表示します。ファイルの書き込み、パッケージのダウンロード、クライアントの起動は行いません。パス、バージョン、取得元、変更内容を確認してから適用してください。自動選択で対象が決まらない場合は、`-WorkspaceFile` に使いたい `.slnx`、`.sln`、`.csproj`、`.vbproj` を明示します。

適用すると、ユーザーのアプリケーションデータ配下にワークスペース専用の `navlyn-mcp` をインストールし、実行ファイルと対象の接続確認を行い、`.vscode/mcp.json` の `navlyn` 項目を書き込みます。無関係な JSONC の項目とコメントは保持し、VS Code の信頼設定は変更しません。そのワークスペースを開き、後述のサーバー起動と実際のツール呼び出しまで確認してください。ヘルパーの通信試験だけでは、クライアントからの接続確認は完了しません。

ローカルの取得元を使う場合は `-Feed 'C:/packages/navlyn'` を追加します。インストールと更新には正確な `-Version` が必要です。同じワークスペースと対象で `-Action Update -Version <version> -Apply`、`-Action Undo -Apply`、`-Action Remove -Apply` を使えます。Undo は直前の管理対象の操作を戻し、Remove はヘルパーが登録・所有した設定とインストールを取り除きます。変更済みのファイルや所有を確認できない項目は、競合として保持します。表示された競合を解消してから再実行してください。

`-Target Global` を指定すると、グローバルの `navlyn-mcp` が変更対象になります。復元用の検証済みパッケージを保持し、ほかのツールは保持します。別の管理対象ワークスペースがそのパッケージを参照中なら、復元を拒否します。導入済みの方が新しい場合は保持し、戻すには `-AllowDowngrade` の明示が必要です。途中で操作が止まった場合の回復手順は、バンドルの README を参照してください。

導入と更新では、パッケージ操作を始める前に SDK と VS Code CLI の有無を確認します。前提条件や接続確認で失敗したら、表示された SDK・クライアントの導入先と対象・実行ファイルを確認するか、以下の手動手順を使います。Copilot CLI、Codex、Claude Code の設定は個別に必要です。このバンドルが設定するのは VS Code です。

## MCP の手動インストール

`dotnet tool list --global` で `navlyn-mcp` が導入済みか確認してください。未導入なら以下を実行します。同じ版があればインストールを省略し、古い版なら `dotnet tool update --global navlyn-mcp --version 0.9.1` を実行します。

```powershell
dotnet tool install --global navlyn-mcp --version 0.9.1
$toolHome = if ($env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME } else { $HOME }
$mcpExe = Join-Path $toolHome '.dotnet/tools/navlyn-mcp.exe'
Test-Path $mcpExe
$mcpExe --version
```

導入済みの場合も、上の `$mcpExe` 以降を実行します。表示された版が 0.9.1 であることを確認し、以下の `C:\path\to\navlyn-mcp.exe` を `$mcpExe` のパスで置き換えます。調べたいリポジトリを開いてから設定してください。

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
