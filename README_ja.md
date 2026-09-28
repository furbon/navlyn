# Navlyn

[English](https://github.com/furbon/navlyn/blob/main/README.md)

Navlyn は、コーディングエージェントが .NET リポジトリの型やメソッドを取り違えないためのツールです。C# を中心に、Visual Basic にも一部対応しています。宣言の場所や参照元、編集前に読んでおきたい箇所を調べ、編集後は差分が触れた型やメソッドを確認できます。

エージェントからは MCP サーバーの `navlyn-mcp`、ターミナルや CI からは `navlyn` コマンドを使います。どちらもローカルのコードを読み取り、結果を JSON で返します。

## 使いたい環境から始める

- [VS Code と GitHub Copilot で使う](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup_ja.md#vs-code-と-github-copilot)
- [GitHub Copilot CLI で使う](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup_ja.md#github-copilot-cli)
- [Codex で使う](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup_ja.md#codex)
- [Claude Code で使う](https://github.com/furbon/navlyn/blob/main/docs/navlyn-client-setup_ja.md#claude-code)
- [ターミナルで型を調べる](https://github.com/furbon/navlyn/blob/main/docs/navlyn-first-10-minutes_ja.md)

Codex に Navlyn を使う場面も覚えさせる場合は、[Codex 用スキルの導入](https://github.com/furbon/navlyn/blob/main/docs/navlyn-codex-routing-skill_ja.md)へ進んでください。

## ターミナルで試す

調べたいリポジトリを読み込める .NET SDK が必要です。NuGet からツールをインストールします。

```powershell
dotnet tool install --global navlyn --version 0.8.1
```

新しいターミナルを開き、調べたいリポジトリのルートで `navlyn` が対象を見つけられるか確認します。

```powershell
navlyn doctor --workspace auto
```

`auto` で対象が一つに決まらない場合は、使いたい `.slnx`、`.sln`、`.csproj`、`.vbproj` を指定してください。[10 分で試す手順](https://github.com/furbon/navlyn/blob/main/docs/navlyn-first-10-minutes_ja.md)では、ツールを専用ディレクトリに入れ、型を一つ調べるところまで説明します。

対象の選び方は[ワークスペース設定](https://github.com/furbon/navlyn/blob/main/docs/navlyn-workspace_ja.md)、全コマンドと返り値は[CLI リファレンス](https://github.com/furbon/navlyn/blob/main/docs/navlyn-cli-commands.md)と[MCP リファレンス](https://github.com/furbon/navlyn/blob/main/docs/navlyn-mcp-server.md)にあります。

Navlyn のコマンドはローカルで動き、コードを外部へ送信しません。コードの編集やテストの実行は行わず、実行時の動作も証明できません。プロジェクトの読み込みには MSBuild を使うため、信頼できるリポジトリで実行してください。詳しくは[制約と注意点](https://github.com/furbon/navlyn/blob/main/docs/navlyn-limitations.md)を参照してください。

MIT ライセンスで公開しています。詳しくは [LICENSE](https://github.com/furbon/navlyn/blob/main/LICENSE) を参照してください。
