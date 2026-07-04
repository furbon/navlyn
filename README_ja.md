# Navlyn

English: [`README.md`](README.md)

**Navlyn は、C#/.NET のコーディングエージェントが「違うシンボル」を編集してしまう事故を減らすためのツールです。**

エージェントは `PaymentService` という文字列を検索できます。しかし、その結果だけでは、どのオーバーロード、対象フレームワーク、部分宣言、DI 登録、ルートハンドラー、公開 API、関連テストが本当に重要なのかを安全に判断できません。Navlyn は、編集前にローカルの Roslyn/MSBuild 由来の証拠を返します。

Navlyn には次の 2 つがあります。

- stdout に決定論的 JSON を返す `navlyn` CLI。
- MCP 対応のコーディングエージェントやエディタで使える、スタンドアロンの `navlyn-mcp` stdio MCP サーバー。

どちらも読み取り専用・ローカル・事実提供専用です。ファイル編集、任意 shell 実行、ネットワークアクセス、ホスト型 index は持ちません。

C# エージェントにコードを触らせる前の **preflight** として使ってください。まず正確なシンボルを固定し、必要なソースと関係だけを確認し、その後で影響範囲、テスト、読むべき文脈へ進みます。

## インストールして試す

グローバルツール:

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp
```

チームやエージェント用ワークスペースでは、リポジトリローカルの tool manifest に固定できます。

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.5.0
dotnet tool install navlyn-mcp --version 0.5.0
dotnet tool restore
```

自分のリポジトリで最初に試すコマンド:

```powershell
navlyn check --workspace path/to/YourRepo.slnx
navlyn resolve-target --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType
navlyn references --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --group-by file --limit 50
navlyn context-pack --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --goal modify --profile compact
```

MCP クライアントでは、まず狭い `reader` profile から始めます。

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/navlyn.workspace.json", "--tool-profile", "reader"]
}
```

コピーできる設定例は [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md) にあります。

## 解きたい問題

人間のレビュー担当者なら「`PaymentService` を変える前に、どこで生成されるか、どの endpoint から来るか、どのテストが関係するかを見て」と言えます。

エージェントは、それを壊れやすい手順へ分解しなければなりません。

1. 似た名前、オーバーロード、部分宣言、生成ファイル、リンクファイル、対象フレームワークを区別する。
2. たまたま最初に見つかった文字列ではなく、同じ C# シンボルへ後続調査を固定する。
3. 読み取り、書き込み、呼び出し、生成、呼び出し元、呼び出し先、実装、フレームワーク入口、テストを分ける。
4. リポジトリ全体をモデルに流すのではなく、読む価値がある小さなキューを作る。
5. 自動化が信頼できる JSON、パス、警告、終了コードを保つ。

Navlyn はこの調査を、上限付きの Roslyn-backed facts として提供します。コードベースの形を LLM に推測させません。

## Before / After

Navlyn なしでは、エージェントが `rg PaymentService` を実行し、最初に見つかったそれらしいファイルを開き、別の target framework や constructor path を見落としたまま編集してしまうかもしれません。

Navlyn では、最初に target envelope を取得します。

```json
{
  "confidence": "high",
  "candidateCount": 1,
  "selectedTarget": {
    "name": "PaymentService",
    "kind": "NamedType",
    "path": "src/Billing/PaymentService.cs",
    "line": 14,
    "column": 21
  },
  "candidateId": "sym:v1:...",
  "selector": {
    "project": "Billing.Api(net10.0)",
    "targetFramework": "net10.0"
  },
  "recommendedNextActions": [
    { "command": "definition", "candidateId": "sym:v1:..." },
    { "command": "references", "candidateId": "sym:v1:..." },
    { "command": "about", "candidateId": "sym:v1:..." }
  ]
}
```

これは実行時の証明ではありません。編集前に同じソースレベルの対象へ調査を固定するための証拠です。

## いつ使うか

テキストだけで答えられるときは、通常のファイル読み取りや `rg` を先に使ってください。C# の意味情報が答えを変えるときに Navlyn を使います。

| タスク | 最初の Navlyn 呼び出し | 止めどき |
| --- | --- | --- |
| 曖昧な名前からシンボルを特定する | `resolve-target --query PaymentService --assume-kind NamedType` | 高信頼の `candidateId` が 1 つ得られた、または候補からユーザー確認が必要だと分かったとき。 |
| 既知の C# ファイルを見る | `outline --file src/Billing/PaymentService.cs` または MCP `navlyn_file_outline` | outline や返された `candidateId` で質問に答えられるとき。 |
| 選択済みシンボルの関係を見る | `references --candidate-id sym:v1:... --group-by file --limit 50` | 返された関係情報で質問に答えられるとき。 |
| 非自明な編集を計画する | `impact --candidate-id sym:v1:... --profile light` | 危ない呼び出し元やファイルが分かったとき。読むキューが必要な場合だけ `context-pack` に進みます。 |
| 実際の Git diff をレビューする | `review-diff --profile evidence` | 変更シンボル、診断、影響、上限付き警告が得られたとき。 |

Navlyn の全コマンドをチェックリストとして順番に実行しないでください。1 回の呼び出しで 1 つの意味上の質問に答える使い方が一番強いです。

## 得られるもの

- **曖昧な意図から正確な anchor へ**: `resolve-target` と `find` は信頼度、代替候補、理由コード、安定した `candidateId` を返します。
- **file-first な調査**: 1 つの C# ファイルを outline し、選択した source slice を見て、必要なときだけ edge を聞きます。
- **ワークスペース事実**: プロジェクト、対象フレームワーク、パッケージ、診断、テスト関係、生成コードポリシー、リポジトリ構造。
- **ソース関係**: 定義、参照、呼び出し元、呼び出し先、実装、型階層、関連ファイル、entrypoint、静的 impact。
- **.NET アプリケーション証拠**: ASP.NET Core route/auth、DI 登録と consumer、options/configuration、MediatR handler、EF Core model、package usage、framework entrypoint。
- **レビュー証拠**: 変更シンボル、診断、静的 impact、public API facts、関連テスト、上限付き review-pack signal。
- **自動化しやすい契約**: stdout は決定論的 JSON、stderr は診断、位置は 1 始まり、パスは可能な限りリポジトリ相対 `/` 区切り。重要な出力には schema/golden snapshot があります。

## MCP で使う

`navlyn-mcp` は、エージェントに shell command を組み立てさせずに C# semantic tools を渡すための入口です。デフォルトの `reader` profile は、最初の調査に必要な狭い surface だけを公開します。

- `navlyn_resolve_target`
- `navlyn_file_outline`
- `navlyn_symbol_source`
- `navlyn_symbol_edges`
- `navlyn_about_symbol`
- `navlyn_workspace_summary`
- `navlyn_workspace_status`
- `navlyn_workspace_refresh`

実際の PR/diff review には `--tool-profile review`、編集計画には `edit`、`navlyn_batch` を含む完全な互換 surface が必要な場合だけ `full` を使います。

MCP サーバーの境界は単純で監査しやすい形にしています。読み取り専用、ローカル専用、任意ファイルサーバーなし、編集ツールなし、shell 実行なし。詳しくは [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md) を参照してください。

## 他のツールとの位置づけ

| ツール | 使う場面 | Navlyn の役割 |
| --- | --- | --- |
| `rg` | コメント、文字列、Markdown、設定テキスト、素早い探索 | C# シンボルの同一性、プロジェクト文脈、ソースレベルの関係。 |
| LSP / IDE | 対話的な編集、rename、定義ジャンプ、エディタ診断 | エージェント、CI、スクリプト向けの安定した JSON 事実。 |
| Roslyn API / analyzer | 独自の compiler tooling を作る | すぐ使える読み取り専用 CLI/MCP workflow。 |
| 編集寄り MCP server | 1 つのクライアントで inspect と modify を行う | 編集 surface を持たない、client-neutral な evidence。 |
| CI review bot | コメント公開や pass/fail check | 人間やエージェントが発言を決める前に見る review evidence pack。 |
| ホスト型 code search | 複数 repo の hosted index | ソースを hosted service に送らない、repo-local な MSBuild/Roslyn facts。 |

## この repo でデモを動かす

このリポジトリで `dotnet restore navlyn.slnx` の後に実行できます。

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- symbol-source --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 6 --column 23 --view declaration
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --base HEAD --head HEAD --profile compact --symbol-limit 3 --impact-limit 3 --diagnostic-limit 3 --related-test-limit 3
```

1 つ目は曖昧な名前を C# シンボルへ固定します。2 つ目はそのシンボルの上限付きソースを開きます。3 つ目は明示した Git 範囲の review envelope を確認します。PR の ref に置き換えるか、dirty branch では ref 指定を省略できます。

より詳しい walkthrough は [`docs/navlyn-demo-walkthroughs.md`](docs/navlyn-demo-walkthroughs.md) にあります。

## 信頼境界

Navlyn は上限付きのソースレベル証拠を返します。実行時挙動を証明せず、テストを実行せず、secret scan をせず、SemVer を決めず、レビューコメントを公開せず、人間の判断を置き換えません。

通常の読解も置き換えません。コメント、文字列、Markdown、生成物、C# 以外のファイルに関する質問なら、`rg` や通常のファイル読み取りを使ってください。

既知の制限は [`docs/navlyn-limitations.md`](docs/navlyn-limitations.md)、性能と warm cache の挙動は [`docs/navlyn-performance.md`](docs/navlyn-performance.md) にあります。

## ドキュメント

- [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md): インストールとクライアント設定。
- [`docs/navlyn-agent-recipes.md`](docs/navlyn-agent-recipes.md): タスク別の CLI/MCP recipe。
- [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md): MCP profile、tool、resource、freshness、境界。
- [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md): CLI contract と JSON behavior。
- [`docs/navlyn-distribution.md`](docs/navlyn-distribution.md): release package と publish runbook。
- [`docs/navlyn-performance.md`](docs/navlyn-performance.md): performance model と measurement command。

## ライセンス

Navlyn は MIT ライセンスです。詳しくは [`LICENSE`](LICENSE) を参照してください。
