# Navlyn

English: [`README.md`](README.md)

Navlyn は、AI エージェントと自動化のために、C#/.NET リポジトリの compiler-backed facts を返すツールです。

決定論的な JSON CLI であり、read-only な stdio MCP server でもあります。エージェントが「これは何のシンボルか」「どこで使われているか」「この PR で重要な変更は何か」「関連しそうなテストはどれか」「編集前に読むべき文脈は何か」を、素のテキストだけに頼らず調べるために使います。

Navlyn は `rg`、エディタ、完全な runtime analyzer の置き換えではありません。コメント、文字列、ドキュメント、非 C# ファイルにはテキスト検索を使ってください。C# のシンボル、source location、参照、呼び出し関係、診断、リポジトリ構造、差分、テスト、public API 変更、framework entrypoint、dependency injection registration、bounded agent context など、Roslyn による意味解析が必要な場面で Navlyn を使います。

## Navlyn の価値

Coding agent は意図を扱うのは得意ですが、正確な navigation command を長くつなげるのは不安定です。人間なら「`WidgetService` を変えたときの影響を見て」と言えますが、エージェントは正しい型を探し、overload や project を識別し、references を集め、callers を見て、entrypoint をたどり、関連テストを探し、しかも結果を使える大きさに抑える必要があります。

Navlyn はその調査ステップを、安定した local machine-readable facts に変換します。

- **曖昧な意図から決定論的な候補へ**: approximate な symbol query から、ranked candidates、confidence、reason codes、alternatives、opaque な `candidateId`、next actions を返します。
- **タスク単位の workflow**: `about`、`related`、`impact`、`entrypoints`、`review-diff`、`context-pack` は、editor-style primitive だけでなく調査タスクそのものに答えます。
- **Evidence-first な review data**: diff workflow は changed symbols、diagnostics、static impact、public API facts、related test candidates、review-pack signals を返します。レビュー文章は生成しません。
- **Token-aware な context retrieval**: `context-pack` は `review`、`modify`、`understand` の目的に合わせて、bounded な reading material を rank します。
- **.NET に特化した intelligence**: repository/project graph、framework-aware entrypoints、test discovery、public API diff、Microsoft.Extensions.DependencyInjection facts を first-class に扱います。
- **Agent-safe integration**: コマンド結果は stdout に deterministic JSON、診断は stderr、パスは可能な限り repository-relative、MCP server は read-only です。

## 位置づけ

Navlyn には 3 つの層があります。多くのエージェントは上の層から始め、必要なときだけ下の層へ降りるのが自然です。

| Layer | 用途 | 例 |
| --- | --- | --- |
| MCP tools | MCP 対応 agent client 向けの小さな high-level tool surface | `navlyn_find_symbol`, `navlyn_about_symbol`, `navlyn_review_diff`, `navlyn_context_pack` |
| Investigation workflows | symbol、diff、task から始める人間・script 向け CLI workflow | `find`, `about`, `related`, `impact`, `entrypoints`, `review-diff`, `context-pack` |
| Roslyn primitives | exact source-position navigation と low-level semantic facts | `definition`, `references`, `implementations`, `type-hierarchy`, `callers`, `calls`, `symbol-info` |

## Quick Start

Navlyn は .NET 10 を target にし、MSBuild/Roslyn 経由で `.slnx`、`.sln`、`.csproj` workspace を読み込みます。

この repository から実行する場合:

```powershell
dotnet restore navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- repo-graph --workspace navlyn.slnx --profile compact
```

設定済みの NuGet sources から package を取得できる場合は、標準的な .NET tool command で install します。

```powershell
dotnet tool install --global navlyn
dotnet tool install --global navlyn-mcp

navlyn check --workspace path/to/YourRepo.slnx
navlyn repo-graph --workspace path/to/YourRepo.slnx --profile compact
```

## Agent Investigation

symbol の意図はわかるが、正確な file、project、overload、column がわからないときは fuzzy discovery から始めます。

```powershell
navlyn find --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
navlyn about --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
navlyn related --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --limit 30
navlyn impact --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType --depth 2
```

長い workflow では、`find` が返す `candidateId` を使うと、後続の問い合わせが同じ declaration を指し続けます。

```powershell
navlyn about --workspace navlyn.slnx --candidate-id sym:v1:...
navlyn context-pack --workspace navlyn.slnx --candidate-id sym:v1:... --goal modify --profile compact
```

Fuzzy command は、複数の plausible symbols を勝手に混ぜません。曖昧な query では candidates と alternatives を返し、agent が自己補正できるようにします。

## Review And Context

Navlyn の review commands は facts provider です。reviewer の代替ではなく、review comments を生成せず、完全な runtime reachability も主張しません。

```powershell
navlyn review-diff --workspace navlyn.slnx --profile evidence
navlyn context-pack --workspace navlyn.slnx --diff --goal review --profile compact
navlyn tests-for-diff --workspace navlyn.slnx --profile compact
navlyn public-api-diff --workspace navlyn.slnx --base main --profile evidence
navlyn review-pack --workspace navlyn.slnx --pack async --pack security --profile evidence
```

最初の scan には `compact`、review / CI facts には `evidence`、もっとも rich な contract shape が必要なときは `full` を使います。

## MCP Server

`navlyn-mcp` tool は、CLI contract を背後に持つ focused read-only MCP surface を公開します。MCP 対応 agent client が、個別の CLI command を組み立てずに semantic C# question を問い合わせたいときに使います。

install 済み server の典型的な設定:

```json
{
  "command": "navlyn-mcp",
  "args": ["--workspace", "path/to/YourRepo.slnx"]
}
```

MCP の setup、tool selection guidance、result envelope、boundaries は [`docs/navlyn-mcp-server.md`](docs/navlyn-mcp-server.md) を参照してください。

## Output Contract

- stdout は command result JSON 専用です。
- stderr は diagnostics、errors、warnings、progress 用です。
- automation-facing output は deterministic です。
- paths は可能な限り repository-relative で、JSON output では `/` separators を使います。
- user-facing line / column values は 1-based です。

完全な CLI contract、options、JSON shapes、error behavior、command boundaries は [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md) にあります。実践的な agent recipe は [`docs/navlyn-agent-recipes.md`](docs/navlyn-agent-recipes.md) にあります。

## License

Navlyn は MIT License で公開されています。詳しくは [`LICENSE`](LICENSE) を参照してください。
