# Navlyn ワークスペース設定

このページは、`.slnx`、`.sln`、`.csproj`、`.vbproj` を直接指定するだけでは足りない場合にだけ読んでください。通常のセットアップは [README_ja.md](../README_ja.md) にあります。

`navlyn.workspace.json` は、候補が複数あるリポジトリで、どの solution/project を使うか固定するための Navlyn 設定です。ワークスペースそのものではありません。

## まず対象を一つ決める

このファイルが必要なリポジトリでも、多くの場合はこれだけで足ります。

```json
{
  "primaryWorkspace": "YourRepo.sln"
}
```

パスは `navlyn.workspace.json` からの相対パスです。`primaryWorkspace` があるときは、それが他の候補探索設定より優先されます。

## 候補から選ばせる

候補にするディレクトリを限定して Navlyn に選ばせたい場合だけ、`primaryWorkspace` を省略します。

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

各ディレクトリでは直下だけを候補として調べます。それでも候補が曖昧なら、Navlyn は推測せず、`primaryWorkspace` または明示的な `--workspace` を求めます。

`excludes`、`generatedFolders`、`tests.include` が制御するのは候補の選択だけです。選ばれた solution/project を読み込んだ後のソースを除外する設定ではありません。

## 項目一覧

| 項目 | 既定値 | 使う場面 |
| --- | --- | --- |
| `$schema` | なし | エディタの補完用。Navlyn の挙動は変わらない。 |
| `primaryWorkspace` | なし | 読み込む一つの `.code-workspace`、`.slnx`、`.sln`、`.csproj`、`.vbproj` を指定する。 |
| `workspaceCandidates` | `["."]` | `primaryWorkspace` がない場合に調べるファイルまたはディレクトリ。 |
| `excludes` | `[]` | 候補から外すパス。接頭辞または単純なワイルドカードを使える。 |
| `generatedFolders` | `[]` | 生成 project を含むため候補から外すパス。 |
| `tests.include` | `true` | `false` にすると、探索時に test project らしい候補を外す。 |
| `tests.projects` | `[]` | 予約項目。受理されるが、現在は候補選択を変えない。 |
| `defaultRootPolicy` | CLI: `all`、MCP: `repo-relative` | リポジトリ外のワークスペースを設定から選べるか制限する。 |
| `allowRoots` | `[]` | `defaultRootPolicy` が `allow-listed` のときに許可する追加ルート。 |
| `cacheHints.enabled` | `false` | `--cache auto` が Navlyn の軽量 workspace cache manifest を使えるようにする。 |
| `cacheHints.directory` | `.navlyn/cache` | cache manifest のディレクトリ。 |

`--workspace-root-policy` は、そのコマンドだけ `defaultRootPolicy` を上書きします。意図してリポジトリ外を使う場合は `allow-listed` と `allowRoots` を組み合わせ、広い許可が必要な場合だけ `all` を使います。

JSON schema は [navlyn-workspace.schema.json](schemas/navlyn-workspace.schema.json) です。エディタ補完には便利ですが、各設定を使うべき場面はこのページを基準にしてください。

## `auto`

`--workspace auto` は、設定ファイルを置かない場合の選択肢です。リポジトリのルートから、`navlyn.workspace.json`、`.code-workspace`、`.slnx`、`.sln`、`.csproj`、`.vbproj` の順でトップレベルの候補を見ます。選択が一意なときだけ使ってください。
