# Navlyn

English: [`README.md`](README.md)

**Navlyn は、C#/.NET リポジトリをコーディングエージェントに正確に調査させるための意味解析レイヤーです。**

コーディングエージェントはテキスト検索を使えます。しかし `rg` の結果だけから、オーバーロード、対象フレームワーク、依存性注入の登録、ルートハンドラー、公開 API の変更、関連テストを安全に判断することはできません。Navlyn は、ローカルの Roslyn/MSBuild から得た事実を、決定論的な JSON CLI と読み取り専用の stdio MCP サーバーで返します。

C# の大きなコードベースにエージェントを入れてよいか判断するとき、Navlyn が減らしたいのは「違うシンボルを見て編集した」「読むべき文脈を落とした」という失敗です。

「この文字列はどこにあるか」ではなく、「これはどの C# シンボルか」「どこから到達するか」「変更すると何に影響するか」「編集前にどの文脈を読むべきか」を知りたいときに使います。

## 1 分デモ

このリポジトリで `dotnet restore navlyn.slnx` の後に実行できます。

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- symbol-source --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --file Navlyn.CommandLine/Cli/Commands/CheckCommand.cs --line 6 --column 23 --view declaration
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --base HEAD --head HEAD --profile compact --symbol-limit 3 --impact-limit 3 --diagnostic-limit 3 --related-test-limit 3
```

1 つ目は曖昧な名前を C# シンボルへ固定します。2 つ目はそのシンボルの上限付きソースを開きます。3 つ目は明示した Git 範囲の review envelope を確認します。PR の ref に置き換えるか、dirty branch では ref 指定を省略できます。

## Before / After

Navlyn なしでは、エージェントは `rg CheckCommand` の最初の結果を開き、別プロジェクトの文脈や生成箇所、関連テストを見落としたまま編集してしまうかもしれません。

Navlyn では、同じ調査を Roslyn 由来の証拠から始めます。

```json
{
  "confidence": "high",
  "candidateCount": 1,
  "selectedTarget": {
    "name": "CheckCommand",
    "kind": "NamedType",
    "path": "Navlyn.CommandLine/Cli/Commands/CheckCommand.cs",
    "line": 6,
    "column": 23
  },
  "candidateId": "sym:v1:...",
  "selector": {
    "project": "Navlyn.CommandLine(net10.0)",
    "targetFramework": "net10.0"
  },
  "recommendedNextActions": [
    { "command": "definition", "candidateId": "sym:v1:..." },
    { "command": "references", "candidateId": "sym:v1:..." },
    { "command": "about", "candidateId": "sym:v1:..." }
  ]
}
```

これは実行時の振る舞いを証明するものではありません。編集前に、対象を安定したソースレベルの証拠へ固定するための材料です。

## 何が得られるか

- **必要なときのワークスペース事実**: プロジェクト、対象フレームワーク、パッケージ参照、テスト関係、診断、リポジトリ構造を返します。
- **曖昧な意図から正確な対象へ**: `find` はおおよそのシンボル名から、順位付き候補、信頼度、理由コード、代替候補、`candidateId`、次の操作を返します。
- **発見後の正確なナビゲーション**: `candidateId` は `symbol-source`、`definition`、`references`、`callers`、`calls`、`implementations`、`type-hierarchy`、`symbol-info`、MCP の `navlyn_symbol_source`、`navlyn_symbol_edges`、`navlyn_exact_navigation` に渡せます。
- **エージェントが扱える文脈**: `context-pack` は、`review`、`modify`、`understand` の目的に合わせて、読むべき材料を上限付きで順位付けします。ソース全体を無制限に出力するものではありません。
- **レビューのための証拠**: 差分コマンドは、変更シンボル、診断、静的な影響範囲、公開 API の変更、関連テスト、レビューパックのシグナルを返します。レビューコメントの文章は生成しません。
- **.NET アプリケーション向けの事実**: ASP.NET Core のルートと認可、Microsoft.Extensions.DependencyInjection の登録と影響、オプションと構成、MediatR ハンドラー、EF Core モデル、パッケージ利用、フレームワークの入口、関連テスト候補を扱います。
- **自動化しやすい出力**: コマンド結果は stdout に決定論的 JSON として出し、診断は stderr に出します。パスは可能な限りリポジトリ相対にし、MCP サーバーは事実だけを返す読み取り専用の境界を保ちます。

Navlyn は `rg`、エディタ、テスト、実行時トレーサー、セキュリティスキャナーの置き換えではありません。コメント、文字列、ドキュメント、C# 以外のファイルにはテキスト検索を使ってください。C# の意味上の同一性が重要な場面で Navlyn を使います。

## なぜ必要か

人間なら「`PaymentService` を変える前に影響範囲とテストを見て」と言えます。

エージェントは、それを壊れやすい長い手順に分解しなければなりません。

1. 似た名前、複数プロジェクト、対象フレームワーク、部分宣言、オーバーロードの中から正しい型やメンバーを探す。
2. 定義、参照、呼び出し元、呼び出し先、実装、フレームワークの入口を正確に集める。
3. 読み取り、書き込み、呼び出し、生成、継承、テスト、製品コードでの利用を分ける。
4. 編集前に読む価値がある少数のファイルへ絞る。
5. 自動化が信頼できるように stdout と stderr の規律を守る。

Navlyn はこの一連の調査を、Roslyn の事実に基づくエージェント向けのフローとしてまとめます。重要なのは、コードベースの形を LLM に推測させないことです。Navlyn は、エージェントと人間のレビュー担当者が確認できる証拠を返します。

## 判断ガイド

テキストだけで答えられるときは、通常のファイル読み取りと `rg` を先に使います。C# の意味上の同一性、オーバーロード、プロジェクト文脈、ソースレベルの関係、上限付きのレビュー証拠が必要になったときに Navlyn を使います。

よく使う最小の入口は次の 3 つです。

| タスク | 最小の最初の呼び出し | 止めどき |
| --- | --- | --- |
| 名前から C# シンボルを特定する | `navlyn resolve-target --workspace navlyn.slnx --query PaymentService --assume-kind NamedType` | 高信頼の `candidateId` が 1 つ得られた、または候補一覧からユーザー確認が必要だと分かったとき。 |
| 既知のシンボルの関係を見る | `navlyn references --workspace navlyn.slnx --candidate-id sym:v1:... --limit 50 --group-by file --group-by usage-kind` | 返された参照で質問に答えられるとき。編集リスクを見るときだけ `impact` に進みます。 |
| 実際の Git diff をレビューする | `navlyn review-diff --workspace navlyn.slnx --profile evidence` | 変更シンボルと影響の事実が得られたとき。読む材料がさらに必要な場合だけ `context-pack --diff` を使います。 |

コメント、文字列、Markdown、生成物、C# 以外のファイル、通常のファイル読み取りだけで答えられる質問には Navlyn を使いません。review/test/context 系のコマンドを既定のチェックリストとして実行しないでください。必要になった事実へ一段ずつ進みます。

## よくある使い方

| 知りたいこと | 最初に使うもの | 次に使うもの |
| --- | --- | --- |
| このワークスペースの構成は何か | プロジェクト、パッケージ、テスト関係が必要なときに `repo-graph --profile compact` | `diagnostics`, `overview` |
| ユーザーが指したシンボルはどれか | `resolve-target --query PaymentService` | 返された `candidateId` を再利用、候補一覧が必要なら `find` |
| 編集前に何を読むべきか | 選択済みシンボルの `references` または `impact` | 上限付きの読解キューが必要なときだけ `context-pack --goal modify` |
| この PR は何に影響するか | `review-diff --profile evidence` | 必要に応じて `tests-for-diff`, `public-api-diff`, `review-pack` |
| 実行境界からどう到達するか | `entrypoints --framework-aware` | `route-map`, `where-handled`, `di-impact` |
| MCP クライアントからどう聞くか | `navlyn_resolve_target` | `navlyn_exact_navigation`, `navlyn_context_pack` |

MCP や batch で複数の事実を続けて集める場合、同じワークスペースから複数の対応済み事実が必要だと分かってから `navlyn_batch` を使います。これは最初に実行するチェックリストではなく、最適化です。

## クイックスタート

Navlyn の tool package は .NET 8 と .NET 10 を対象にし、MSBuild/Roslyn 経由で `.code-workspace`、`.slnx`、`.sln`、`.csproj` ワークスペースを読み込みます。リポジトリの最上位に明確な候補が 1 つだけある場合に限り、`--workspace auto` も使えます。

このリポジトリから実行する場合:

```powershell
dotnet restore navlyn.slnx
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx --profile compact
```

Navlyn のツールパッケージを含む NuGet ソースまたは社内フィードを使う場合:

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp

navlyn check --workspace path/to/YourRepo.slnx
navlyn repo-graph --workspace path/to/YourRepo.slnx --profile compact
```

チームでリポジトリ単位に固定する場合は、.NET tool manifest をコミットします。

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.5.0
dotnet tool install navlyn-mcp --version 0.5.0
dotnet tool restore

dotnet tool run navlyn -- check --workspace path/to/YourRepo.slnx
```

CLI と MCP の設定例は [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md)、[`examples/install/dotnet-tools.json`](examples/install/dotnet-tools.json)、[`examples/install/vscode-mcp.json`](examples/install/vscode-mcp.json) にあります。

パッケージ化とリリースの流れは [`docs/navlyn-distribution.md`](docs/navlyn-distribution.md) にあります。再現可能なデモは [`docs/navlyn-demo-walkthroughs.md`](docs/navlyn-demo-walkthroughs.md) にあります。ローカル benchmark と eval の案内は [`docs/navlyn-performance.md`](docs/navlyn-performance.md) と [`docs/evals/tool-selection.md`](docs/evals/tool-selection.md) にあります。既知の制限は [`docs/navlyn-limitations.md`](docs/navlyn-limitations.md) にあります。

## CLI の例

まず target envelope で対象を固定し、`candidateId` で正確な事実へ進みます。

```powershell
navlyn resolve-target --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
navlyn about --workspace navlyn.slnx --candidate-id sym:v1:...
navlyn references --workspace navlyn.slnx --candidate-id sym:v1:... --usage-kind invoke --usage-kind construct --group-by file --group-by usage-kind --limit 50
navlyn impact --workspace navlyn.slnx --candidate-id sym:v1:... --depth 2
navlyn context-pack --workspace navlyn.slnx --candidate-id sym:v1:... --goal modify --change-kind signature --profile compact
```

曖昧な問い合わせでも、Navlyn はそれらしい複数のシンボルを勝手に混ぜません。候補、代替候補、信頼度、次の操作を返すので、呼び出し側が軌道修正できます。

## レビューの例

Navlyn のレビュー系コマンドは証拠を返すためのものです。レビュー担当者の代わりにはならず、最終的なレビュー文章も生成せず、完全な実行時到達性も主張しません。

```powershell
navlyn review-diff --workspace navlyn.slnx --profile evidence
navlyn tests-for-diff --workspace navlyn.slnx --profile compact
navlyn public-api-diff --workspace navlyn.slnx --base main --profile evidence
navlyn review-pack --workspace navlyn.slnx --pack async --pack security --profile evidence
navlyn context-pack --workspace navlyn.slnx --diff --goal review --profile compact
```

最初の把握には `compact`、レビューや CI 向けの証拠には `evidence`、下流ツールが最も詳しい JSON 形状を必要とする場合は `full` を使います。

## MCP サーバー

`navlyn-mcp` は、同じ Navlyn engine と CLI JSON 契約を背後に持つ、読み取り専用の MCP インターフェイスです。MCP 対応クライアントが、シェルコマンドを組み立てずに C# の意味解析上の質問をしたいときに使います。MCP 用途では `navlyn-mcp` だけをインストールすればよく、別途 `navlyn` CLI を入れる必要はありません。

インストール済みサーバーの典型的な設定:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.slnx"]
}
```

最上位にワークスペース候補が 1 つだけあるリポジトリでは、自動検出も使えます。

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "auto"]
}
```

MCP サーバーは `navlyn_workspace_summary`、`navlyn_resolve_target`、`navlyn_find_symbol`、`navlyn_file_outline`、`navlyn_symbol_source`、`navlyn_symbol_edges`、`navlyn_exact_navigation`、`navlyn_review_diff`、`navlyn_context_pack`、`navlyn_batch` などの道具に加え、上限付きのリソースとプロンプトを公開します。tool description は必要時だけ使う前提で書かれています。file/source/edge tools は既知の 1 ファイルまたは 1 シンボルに、workspace summary はプロジェクト文脈が必要なとき、review diff は実際の Git diff があるとき、context pack は段階的な追加調査、batch は複数の事実が必要だと決まったときに使います。詳しくは [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md) を参照してください。

## Navlyn の位置づけ

| ツール | 使う場面 | Navlyn の役割 |
| --- | --- | --- |
| `rg` | コメント、文字列、ドキュメント、C# 以外のファイル、素早いテキスト調査 | ローカルな C# シンボルの同一性、`candidateId`、参照、呼び出し関係、ソースレベルの影響範囲 |
| LSP / IDE | エディタ内の編集、rename、定義ジャンプ、診断表示 | エージェント、CI、スクリプト向けの決定論的 JSON 事実 |
| Roslyn API / refactoring tool | analyzer、refactoring、IDE 機能の構築 | 読み取り専用で上限付き出力を持つ、すぐ使える CLI/MCP ワークフロー |
| Roslyn MCP / editor agent | 1 つのクライアント内での対話的な意味解析支援 | クライアントに依存しない envelope、安定したコマンド事実、編集機能を持たない境界 |
| コード検索 assistant | 複数リポジトリ検索、ホスト型 index、広い発見 | ソースをホスト型 index に送らない、リポジトリローカルの MSBuild/Roslyn 事実 |
| CI review bot | レビューコメントや pass/fail check の公開 | 人間やエージェントが発言を決める前に確認できるレビュー証拠パック |

Navlyn はエディタ、リファクタリングエンジン、テストランナー、実行時トレーサー、セキュリティスキャナー、パッケージ互換性の判定器、任意ファイルサーバーではありません。境界は、読み取り専用でローカルなソースレベルの証拠です。

## 出力契約

- stdout はコマンド結果の JSON 専用です。
- stderr は診断、エラー、警告、進行状況用です。
- 自動化向けの出力は決定論的です。
- パスは可能な限りリポジトリ相対で、JSON 出力では `/` 区切りを使います。
- 利用者向けの行番号と列番号は 1 始まりです。
- 静的な影響、レビュー、フレームワーク、依存性注入、EF、構成、パッケージに関する結果は、上限付きのソースレベルの証拠であり、実行時の完全な証明ではありません。

すべてのコマンド、オプション、JSON 形状、エラー動作、境界は [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md) にあります。実践的なエージェント向けの流れは [`docs/navlyn-agent-recipes.md`](docs/navlyn-agent-recipes.md) にあります。

## ライセンス

Navlyn は MIT ライセンスで公開されています。詳しくは [`LICENSE`](LICENSE) を参照してください。
