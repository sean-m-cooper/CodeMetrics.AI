# CodeTestament local producer migration checkpoint

Date: 2026-10-29. Branch: codex/codetestament-migration. Local Windows verification only.

The Python implementation and companion skill were already committed at
`16d3118` and `33aaa52`, respectively. Both had no pending Python source changes.
The producer migration starts from that behavior. Companion skill defaults are
unchanged; this checkpoint does not publish packages, push tags or rename GitHub.

## Executed validation

- .NET Release suite: 1,151 passed, zero failed. Formatting verification passed.
- JS/TS full suite: 257 passed and one new migration fixture failed because it
  supplied a directory where package.json is required. After correcting that
  test, both migration tests passed in a targeted rerun; production code did not
  change after the full run. Installed npm package smoke passed.
- Installed Python package suite: 59 passed, zero failed. Coverage collection
  used an isolated Python 3.12 environment on E: with coverage.py 7.6.1.
  The initial missing-coverage dependency failure was resolved in that environment.
- Old and new npm distributions and NuGet tools installed side by side.
  NuGet sources and caches were isolated; no public-feed fallback was available.
- Paired .NET, JS/TS and Python fixtures matched all substantive evidence after
  the documented metadata exclusions. All three cross-brand baseline gates
  rejected comparison. The complete .NET catalog definitions matched.
- Git whitespace check passed. Historical fixtures, source-facts identifiers,
  rulesets, scoring policies and CMAI definitions were not edited.

## Exact locally built artifacts

Legacy source behavior: `16d3118`. Migration archives were built from the local
changes on top of plan commit `e984e35`; archive hashes identify the tested bytes.
These are locally built archives, not downloaded registry artifacts.

| Artifact | SHA-256 |
| --- | --- |
| codemetrics-ai-0.6.0-preview.2.tgz | `1ec5a51478f6b8a13a9addb286089503a3b85fa0cafebe293c951969f9e0fcc8` |
| codetestament-0.6.0-preview.3.tgz | `695888c32bac941f487b206699e3522a1d85b37facebfdddc2879eea6e2299fc` |
| CodeMetrics.AI.2.3.1.nupkg | `fd504c8b5c690e39ca82906eea9409bc133627aeb4d22a56d7ecffcd3bdcdf27` |
| CodeTestament.Scorecard.2.4.0.nupkg | `de8fedde9941338ac47e5bb574e466f5e9183733d8562c637312d01ea617c863` |

Local receipt: `TestResults/brand-migration/verify-9b7c3dff-1d8e-443c-9751-155d7620ce14/receipt.json`.
Generated outputs remain untracked/ignored; do not add them to the repository.
Use [the verification command](codetestament-migration.md#paired-artifact-verification)
to reproduce the comparison with explicitly selected archives.

## Remaining release work

That subsequent stage is now implemented and tested locally: explicit legacy/new
package selection, package-aware caches and installed-package integration. The
canonical skill has moved into this repository; see the
[skill ownership guide](scorecard-skill.md). This original checkpoint records
producer parity; later skill validation records live under the skill evals.
Linux execution, public registry installs, publisher authorization, SARIF upload
stream behavior and the stable npm source boundary are not established by this
checkpoint. Keep existing public defaults until the producer/consumer release
checks in the [migration plan](codetestament-migration-plan.md) are complete.
