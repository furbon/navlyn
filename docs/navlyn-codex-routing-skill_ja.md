# Codex に Navlyn の使いどころを伝える

[English](navlyn-codex-routing-skill.md)

任意の `navlyn-semantic-routing` スキルは、C# や Visual Basic の束縛が不確かな場面で、意味的な証拠を得る方法を Codex に伝えます。必要時だけ使う場合は、インストール済みの `navlyn` CLI で足り、MCP サーバーの常時起動は不要です。同じセッションで意味解析を繰り返す場合は、[Codex の MCP 設定](navlyn-client-setup_ja.md#codex)でワークスペースを再利用できます。通常の読取り・検索・明確な局所編集には通常ツールを使います。

軽量な CLI 導線として、リポジトリの指示に「束縛や関係が不確かな場合に Navlyn CLI を利用できる。既知の呼出し位置には `navlyn read --workspace <project> --file <source> --line <line> --column <column> --view body --external-source decompiled` で参照メンバーの本体を取得できる」と記載できます。以下のスキルは追加の判断支援であり、ファイル読取りの前提ではありません。

## 1. Navlyn のソースを用意する

スキルは NuGet パッケージに含まれません。[Navlyn の GitHub リポジトリ](https://github.com/furbon/navlyn)を取得します。Git がある場合は次を実行します。

```powershell
$navlynSource = Join-Path $HOME 'navlyn-source'
git clone https://github.com/furbon/navlyn.git $navlynSource
```

すでにソースがある場合は、取得し直さず、次の手順の `$navlynSource` をその場所に置き換えます。

## 2. 作業するリポジトリに導入する

調べたいリポジトリのルートで PowerShell を開きます。上で別の保存先を使った場合は、`$navlynSource` をその絶対パスに合わせてください。

```powershell
$navlynSource = Join-Path $HOME 'navlyn-source'
$skillRoot = Join-Path (Get-Location) '.agents/skills'
New-Item -ItemType Directory -Force $skillRoot | Out-Null
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Install -DestinationRoot $skillRoot
Get-ChildItem (Join-Path $skillRoot 'navlyn-semantic-routing')
```

`SKILL.md` と `references` が表示されたら、Codex の新しいセッションを作り、束縛が不確かな事実を依頼して実際のツール選択を確認します。スキルは実行ツールをインストールしません。CLI は `navlyn --version`、MCP を選んだ場合は [MCP の接続確認](navlyn-client-setup_ja.md#codex)で確認します。

## 更新と削除

更新時は、Navlyn のソースを更新した後で、上記の `-Action Install` をもう一度実行します。削除は次のコマンドです。

```powershell
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Uninstall -DestinationRoot $skillRoot
```

インストーラーは管理情報 `.navlyn-semantic-routing.install.json` を保存します。導入先のファイルが手で変更された場合や別のファイルが混在する場合は、上書きや削除をせず停止します。エラーに示された内容を確認してから対処してください。
