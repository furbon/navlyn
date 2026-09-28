# Navlyn ワークスペース設定

通常は、調べるリポジトリのルートで `navlyn doctor --workspace auto` を実行するだけです。最初の実行手順は[Windows で Navlyn を試す](navlyn-first-10-minutes_ja.md)にあります。候補が複数ある場合は、まず `--workspace .\YourRepo.slnx` のように対象を直接指定してください。

チームで使う対象を固定したい場合は、リポジトリのルートに `navlyn.workspace.json` を置きます。このファイルで、読み込むソリューションやプロジェクトを指定します。

## まず対象を一つ決める

このファイルが必要なリポジトリでも、多くの場合はこれだけで足ります。

```json
{
  "primaryWorkspace": "YourRepo.sln"
}
```

パスは `navlyn.workspace.json` からの相対パスです。`primaryWorkspace` があるときは、それが他の候補探索設定より優先されます。

## 候補から選ばせる

調べるディレクトリを限定し、その中から Navlyn に選ばせたい場合は `primaryWorkspace` を省略します。

```json
{
  "workspaceCandidates": ["src", "tools"],
  "excludes": ["artifacts", "bin", "obj"],
  "generatedFolders": ["src/Generated"],
  "tests": {
    "include": false
  }
}
```

各ディレクトリでは直下だけを候補として調べます。それでも候補が複数ある場合は選択を止めます。`primaryWorkspace` または `--workspace` で対象を指定してください。

`excludes`、`generatedFolders`、`tests.include` が制御するのは候補の選択だけです。選ばれたソリューションやプロジェクトを読み込んだ後のソースを除外する設定ではありません。

## 項目一覧

| 項目 | 既定値 | 使う場面 |
| --- | --- | --- |
| `$schema` | なし | エディタの補完用。Navlyn の挙動は変わらない。 |
| `primaryWorkspace` | なし | 読み込む一つの `.code-workspace`、`.slnx`、`.sln`、`.csproj`、`.vbproj` を指定する。 |
| `workspaceCandidates` | `["."]` | `primaryWorkspace` がない場合に調べるファイルまたはディレクトリ。 |
| `excludes` | `[]` | 候補から外すパス。接頭辞または単純なワイルドカードを使える。 |
| `generatedFolders` | `[]` | 生成されたプロジェクトを含むため候補から外すパス。 |
| `tests.include` | `true` | `false` にすると、探索時にテストプロジェクトらしい候補を外す。 |
| `tests.projects` | `[]` | 予約項目。受理されるが、現在は候補選択を変えない。 |
| `defaultRootPolicy` | CLI: `all`、MCP: `repo-relative` | リポジトリ外のワークスペースを設定から選べるか制限する。 |
| `allowRoots` | `[]` | `defaultRootPolicy` が `allow-listed` のときに許可する追加ルート。 |
| `cacheHints.enabled` | `false` | `--cache auto` で軽量なワークスペース情報を保存できるようにする。 |
| `cacheHints.directory` | `.navlyn/cache` | 保存先ディレクトリ。 |

`--workspace-root-policy` は、そのコマンドだけ `defaultRootPolicy` を上書きします。意図してリポジトリ外を使う場合は `allow-listed` と `allowRoots` を組み合わせ、広い許可が必要な場合だけ `all` を使います。

エディタの補完には [JSON スキーマ](schemas/navlyn-workspace.schema.json)を使えます。

## `auto`

`--workspace auto` は、CLI で設定ファイルを置かない場合の指定です。MCP サーバーも、`--workspace` を省略すると同じ方法で選びます。リポジトリのルートで `navlyn.workspace.json`、`.code-workspace`、`.slnx`、`.sln`、`.csproj`、`.vbproj` の順に直下の候補を調べます。同じ優先度の候補が複数ある場合は、対象を選ばずエラーを返します。
