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

CLI と MCP server をインストールします。

```powershell
dotnet tool install --global navlyn --version 0.7.0
dotnet tool install --global navlyn-mcp --version 0.7.0
```

次に、workspace と symbol を一つ確認します。`YourRepo.slnx` はリポジトリにある `.slnx`、`.sln`、`.csproj`、`.vbproj` に置き換えてください。

```powershell
navlyn doctor --workspace path/to/YourRepo.slnx
navlyn resolve-target --workspace path/to/YourRepo.slnx --query PaymentService --assume-kind NamedType --limit 10
navlyn symbol-source --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --view declaration --max-lines 80
navlyn edit-preflight --workspace path/to/YourRepo.slnx --candidate-id sym:v1:... --goal modify --change-kind behavior
```

`resolve-target` が返す `candidateId` を後続の呼び出しで使います。編集後は実際の diff を確認します。

```powershell
navlyn review-diff --workspace path/to/YourRepo.slnx --profile evidence
```

## MCP で使う

CLI で確認したものと同じ workspace を server に渡します。

### GitHub Copilot / VS Code

リポジトリのルートに `.vscode/mcp.json` を作成します。

```json
{
  "servers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${workspaceFolder}/YourRepo.sln"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

### Codex

リポジトリのルートで次を実行します。

```powershell
codex mcp add navlyn -- navlyn-mcp --workspace path/to/YourRepo.sln
```

### Claude Code

リポジトリのルートに `.mcp.json` を作成します。

```json
{
  "mcpServers": {
    "navlyn": {
      "type": "stdio",
      "command": "navlyn-mcp",
      "args": ["--workspace", "${CLAUDE_PROJECT_DIR:-.}/YourRepo.sln"]
    }
  }
}
```

Navlyn MCP は、読み取り専用の semantic tool surface を一つだけ公開します。workspace を一度設定すれば、エージェントは必要な最小の fact から始め、`candidateId` を再利用し、返された JSON が問いに答えた時点で止まれます。

## エージェントが得るもの

| エージェントの問い | Navlyn MCP tool |
| --- | --- |
| ユーザーが指しているシンボルはどれか | `navlyn_resolve_target` |
| このファイルには何があるか | `navlyn_file_outline` |
| 選んだシンボルの宣言を見たい | `navlyn_symbol_source` |
| 呼び出し元や参照元を知りたい | `navlyn_symbol_edges` |
| 変更前に何を把握すべきか | `navlyn_edit_preflight` |
| この Git diff は何へ影響したか | `navlyn_review_diff` |

必要なのはリポジトリ全体を渡すことではなく、一つずつ答えられる問いです。Navlyn は範囲を絞った JSON の事実を返します。エージェントは調査中ずっと同じ `candidateId` を使い、必要になったときだけ次の関係やソース断片を取得できます。

## 使う場面 / 使わない場面

| Navlyn を使う | Navlyn を使わない |
| --- | --- |
| C# / Visual Basic の symbol identity、overload、partial declaration、project context、target framework、DI、route、related tests、Git diff evidence。 | コメント、文字列、docs、任意のテキスト検索、生成物、runtime proof、security scan、test execution、編集、refactoring、review comment 投稿。 |

テキストで十分なときは通常のファイル読み取りと `rg` を使います。Roslyn/MSBuild の事実でエージェントが読む場所や編集対象を変えるべきときに Navlyn を使います。

## ワークスペースの選び方

ほとんどのリポジトリでは、上の例のように solution/project を直接指定すればよく、`navlyn.workspace.json` は必要ありません。

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
