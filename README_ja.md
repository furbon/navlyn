# Navlyn

English: [`README.md`](README.md)

**Navlyn は、C# を主役にした .NET リポジトリを触るコーディングエージェントのための、読み取り専用の意味情報レイヤーです。Roslyn 経由で Visual Basic にも対応します。**

エージェントはもう、コードを書くこと自体はかなり得意です。難しいのは、その編集対象が本当に正しいかを見極めることです。C# のリポジトリや Roslyn が読み込める Visual Basic ソースでは、オーバーロード、部分クラス、対象フレームワーク、DI 登録、ルートハンドラー、公開 API、関連テストなどが絡み、文字列検索だけでは判断しきれない場面がよくあります。

`PaymentService` という文字列は `rg` で見つかります。Navlyn は、その `PaymentService` がこのワークスペースではどの Roslyn シンボルなのかを確認し、後続の調査で使い回せる安定した JSON として返します。

イメージとしては、C# を主役にした編集前の **事前確認** です。

1. ユーザーが意図したシンボルを特定する。
2. 必要なソースと関係だけを、上限付きで読む。
3. 編集はいつものツールで、Navlyn の外で行う。
4. 編集後の diff が意図した対象に当たっているか確認する。

Navlyn には 2 つの入口があります。

- `navlyn`: shell、CI、スクリプト、エージェントループで使う CLI。
- `navlyn-mcp`: MCP 対応のエージェントやエディタから使う、スタンドアロンの stdio MCP サーバー。

どちらもローカルで動く読み取り専用ツールです。ファイル編集、任意の shell 実行、ネットワークアクセス、ソースのアップロード、ホスト型インデックスは持ちません。

説明や例は引き続き C# を中心にしていますが、同じ Roslyn/MSBuild ベースのコマンド群で Visual Basic のプロジェクトとソースファイル（`.vbproj` / `.vb`）も扱えます。

## なぜ必要か

成熟した C# コードベースで起きるエージェントの失敗は、コード生成そのものよりも「文脈を取り違えた」ことが原因になりがちです。

- 同じ名前や似た名前の型、メソッド、オーバーロードを取り違える。
- multi-target のプロジェクトで、対象フレームワークごとに見えるシンボルが違う。
- partial、生成ファイル、リンクファイル、条件付きコンパイルで、実際に有効なコードが変わる。
- 重要なつながりが文字列ではなく、DI 登録、ASP.NET の route、MediatR handler、EF model、関連テストにある。
- リポジトリを広く読みすぎて、モデルが本当に必要だった手がかりを見失う。

Navlyn は、こうした地味だけれど重要な調査を Roslyn/MSBuild 由来の JSON として返します。コードベースの形を LLM に推測させません。

## まずは動かす

個人でさっと使うなら、グローバルツールとして入れます。

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp
```

チーム、CI、エージェント用ワークスペースでバージョンを固定したい場合は、リポジトリローカルのツールマニフェストに入れます。

```powershell
dotnet new tool-manifest
dotnet tool install navlyn --version 0.6.0
dotnet tool install navlyn-mcp --version 0.6.0
dotnet tool restore
```

自分の C# リポジトリや Visual Basic プロジェクトで最初に打つなら、このあたりからです。

```powershell
navlyn doctor --workspace path/to/YourRepo.slnx
navlyn resolve-target --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType
navlyn edit-preflight --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType --goal modify --change-kind behavior

# resolve-target または edit-preflight の candidateId を控え、Navlyn の外で編集したあと:
navlyn post-edit-guard --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --fail-on-risk high
```

MCP クライアントでは、まず狭い `reader` プロファイルから始めます。

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/navlyn.workspace.json", "--tool-profile", "reader"]
}
```

そのままコピーしやすい設定例は [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md) にあります。最初の 10 分で一通り試すなら [`docs/navlyn-first-10-minutes.md`](docs/navlyn-first-10-minutes.md) を見てください。

## 基本の流れ

**1. 対象を固定する。**

`resolve-target` は、曖昧な名前からソース上の対象を 1 つ選べる場合は選び、曖昧な場合は候補と理由を返します。返ってきた `candidateId` を後続コマンドで使えば、調査対象が「次にたまたま見つかった文字列」へずれるのを避けられます。

**2. 必要な証拠だけ集める。**

質問に応じて `symbol-source`、`references`、`about`、`impact`、`context-pack` を使います。編集前によく必要になる情報は `edit-preflight` でまとめて取得できます。対象、上限付きソース、上限付きコンテキスト、関連テスト、信頼度、分かっていないこと、編集後に走らせる確認コマンドが 1 つの JSON に入ります。

**3. diff を確認する。**

編集は普段どおりのツールで行います。その後、`post-edit-guard`、`wrong-symbol-guard`、または `review-diff --profile evidence` で、実際の Git diff が意図した対象と合っているかを確認します。

Navlyn の全コマンドを順番に走らせる必要はありません。1 回の呼び出しで 1 つの意味上の質問に答える使い方が、一番扱いやすく、強いです。

## Before / After

Navlyn がない場合、エージェントは `rg PaymentService` を実行し、最初に見つかったそれらしいファイルを開き、別の対象フレームワークやコンストラクター経路を見落としたまま編集してしまうかもしれません。

Navlyn では、最初に対象を固定します。

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

これは実行時の保証ではありません。編集前の調査を、同じソースレベルの対象へ固定するための根拠です。

## いつ使うか

コメント、文字列、Markdown、設定ファイルを見るだけなら、普通にファイルを読むか `rg` を使う方が速いです。C# または Visual Basic の意味情報、プロジェクト文脈、diff の根拠が答えを変えるときに Navlyn を使ってください。

| やりたいこと | 最初の Navlyn 呼び出し | 止めどき |
| --- | --- | --- |
| 曖昧な名前からシンボルを特定する | `resolve-target --query PaymentService --assume-kind NamedType` | 高信頼の `candidateId` が 1 つ得られた、または候補からユーザー確認が必要だと分かったとき。 |
| 既知の C# または Visual Basic ファイルを見る | `outline --file src/Billing/PaymentService.cs` または MCP `navlyn_file_outline` | outline や返された `candidateId` で質問に答えられるとき。 |
| 選択済みシンボルの関係を見る | `references --candidate-id sym:v1:... --group-by file --limit 50` | 返された関係情報で質問に答えられるとき。 |
| 非自明な編集を計画する | `edit-preflight --candidate-id sym:v1:... --goal modify` | 対象、上限付き証拠、分かっていないこと、編集後の確認コマンドが揃ったとき。 |
| 実際の Git diff をレビューする | `review-diff --profile evidence` | 変更シンボル、診断、影響、関連テスト、警告が見えたとき。 |
| 編集後の diff を確認する | `post-edit-guard --candidate-id sym:v1:... --fail-on-risk high` | guard が通る、またはズレの理由を説明できるとき。 |

## 得られるもの

- **曖昧な意図から正確な対象へ**: `resolve-target` と `find` は、信頼度、代替候補、理由コード、再利用できる `candidateId` を返します。
- **ファイル単位から始める意味情報の読解**: 1 つの C# または Visual Basic ファイルを outline し、選んだソース範囲を確認し、必要なときだけ関係をたどれます。
- **ワークスペース事実**: プロジェクト、対象フレームワーク、パッケージ、診断、テスト関係、生成コードポリシー、リポジトリ構造。
- **ソース上の関係**: 定義、参照、利用種別、呼び出し元、呼び出し先、実装、型階層、関連ファイル、入口、静的な影響。
- **.NET アプリケーションの証拠**: ASP.NET Core のルート/認可、DI 登録と利用側、options/configuration、MediatR handler、EF Core model、パッケージ利用、フレームワーク入口。
- **レビュー用の証拠**: 変更シンボル、診断、静的な影響、公開 API に関する事実、関連テスト、上限付き review-pack シグナル。
- **エージェント向けの確認材料**: wrong-symbol 回避のための `edit-preflight`、`post-edit-guard`、`wrong-symbol-guard`、change intent、handoff、confidence ledger。
- **自動化しやすい契約**: stdout は決定論的 JSON、stderr は診断、位置は 1 始まり、パスは可能な限りリポジトリ相対の `/` 区切り。重要な出力には契約ドキュメント、schema、golden snapshot があります。

## MCP で使う

`navlyn-mcp` は、エージェントに shell command を組み立てさせずに C# を主役にした .NET の意味情報を渡すための入口です。Visual Basic ソースも対象です。最初は狭く始め、必要になったときだけ広げます。

| プロファイル | 使う場面 |
| --- | --- |
| `reader` | セットアップ確認、ファイル outline、シンボル解決、上限付きソース、選択済みシンボルの関係。デフォルトです。 |
| `edit` | 編集前の証拠、関連テスト、影響調査、上限付きコンテキスト、handoff/confidence 情報、編集後の確認。 |
| `review` | 実際の Git diff、PR レビュー証拠、公開 API に関する事実、関連テスト、確認用コマンド。 |
| `full` | `navlyn_batch` を含む、すべての MCP ツールが必要なクライアント向けの互換モード。 |

境界は意図的にシンプルです。stdio のみ、読み取り専用、ローカル専用、任意ファイルサーバーなし、編集ツールなし、shell 実行なし。詳しくは [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md) を参照してください。

## 他のツールとの位置づけ

| ツール | 使う場面 | Navlyn の役割 |
| --- | --- | --- |
| `rg` と通常のファイル読み取り | コメント、文字列、Markdown、設定、素早いテキスト探索 | テキストだけでは曖昧なときの C# または Visual Basic シンボル同一性、プロジェクト文脈、ソース上の関係。 |
| LSP / IDE | 対話的な編集、rename、定義ジャンプ、エディタ診断 | エージェント、CI、スクリプト、MCP クライアント向けの安定した JSON 事実。 |
| Roslyn API / analyzer | 独自の compiler tooling やルール診断を作る | analyzer を書かずに使える読み取り専用ワークフロー。 |
| 汎用 code-search MCP | 言語横断、またはテキストレベルのリポジトリ検索 | C# と Visual Basic の MSBuild/Roslyn 由来の事実: 対象フレームワーク、シンボル、参照、DI、ルート、テスト、レビュー証拠。 |
| 編集機能を持つ MCP server | 1 つのクライアントで調査と編集を行う | 編集面を持たない、クライアント非依存の証拠。 |
| CI review bot | コメント投稿や pass/fail check | 人間やエージェントが発言を決める前に見る、ローカルなレビュー事実。 |
| ホスト型 code search | 複数リポジトリをまたぐ hosted index | ソースを外部サービスに送らない、リポジトリローカルの解析。 |

## このリポジトリでデモを動かす

このリポジトリでは、`dotnet restore navlyn.slnx` の後に実行できます。

```powershell
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- resolve-target --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --limit 5
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- edit-preflight --workspace navlyn.slnx --project "Navlyn.CommandLine(net10.0)" --query CheckCommand --assume-kind NamedType --goal modify --change-kind behavior
dotnet run --framework net10.0 --no-launch-profile --project navlyn -- review-diff --workspace navlyn.slnx --base HEAD --head HEAD --profile compact --symbol-limit 3 --impact-limit 3 --diagnostic-limit 3 --related-test-limit 3
```

1 つ目は、このリポジトリ内の曖昧な名前を C# シンボルへ固定します。2 つ目は、その対象を編集する前の証拠を作ります。3 つ目は、明示した Git 範囲のレビュー用 JSON を確認します。PR の ref に置き換えるか、dirty branch では ref 指定を省略できます。

詳しい walkthrough は [`docs/navlyn-demo-walkthroughs.md`](docs/navlyn-demo-walkthroughs.md) にあります。

## 信頼境界

Navlyn は、上限付きのソースレベル証拠を返します。実行時挙動を証明せず、テストを実行せず、secret scan をせず、SemVer を決めず、レビューコメントを投稿せず、人間の判断を置き換えません。

通常の読解も置き換えません。コメント、文字列、Markdown、生成物、Roslyn ソースではないファイルに関する質問なら、`rg` や通常のファイル読み取りを使ってください。

既知の制限は [`docs/navlyn-limitations.md`](docs/navlyn-limitations.md)、性能と warm cache の挙動は [`docs/navlyn-performance.md`](docs/navlyn-performance.md) にあります。

## ドキュメント

- [`docs/navlyn-first-10-minutes.md`](docs/navlyn-first-10-minutes.md): CLI/MCP の最初の成功体験までの最短導線。
- [`docs/navlyn-client-setup.md`](docs/navlyn-client-setup.md): インストール形態とクライアント設定。
- [`docs/navlyn-agent-recipes.md`](docs/navlyn-agent-recipes.md): タスク別の CLI/MCP レシピ。
- [`docs/navlyn-positioning.md`](docs/navlyn-positioning.md): search、LSP、analyzer、MCP server、review bot との位置づけ。
- [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md): MCP プロファイル、ツール、リソース、鮮度、境界。
- [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md): 公開 CLI 契約と JSON の挙動。
- [`docs/navlyn-performance.md`](docs/navlyn-performance.md): 性能モデルと計測コマンド。
- [`docs/navlyn-development-workflow.md`](docs/navlyn-development-workflow.md): 貢献者向けの検証、スクリプト、開発ワークフロー。

## ライセンス

Navlyn は MIT ライセンスです。詳しくは [`LICENSE`](LICENSE) を参照してください。
