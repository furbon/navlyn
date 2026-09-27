# Navlyn

English: [`README.md`](README.md)

**コーディングエージェントに、コードベースをさまよわせない。**

Navlyn は「`PaymentService` を直して」のような指示を、エージェントが行動するために必要な少数のコード、関係、確認へ変えます。意図した対象を選び、調査中に使い回せる anchor を返し、編集後の実際の diff がその対象に収まっているかを確認します。

```text
「PaymentService を直して」
        |
        v
意図した対象を選ぶ -> 必要なものだけ読む -> 編集する -> diff を確認する
```

汎用のコード検索は、エージェントに候補の山を返します。Navlyn は、次の問いを一つの選択済み target に結び続けます。ユーザーが指した symbol はどれか、何に依存しているか、変更前に何を読むべきか、編集が意図した場所に入ったか。無関係なコードを広く読んだり、何度も検索し直したりせず、編集に関係する事実へエージェントのコンテキストを使えます。

コーディングエージェントからは `navlyn-mcp` を使うのが基本です。`navlyn` CLI は同じエンジンを shell、CI、初回確認で使うための入口です。

## 3 分の最短導線

現在の `0.8.0-preview.1` candidate は、公開前のローカル release rehearsal 用です。NuGet には公開されていません。Windows、PowerShell 7、.NET SDK 10 を用意し、[Windows 向けローカル feed quick start](docs/navlyn-first-10-minutes.md) に従って両ターゲットの package を作成し、`--tool-path` に隔離して install してください。install した絶対パスの `navlyn.exe` で最初の semantic fact を取得します。repository にある tool manifest もこの preview を指定しているため、対応するローカル feed を使って restore してください。

このプレビューでは、ローカルにある依存ライブラリのメソッドを `read --external-source decompiled` で調べられます。返る C# は逆コンパイルで再構成したもので、元のソースではありません。[実パッケージの評価結果](docs/evals/external-member-corpus.md)も参照してください。

### 公開済みの 0.7.0

以下のコマンドは公開済みの `0.7.0` 用です。`0.8.0-preview.1` candidate を試す場合は、上記のローカル feed quick start を使用してください。

コーディングエージェント用の MCP server を install します。

```powershell
dotnet tool install --global navlyn-mcp --version 0.7.0
```

shell や CI で JSON fact を使いたい場合は CLI も入れます。

```powershell
dotnet tool install --global navlyn --version 0.7.0
```

次に、workspace と symbol を一つ確認します。通常の、トップレベルに `.slnx`、`.sln`、`.csproj`、`.vbproj` が一つあるリポジトリでは `auto` を使います。

```powershell
navlyn doctor --workspace auto
navlyn target --workspace auto --query PaymentService --assume-kind NamedType --limit 10
navlyn read --workspace auto --candidate-id sym:v1:... --view declaration --max-lines 80
navlyn prepare-edit --workspace auto --candidate-id sym:v1:... --goal modify --change-kind behavior
```

`target` が返す `candidateId` を後続の呼び出しで使います。編集後は実際の diff を確認します。

```powershell
navlyn review --workspace auto --profile evidence
```

`auto` が workspace を見つけられない場合や、同じ優先度の候補が複数ある場合だけ、意図した `.slnx`、`.sln`、`.csproj`、`.vbproj` を明示するか、`navlyn.workspace.json` でリポジトリ共通の選択を固定してください。

## MCP で使う

トップレベルの workspace 候補が一つだけのリポジトリでは、MCP server はリポジトリルートの working directory から自動で workspace を見つけます。複数の solution/project があり得るリポジトリだけ、明示的に `--workspace` を渡します。MCP client が server をリポジトリルートから起動しない場合は、`--workspace` ではなく `--working-directory <repo-root>` を渡します。

### GitHub Copilot CLI

ローカル preview を使う場合は、[Copilot CLI MCP 設定例](examples/install/copilot-cli-mcp.json)を repository ルートの `.mcp.json` または `.github/mcp.json` にコピーし、command を install 済み `navlyn-mcp.exe` の絶対パスに置き換えます。Copilot CLI の設定形式は `mcpServers` です。prompt session で repository MCP 設定を有効にするには、起動前に PowerShell で次を設定します。

```powershell
$env:GITHUB_COPILOT_PROMPT_MODE_WORKSPACE_MCP = 'true'
copilot
```

Windows 上の Copilot CLI `1.0.88` で、ローカル install した preview server の起動と `navlyn_target` の semantic call を明示的な追加設定で確認しました。この結果は当該 CLI/package の組み合わせに限られ、Codex skill が Copilot で使えることを示しません。現在の project config の opt-in 条件は公式の [Copilot CLI MCP guide](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers) を参照してください。

### GitHub Copilot in VS Code

リポジトリのルートに `.vscode/mcp.json` を作成します。ローカル preview では、`navlyn-mcp` を install 済み実行ファイルの絶対パスに置き換えます。

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "cwd": "${workspaceFolder}"
    }
  }
}
```

VS Code は `.vscode/mcp.json` と `servers` 形式を使います。これは設定方法を記載したもので、この rehearsal では VS Code の動作確認はしていません。詳細は公式の [VS Code MCP guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers) を参照してください。

### Codex

公開済みの global install では、リポジトリのルートで次を実行します。ローカル preview では [quick start](docs/navlyn-first-10-minutes.md#mcp-client-setup) の絶対パスを使用します。

```powershell
codex mcp add navlyn -- navlyn-mcp
```

## Codex routing skill

Navlyn routing skill は `navlyn-mcp` とは別に install します。対象 repository で、Navlyn source checkout にある installer を呼び出し、その repository の `.agents/skills` に install / update します。

```powershell
$navlynSource = (Resolve-Path '<navlyn-source-checkout>').Path
$skillRoot = Join-Path (Get-Location) '.agents/skills'
New-Item -ItemType Directory -Force $skillRoot | Out-Null
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Install -DestinationRoot $skillRoot
```

同じ bytes で再実行しても変更はありません。隣接する `.navlyn-semantic-routing.install.json` marker が所有権を記録します。marker がない・不正、管理対象の bytes が変わった、または無関係なファイルがある場合は停止し、ファイルを保持します。状態を確認して競合を解決してから再実行してください。管理下の skill は次のように削除できます。

```powershell
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Uninstall -DestinationRoot $skillRoot
```

Windows の Codex CLI `0.155.0-alpha.16` で隔離 discovery と6ケースの activation smoke を確認しました。process-scoped full-access session で実施し、source への書き込み試行や diff はありませんでした。検証した read-only Windows sandbox では WindowsApps PowerShell が起動できず、同 sandbox mode での activation は確認していません。Codex skill は Copilot 対応を意味しません。詳細は [release contract](docs/navlyn-release-contract.md#client-support-claims) を参照してください。

### Claude Code

リポジトリのルートに `.mcp.json` を作成します。

```json
{
  "mcpServers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--working-directory", "${CLAUDE_PROJECT_DIR:-.}"]
    }
  }
}
```

Navlyn MCP は、読み取り専用の semantic tool surface を一つだけ公開します。既定の起動では、リポジトリローカルの workspace 候補が一つだけならそれを使い、曖昧な場合は推測せずに失敗します。エージェントは必要な最小の fact から始め、`candidateId` を再利用し、返された JSON が問いに答えた時点で止まれます。

## エージェントが得るもの

| エージェントの問い | Navlyn MCP tool |
| --- | --- |
| ユーザーが指しているシンボルはどれか | `navlyn_target` |
| 選んだシンボルの宣言を見たい | `navlyn_read` |
| 呼び出し元や参照元を知りたい | `navlyn_navigate` |
| 変更前に何を把握すべきか | `navlyn_prepare_edit` |
| 実際の diff が対象から外れていないか | `navlyn_verify_edit` |
| この Git diff は何へ影響したか | `navlyn_review` |

必要なのはリポジトリ全体を渡すことではなく、一つずつ答えられる問いです。Navlyn は範囲を絞った JSON の事実を返します。エージェントは調査中ずっと同じ `candidateId` を使い、必要になったときだけ次の関係やソース断片を取得できます。

## 使う場面 / 使わない場面

| Navlyn を使う | Navlyn を使わない |
| --- | --- |
| C# / Visual Basic の symbol identity、overload、partial declaration、project context、target framework、DI、route、related tests、Git diff evidence。 | コメント、文字列、docs、任意のテキスト検索、生成物、runtime proof、security scan、test execution、編集、refactoring、review comment 投稿。 |

テキストで十分なときは通常のファイル読み取りと `rg` を使います。Roslyn/MSBuild の事実でエージェントが読む場所や編集対象を変えるべきときに Navlyn を使います。

## ワークスペースの選び方

ほとんどのリポジトリでは `navlyn.workspace.json` は必要ありません。MCP server はリポジトリルートで `--workspace` を省略し、CLI では `--workspace auto` を使います。どちらもトップレベルの `navlyn.workspace.json`、`.code-workspace`、`.slnx`、`.sln`、`.csproj`、`.vbproj` 候補を一つだけ選び、最良候補が曖昧なら推測せずに止まります。

solution/project の候補が複数あり、誰が使っても同じ対象を選ばせたい場合だけ `navlyn.workspace.json` を追加します。最小構成はこれです。

```json
{
  "primaryWorkspace": "YourRepo.sln"
}
```

候補探索や root policy を含む完全な設定は [docs/navlyn-workspace_ja.md](docs/navlyn-workspace_ja.md) にあります。

## CLI と CI

MCP setup 後は CLI は必須ではありませんが、install 確認、script 化、CI evidence には CLI が一番簡単です。

## 境界

Navlyn はローカルで動く読み取り専用ツールです。ファイル編集、任意の shell 実行、ネットワークアクセス、ソースのアップロード、実行時の挙動の証明は行いません。compiler と project の事実が必要なときは Navlyn を使い、テキストで十分なときは通常のファイル読み取りと `rg` を使います。

## ドキュメント

- [MCP サーバーリファレンス](docs/navlyn-mcp-server.md): stable tool surface、resource、プロトコルの挙動。
- [ワークスペース設定](docs/navlyn-workspace_ja.md): `navlyn.workspace.json` を置く場面と設定項目。
- [最初の調査](docs/navlyn-first-10-minutes.md): セットアップ後に行う短い意味解析フロー。
- [Demo と case study](docs/navlyn-demo-walkthroughs.md): current repo と fixture で再現できる evidence。
- [CLI コマンドリファレンス](docs/navlyn-cli-commands.md): コマンドと JSON 契約の完全な仕様。
- [エージェントレシピ](docs/navlyn-agent-recipes.md): 用途ごとの CLI / MCP workflow。

クライアント固有の現在の設定形式は、公式の [GitHub Copilot](https://docs.github.com/en/copilot/how-tos/provide-context/use-mcp-in-your-ide/extend-copilot-chat-with-mcp)、[Codex](https://learn.chatgpt.com/docs/extend/mcp)、[Claude Code](https://docs.anthropic.com/en/docs/claude-code/mcp) を参照してください。

## License

Navlyn は MIT License で提供しています。詳しくは [LICENSE](LICENSE) を参照してください。
