# Navlyn Excellence Loop

This document defines how to plan and execute changes when the goal is to move Navlyn from useful to exceptional. It is intentionally stricter than normal maintenance workflow.

## Target Bar

Use 9.5/10 as a release-gate threshold, not as the ambition. Plan for 10/10 or better, then expect the first result to fall short under external review.

Score these dimensions independently:

- Problem clarity: the target user can name the pain Navlyn removes.
- First-run path: a normal C#/.NET agent setup reaches useful evidence with minimal choices.
- Agent fitness: tool choice, output shape, stopping behavior, and guardrails reduce wrong-symbol edits.
- OSS readiness: install, docs, contribution path, release path, issue path, and boundaries are credible.
- Evidence strength: claims are backed by tests, evals, demos, or reproducible command output.
- Code quality: design boundaries, tests, public contract compatibility, and maintainability hold under growth.
- Adoption path: the public message makes the right user say "this solves my current problem."

Every dimension must clear 9.5 in the final pass. A high average is not enough if one dimension remains weak.

## Planning Rules

Start from the desired external perception, not from the current file layout.

1. Define the exact audience and the situation where Navlyn should be chosen.
2. State the current score and the gap for each dimension.
3. List the strongest possible objections a skeptical C#/.NET agent user, OSS maintainer, and coding agent would raise.
4. Turn those objections into concrete changes across code, docs, tests, examples, packaging, and evals.
5. Allow breaking changes to public API, CLI aliases, MCP setup, docs structure, and examples when the existing shape blocks the target bar.
6. Prefer one coherent product path over preserving every historical explanation.
7. Define verification before editing, including focused tests, contract checks, manual command inspection, and docs grep checks.

A plan that only renames sections, adds prose, or polishes existing docs is not enough unless the stated gap is only prose clarity.

## Execution Rules

Work in complete passes. A pass is not complete until implementation, docs, examples, tests, and manual inspection match the same product story.

- Keep canonical paths more prominent than advanced surfaces.
- Remove or demote confusing setup paths instead of explaining around them.
- Treat tests and contract checks as the safety net for bold changes.
- Update examples and agent instructions when the recommended behavior changes.
- Verify both machine contracts and human first-run paths.
- Record known limits as product boundaries, not excuses.

## Skeptical Review Pass

After each implementation pass, run a separate skeptical review. Use a fresh checklist and assume the previous implementer optimized for their own plan.

The skeptical review must answer:

- Would a new C#/.NET user understand why Navlyn exists within one minute?
- Would the shortest MCP setup work in a normal one-workspace repository?
- Would a coding agent know the first tool to call and when to stop?
- Are any docs still making normal C#/.NET prerequisites look like Navlyn-specific friction?
- Are advanced commands presented as optional escalation, not as a checklist?
- Are claims about static evidence, runtime proof, security, and tests bounded correctly?
- Is there executable evidence for every important compatibility or behavior claim?
- What would an external reviewer still score below 9.5, and why?

Do not let the implementer declare success solely from intent. Success requires the skeptical review to identify no dimension below 9.5.

## Iteration Rule

Run up to five improvement rounds for a 9.5+ objective:

1. Plan against the full scorecard.
2. Implement the pass.
3. Verify with tests, scripts, command output, and docs checks.
4. Run the skeptical review.
5. If any dimension is below 9.5, write the next plan from that failed dimension and repeat.

Stop early only when every dimension is at or above 9.5 with concrete evidence. If five rounds still miss the bar, publish the remaining gaps plainly instead of flattening them into a higher score.

## Minimum Evidence For A 9.5 Claim

A 9.5+ claim should cite local evidence from at least four categories:

- Passing focused and contract tests.
- A manual first-run command or MCP smoke.
- README or setup docs that show the shortest path.
- Agent instruction examples that match the shortest path.
- Eval or case-study output showing wrong-symbol avoidance or tool-selection discipline.
- Packaging or distribution checks when install experience is part of the claim.

If the evidence is not reproducible from the repository, do not count it toward the score.
