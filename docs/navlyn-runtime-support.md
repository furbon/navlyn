# Runtime support

Navlyn v0.9.0 requires a patched .NET 10 SDK. CLI, MCP, installer, package assets, tests, and CI use .NET 10 only. Upgrade the SDK before updating either tool. There is no .NET 8 tool asset or installer fallback.

The target repository can contain other framework targets if the installed SDK/MSBuild and restored dependencies can load them. `--target-framework` (MCP `targetFramework`) selects a particular binding context; it does not change the Navlyn runtime.

.NET 10 LTS is supported through **2028-11-14**, according to the [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), checked on 2026-10-04.

## 日本語

Navlyn v0.9.0 は最新パッチの .NET 10 SDK が必要です。CLI、MCP、導入スクリプト、パッケージ、テスト、CI は .NET 10 のみを使います。ツール更新前に SDK を更新してください。.NET 8 向けツールや導入時のフォールバックはありません。

解析対象には別のフレームワークを含められますが、SDK/MSBuild と復元済み依存関係によって読み込める必要があります。`--target-framework`（MCP は `targetFramework`）は解析時の束縛先を選ぶオプションです。
