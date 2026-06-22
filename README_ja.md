# Navlyn

Navlyn は、C#/.NET リポジトリを対象にした、エージェント、自動化、開発者向けのセマンティックコードナビゲーション CLI です。

`rg` のようなテキスト検索を置き換えるものではありません。コメント、文字列、ドキュメント、非 C# ファイルの探索にはテキスト検索を使い、C# のシンボル、定義、参照、実装、診断、呼び出し関係など、Roslyn による意味解析が必要な場面で Navlyn を使います。

## できること

- `.slnx`、`.sln`、`.csproj` ワークスペースを読み込む。
- ワークスペース概要とコンパイラ診断を JSON で出力する。
- C# シンボルを名前、位置、ファイル範囲、曖昧なクエリから探す。
- 定義、参照、実装、型階層、caller、callee を調べる。
- semantic outline と詳細な symbol facts を取得する。
- `batch` で複数の問い合わせを 1 回のワークスペース読み込みで実行する。
- 生成コードを既定で含め、対応コマンドでは `--exclude-generated` で除外する。

## 使い始める

リポジトリルートから実行します。

```powershell
dotnet restore navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- check --workspace navlyn.slnx
```

.NET local tool として pack して使うこともできます。

```powershell
dotnet pack navlyn\navlyn.csproj
dotnet new tool-manifest
dotnet tool install navlyn --add-source .\navlyn\bin\Release --version 0.1.0
dotnet tool run navlyn check --workspace navlyn.slnx
```

## 基本的な使い方

```powershell
dotnet run --no-launch-profile --project navlyn -- overview --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- diagnostics --workspace navlyn.slnx
dotnet run --no-launch-profile --project navlyn -- find --workspace navlyn.slnx --query CheckCommand --assume-kind NamedType
dotnet run --no-launch-profile --project navlyn -- definition --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --column 37
dotnet run --no-launch-profile --project navlyn -- references --workspace navlyn.slnx --file navlyn\Cli\NavlynCli.cs --line 31 --column 37
```

すべてのコマンド契約と JSON shape は [`docs/navlyn-cli-commands.md`](docs/navlyn-cli-commands.md) を参照してください。

## 使い方のヒント

- Navlyn はコマンド結果を stdout に JSON として出力します。
- エラー、警告、進捗、診断ログは stderr に出力します。
- パスは可能な限りリポジトリ相対で出力します。
- ユーザー向けの行番号と列番号は 1-based です。
- 取得できる source location では、1-based exclusive end position として `endLine` / `endColumn` を返します。
