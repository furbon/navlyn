# Windows で Navlyn を試す

[English](navlyn-first-10-minutes.md)

[PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) と、調べたいリポジトリを読み込める [.NET SDK](https://dotnet.microsoft.com/download) を用意してください。リポジトリに `global.json` がある場合は、そこに指定された SDK のバージョンも確認してください。

## 1. ツールをインストールする

PowerShell で次を実行します。Navlyn を一時ディレクトリに入れるので、既存の .NET ツール設定には影響しません。

```powershell
$tools = Join-Path $env:TEMP "navlyn-tools-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $tools | Out-Null
dotnet tool install navlyn --tool-path $tools --version 0.8.4
dotnet tool list --tool-path $tools
```

一覧に `navlyn` が `0.8.4` と表示されることを確認します。

## 2. 調べるリポジトリで実行する

`C:\path\to\your-repository` を、調べたい C# または Visual Basic リポジトリの絶対パスに置き換えます。

```powershell
Set-Location 'C:\path\to\your-repository'
& (Join-Path $tools 'navlyn.exe') doctor --workspace auto
```

`doctor` が対象を選べたら、実際にそのリポジトリにある型名を使って検索します。`PaymentService` は例なので置き換えてください。

```powershell
& (Join-Path $tools 'navlyn.exe') target --workspace auto --query PaymentService --assume-kind NamedType --limit 10
```

結果の JSON にある `candidateId` をコピーすると、その宣言を読めます。`sym:v1:...` は実際に返った値に置き換えます。

```powershell
& (Join-Path $tools 'navlyn.exe') read --workspace auto --candidate-id 'sym:v1:...' --view declaration --max-lines 80
```

## うまくいかない場合

- `dotnet --list-sdks` で SDK を確認します。`NAVLYN1201` が `global.json` を指す場合は、そこで指定された SDK をインストールします。
- 対象が複数あると表示されたら、`--workspace auto` を `--workspace .\YourRepo.slnx` のように置き換えます。`.sln`、`.csproj`、`.vbproj` も指定できます。
- コマンドが見つからなければ、上記の `Join-Path $tools` を使い、`dotnet tool list --tool-path $tools` でインストール先を確認します。
- 終わったら、作成したディレクトリを `Remove-Item -LiteralPath $tools -Recurse` で削除できます。削除する前に `$tools` の値を確認してください。

AI ツールから使う場合は、[使っているクライアントの設定手順](navlyn-client-setup_ja.md)へ進みます。VS Code では[設定バンドル](navlyn-client-setup_ja.md#vs-code-の設定バンドル)を使い、書き込みを行わない計画を確認してから適用し、実際のツール呼び出しまで確認できます。手動設定の手順もあります。複数のソリューションがある場合は、[ワークスペース設定](navlyn-workspace_ja.md)を参照してください。
