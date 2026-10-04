# Advanced routing

Use this reference when the task requires a capability beyond focused target/read/navigation. Each tool answers a particular static question; availability is not a reason to invoke it. Ordinary reads, `rg`, Git, build, and test tools remain part of the same workflow.

| Missing fact | Tool and input |
| --- | --- |
| Impact around a symbol | `navlyn_impact`: query or candidate; light profile first. Ask only for relevant relationships. |
| Preparation for a semantic edit | `navlyn_prepare_edit`: intended query, candidate, or position. It gathers evidence and does not edit. |
| Actual diff versus intended target | `navlyn_verify_edit`: saved preflight, candidate, query, or position plus diff context. |
| Changed-symbol review | `navlyn_review`: actual Git diff or refs. Ordinary Git inspection can answer text-only review. |
| Project/framework/reference structure | `navlyn_workspace_summary`: relevant project and compact profile. Read a named project file for a declared value. |
| Cache state or load recovery | `navlyn_workspace_status`, `navlyn_workspace_refresh`, `navlyn_doctor`: choose the specific status, justified refresh, or environment diagnosis. |
| Existing compiler diagnostics | `navlyn_diagnostics`: workspace, symbol, or pack mode. Build normally when execution is requested. |
| Likely related tests | `navlyn_tests_for_symbol`, `navlyn_tests_for_diff`: selected target or actual diff. Candidates are not coverage or test results. |
| Static reachability/entrypoint patterns | `navlyn_entrypoints`: target or framework mode; choose the needed bounded depth. |
| DI registrations/consumers | `navlyn_di`: graph, registrations, or impact mode. |
| Source-defined endpoints | `navlyn_routes`: map or impact mode; route pattern for impact. |
| Options registrations/bindings | `navlyn_options`: graph or impact mode; query for impact. |
| Message handlers/call sites | `navlyn_messages`: handlers or flow mode with the message target. |
| EF source model/query sites | `navlyn_ef`: model or impact mode with the relevant entity. |
| Package references/source usage | `navlyn_packages`: package name and usage or impact mode. |
| Source public/protected API changes | `navlyn_public_api_diff`: base ref and optional head. |
| A bounded reading queue after smaller facts fail | `navlyn_context_pack`: concrete goal/target and useful budget. |
| Several already selected supported facts | `navlyn_batch`: independent requests or a necessary dependent chain; report per-request failures. |

Carry project and exact `targetFramework` when they change meaning. Partial declarations, linked source, and generated code may need different compilation contexts. Narrow only as far as the requested conclusion permits; a local file search cannot establish repository-wide absence. Choose one missing fact and stop when it is established.
