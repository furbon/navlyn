# Codex に Navlyn の使いどころを伝える

[English](navlyn-codex-routing-skill.md)

Codex 用の `navlyn-semantic-routing` スキルは、C# や Visual Basic の型・メソッドについて、Navlyn を使うべき場面を Codex に伝えるファイルです。Navlyn のツールを実際に呼べるようにするには、先に [Codex の MCP 設定](navlyn-client-setup_ja.md#codex)を済ませます。

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

`SKILL.md` と `references` が表示されたら、Codex の新しいセッションを作ります。C# の型やメソッドの参照元を調べるよう依頼し、必要に応じて Navlyn のツールが選ばれるか確認します。スキルの導入だけでは MCP 接続の確認にならないため、[MCP の接続確認](navlyn-client-setup_ja.md#codex)も実行します。

## 更新と削除

更新時は、Navlyn のソースを更新した後で、上記の `-Action Install` をもう一度実行します。削除は次のコマンドです。

```powershell
& (Join-Path $navlynSource 'scripts/install-routing-skill.ps1') -Action Uninstall -DestinationRoot $skillRoot
```

インストーラーは管理情報 `.navlyn-semantic-routing.install.json` を保存します。導入先のファイルが手で変更された場合や別のファイルが混在する場合は、上書きや削除をせず停止します。エラーに示された内容を確認してから対処してください。
