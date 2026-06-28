# Agent Instructions

- Use `navlyn repo-graph --profile compact` before broad C# workspace investigation.
- Use `navlyn resolve-target` to choose a candidate id before deeper symbol-specific commands.
- Use `navlyn find` when you need a broader candidate list.
- Use `navlyn context-pack --goal modify --profile compact` before non-trivial edits.
- Use `navlyn review-diff --profile evidence` for pull request review facts.
- Use `navlyn context-pack --profile compact` when output budget is tight.
- In MCP, prefer `navlyn_batch` when collecting several facts from the same workspace.
- Treat `review-pack` findings as evidence-backed signals, not final review comments.
- Keep Navlyn stdout as JSON and send diagnostics or notes elsewhere.
