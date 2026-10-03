# Navlyn runtime support

Navlyn v0.8.6 ships .NET 8 and .NET 10 tool assets and tests both on Windows, Linux, and macOS. Use a patched .NET 10 SDK for new installations when the repository's build requirements permit it. The SDK used to load a repository and the runtime used to run Navlyn are distinct: removing a future tool runtime asset does not by itself remove navigation of projects targeting that framework.

Microsoft ends .NET 8 support on **2026-11-10**; .NET 10 LTS is supported through **2028-11-14**. These dates were checked on 2026-10-04 against the [official support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

The v0.8 line retains both assets. Plan the next minor release's runtime baseline around .NET 10, announce any .NET 8 tool-asset removal in its release notes, and validate older target-framework repositories with their required SDKs before changing that policy. Navlyn does not extend Microsoft's support for an expired runtime.

## 日本語

v0.8.6 は .NET 8 と .NET 10 のツールを含み、Windows・Linux・macOS で両方を検証します。新規導入では、対象リポジトリのビルド要件が許す場合、最新パッチの .NET 10 SDK を推奨します。Navlyn 自体を動かすランタイムと、対象プロジェクトを読み込む SDK は別です。将来 .NET 8 向けツールを取り除いても、それだけで .NET 8 向けプロジェクトの解析ができなくなるわけではありません。

.NET 8 の Microsoft サポートは **2026-11-10** に終了します。.NET 10 LTS は **2028-11-14** までです。v0.8 系は両方のツールを維持し、次のマイナーリリースでは .NET 10 を基準にする方針と、古い対象フレームワークを必要な SDK で読み込めることを確認してから変更します。
