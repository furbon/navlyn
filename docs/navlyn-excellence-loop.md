# Navlyn improvement review

Review product changes against concrete user needs and observed behavior. Do not use self-assigned quality scores, fixed iteration counts, or large evaluation campaigns as release gates.

## Planning

Define the intended behavior, inspect the current implementation, and list evidence needed to decide whether it works. Include relevant implementation, selection, freshness, JSON contracts, documentation, installation, packaging, and publication concerns. Unknown defects found during the work remain in scope; the first fixed symptom does not end the review.

## Implementation and review

Keep changes verifiable. Inspect adjacent execution paths and shared assumptions. Prefer one reusable implementation over fixes repeated independently across CLI, MCP, and batch. Preserve meaningful assertions when replacing expensive tests with smaller fixtures. Delete checks that only exercise an evaluation framework or repeat evidence already established by product tests.

Run affected tests first. After integration, run the primary suite once; rerun only checks whose inputs changed or whose results failed. Diagnose the actual failure before retrying. Do not extend timeouts to hide unnecessary workspace loads, repeated builds, or stalled processes.

## Evidence

State observed results, versions, environment, and material limits. Measure cold and warm behavior separately in one persistent MCP session. Compare like environments. Use a small fixed set of real tasks to assess agent usefulness when routing or navigation changes. Report unchanged or worse results honestly. Source tests, package smoke, public package availability, and client negotiation prove different facts; perform each once where relevant and reuse the exact tested artifacts for publication.

See [development workflow](navlyn-development-workflow.md) and [release plans](plans/v0.8.7.md).
