# Python async scoring package validation

Date: 2026-10-28. Python adapter: **0.15.0**. Ruleset: `python-2026-10-28-async-scoring-v1`.

Standalone analyzer work was saved in commit `3f7c41a`. This follow-up packages the helper-path and async-scoring reports, validates their consistency, and integrates Async/Blocking Usage into canonical evidence, findings, comparisons, and score gates. Method Complexity is unchanged. **Error Handling remains unscored.**

Python can now score up to two of nine dimensions. Projects without eligible production async implementations retain only Method Complexity; absence of an async population does not earn a 10. Adapter 0.14.0 evidence remains readable under its original unscored async contract. Comparisons across ruleset versions are rejected.

## Installed corpus results

| Project | Method Complexity | Async/Blocking Usage | Affected / eligible owners | Owners with unresolved paths |
| --- | ---: | ---: | ---: | ---: |
| Requests | 7.0 | Unavailable | 0 / 0 | 0 |
| Starlette | 7.1 | 10 | 0 / 135 | 129 |
| HTTPie | 6.8 | Unavailable | 0 / 0 | 0 |
| Django | 5.6 | 10 | 0 / 266 | 257 |

All four installed runs matched standalone async scoring. Method Complexity, Error Handling, source facts, and prior async reports were unchanged apart from run identity and timestamps. Compatible self-baseline gates passed; cross-version baselines were rejected. Pinned target revisions stayed clean and unchanged.

The async scores describe recognized blocking patterns within the bounded source analysis. They do not establish runtime performance or prove unresolved calls safe. The skill must show affected/eligible counts and unresolved-owner counts alongside these partial scores. The canonical validator checks report consistency and provenance; it does not independently replay the Python AST or authenticate coherently rewritten source claims.

## Package and skill checks

- 243 Python unit tests passed.
- 256 JavaScript/TypeScript tests passed.
- 57 installed Python evidence tests passed, including ladder boundaries, historical compatibility, tampering, failure handling, and score regression gates.
- 7 shared source-facts conformance tests passed.
- 22 final installed skill integration checks passed.
- 16 skill runtime, catalog, and offline harness checks passed; structural skill validation passed.
- All 35 bundled Python files matched source bytes. The final archive's 139 runtime files matched the archive used for installed evidence tests and corpus runs; only documentation was clarified afterward. Skill integration ran against the final archive.

The fresh reporting fixture scores Method Complexity 10 and Async/Blocking Usage 8, with one affected owner among 25. Repeated helper paths count that owner once. Documented intent and an offloaded callback are excluded; one unresolved owner stays visible. Error Handling stays unscored. The fixture and rubric were validated offline; no live model reporting evaluation was run.

The local npm archive is `TestResults/python-async-scoring-package-2026-10-28/final/codemetrics-ai-0.5.0.tgz`. The npm wrapper version remains 0.5.0, independently of Python adapter 0.15.0. The companion skill changes are in `E:/repos/ai_tools-js-0.4-release/skills/code-scorecard` and retain the local-package requirement.

See [the machine-readable validation record](python-async-scoring-package-2026-10-28.json) for archive and evidence hashes, target revisions, run identities, and test log locations. Packaging and skill changes are local and uncommitted. Nothing was pushed, tagged, or published in this follow-up.
