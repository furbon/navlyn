# Navlyn

[English](https://github.com/furbon/navlyn/blob/main/README.md)

Navlyn は、束縛や関係が不確かな .NET コードについて、コンパイラーに基づく事実をエージェントに返すツールです。C# を中心に Visual Basic にも一部対応し、呼出し先のオーバーロード、参照 DLL の実装、呼出し元、変更の影響などを調べられます。

必要時だけ意味的な証拠を得る場合は、エージェントからインストール済みの `navlyn` CLI を呼べます。MCP サーバーの常時起動は不要です。同じセッションで意味解析を繰り返す場合は、共有ワークスペースを使う `navlyn-mcp` が選択肢になります。どちらもローカルの事実を JSON で返し、通常の読取り・検索・明確な局所編集には通常ツールを使います。[CLI 導線とルーティング](https://github.com/furbon/navlyn/blob/main/docs/navlyn-codex-routing-skill_ja.md)を参照してください。

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
dotnet tool install --global navlyn --version 0.9.2
```

新しいターミナルを開き、調べたいリポジトリのルートで `navlyn` が対象を見つけられるか確認します。

```powershell
navlyn doctor --workspace auto
```

`auto` で対象が一つに決まらない場合は、使いたい `.slnx`、`.sln`、`.csproj`、`.vbproj` を指定してください。[10 分で試す手順](https://github.com/furbon/navlyn/blob/main/docs/navlyn-first-10-minutes_ja.md)では、ツールを専用ディレクトリに入れ、型を一つ調べるところまで説明します。

対象の選び方は[ワークスペース設定](https://github.com/furbon/navlyn/blob/main/docs/navlyn-workspace_ja.md)、全コマンドと返り値は[CLI リファレンス](https://github.com/furbon/navlyn/blob/main/docs/navlyn-cli-commands.md)と[MCP リファレンス](https://github.com/furbon/navlyn/blob/main/docs/navlyn-mcp-server.md)にあります。

Navlyn のコマンドはローカルで動き、コードを外部へ送信しません。コードの編集やテストの実行は行わず、実行時の動作も証明できません。プロジェクトの読み込みには MSBuild を使うため、信頼できるリポジトリで実行してください。詳しくは[制約と注意点](https://github.com/furbon/navlyn/blob/main/docs/navlyn-limitations.md)を参照してください。

Navlyn v0.9.2 は .NET 10 SDK が必要で、`net10.0` 向けツールのみを配布します。[ランタイムの対応方針](https://github.com/furbon/navlyn/blob/main/docs/navlyn-runtime-support.md)を参照してください。

MIT ライセンスで公開しています。詳しくは [LICENSE](https://github.com/furbon/navlyn/blob/main/LICENSE) を参照してください。
