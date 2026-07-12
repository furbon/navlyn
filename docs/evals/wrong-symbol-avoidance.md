# Navlyn Wrong-Symbol Avoidance Eval

This eval proves the core Navlyn claim with executable scenarios: text search can find a plausible but unsafe target, while Navlyn either fails closed or anchors the intended Roslyn symbol.

It is not a model benchmark. It is a product proof for the failure mode Navlyn is built to reduce.

Run from the repository root:

```powershell
./scripts/test-wrong-symbol-avoidance-eval.ps1 -NoBuild
```

The scorer checks:

- text-search baseline reaches a wrong or inactive source location;
- Navlyn does not select from ambiguous intent without narrowing;
- Navlyn can anchor the intended target when a source position is supplied;
- inactive conditional source is not treated as an editable source symbol;
- stdout is valid JSON and stderr is clean for successful Navlyn commands.

## Scenarios

| Scenario | Text-search risk | Navlyn behavior |
| --- | --- | --- |
| `ambiguous-enemy-manager` | The first textual `EnemyManager` declaration is `Alpha.EnemyManager`, while the intended symbol is `Beta.EnemyManager`. | `target --query EnemyManager` returns `confidence: ambiguous`; source-position `target` anchors `Beta.EnemyManager`. |
| `inactive-conditional-symbol` | Text search finds `InactiveBranchSymbol` in a disabled branch. | `target --query InactiveBranchSymbol` returns no candidates because the active project context excludes that symbol. |

This eval should grow whenever a real agent or user reports a wrong-symbol failure mode.
