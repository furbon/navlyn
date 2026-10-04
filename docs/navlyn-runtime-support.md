# Navlyn runtime support

Navlyn v0.8.6 ships .NET 8 and .NET 10 tool assets. Product tests run on .NET 10, with focused portable checks on Windows, Linux, and macOS. Use a patched .NET 10 SDK for new installations when the repository's build requirements permit it. The SDK used to load a repository and the runtime used to run Navlyn are distinct: removing a future tool runtime asset does not by itself remove navigation of projects targeting that framework.

Microsoft ends .NET 8 support on **2026-11-10**; .NET 10 LTS is supported through **2028-11-14**. These dates were checked on 2026-10-04 against the [official support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

The v0.8 line retains existing assets without another .NET 8 test lane. v0.9.0 moves the tools, installer, scripts, and CI completely to .NET 10. Install a .NET 10 SDK before upgrading; no .NET 8 tool compatibility layer is planned.

## 日本語

v0.8.6 は .NET 8 と .NET 10 のツールを含み、製品テストは .NET 10、OS ごとの確認は小さな CLI／MCP 検証を使います。新規導入では、対象リポジトリのビルド要件が許す場合、最新パッチの .NET 10 SDK を推奨します。Navlyn 自体を動かすランタイムと、対象プロジェクトを読み込む SDK は別です。将来 .NET 8 向けツールを取り除いても、それだけで .NET 8 向けプロジェクトの解析ができなくなるわけではありません。

.NET 8 の Microsoft サポートは **2026-11-10** に終了します。.NET 10 LTS は **2028-11-14** までです。v0.8 系では既存のツール資産を維持しますが、.NET 8 用のテストは繰り返しません。v0.9.0 ではツール・導入スクリプト・CI をすべて .NET 10 に移行します。更新前に .NET 10 SDK を導入してください。
