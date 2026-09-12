# .NET 2.3.0 public-corpus verification

Recorded on 2026-09-11 from four pinned public repositories. This is a targeted fidelity check, not a calibrated ranking or a substitute for source review. All four runs use Release / Any CPU, schema v3, ruleset `dotnet-2026-09-11`, the local CodeMetrics.AI 2.3.0 package and shared evidence validator 0.3.0. Thresholds and score formulas were not changed.

All analyzer and validator exits are zero. Each fresh run has matching invocation/evidence run and audit IDs. Repository source was unchanged. No test suite was executed in the target repositories and no coverage report was supplied; prior Release-build preflight results were retained. The SDK, clones, caches and artifacts stayed on E:.

## Scores

Every score and overall below is a **partial static assessment**. All nine dimensions are scored. Different target-framework populations, project roles and live package observations limit direct comparisons.

| Dimension | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore |
|---|---:|---:|---:|---:|
| Architecture & SOLID | 9.3 | 4.1 | 2 | 4 |
| Code Quality | 9.3 | 2.4 | 9 | 6.7 |
| Testing | 8 | 6 | 6 | 6 |
| Security | 10 | 10 | 2 | 0 |
| Error Handling | 0 | 0 | 10 | 0 |
| Documentation | 9 | 2 | 3 | 4 |
| Dependency Management | 2 | 2 | 2 | 2 |
| Performance & Async | 0 | 10 | 2 | 0 |
| Maintainability | 7.3 | 7.3 | 6 | 6.7 |
| Overall, partial assessment (9/9) | 6.1 | 4.9 | 4.7 | 3.3 |

## Verified corrections

- Polly: production population excludes Specs, test utilities, benchmarks and snippets. The circuit-controller classes no longer trigger web-controller layering checks. Architecture is 9.3 versus 2 in the earlier 2.2.0 snapshot; testing counts 2,632 unique methods and one skipped source site, rather than multiplying test sites by framework.
- Newtonsoft.Json: completed-task guards and their verified helper eliminate the previous blocking findings; Performance & Async is 10 versus 0. The repository README above `Src` is recognized. Documentation remains 2 because the other documented gates still bind.
- SimplCommerce: the dependency child selects SDK 8.0.425 while the analyzer runs on SDK 10.0.401. No outside-repository working-directory workaround is used. The unselected netcoreapp2.0 build task does not produce an unsupported-framework finding. Its unchanged overall does not mean the evidence was unchanged.
- OrchardCore: the three `Build=false` fixture projects are excluded from both source and package scope. The run now completes. Existing duplicate-source and package-advisory workspace warnings remain in evidence; selected production compilation failures would still make analysis incomplete.

These are incompatible cross-ruleset snapshots, not baseline gates or evidence of source improvement. The 2.2.0 preflight snapshots are retained under `TestResults/public-corpus-runs/`; early diagnostic packages built during this change are kept separately from the final 2.3.0 results.

## Provenance

### Polly

- Commit: `1a80392b1f093f40e59c515f4aeb989bea5db857`
- Entry point: `Polly.slnx`
- Run ID: `06c22d4f-827d-4da5-af49-21e9ef92d654`
- Audit ID: `7d764a0a-c1b3-48f3-a59c-c2d188149f83`
- Population: 21/53 loaded project/framework instances analyzed; 2,109 types and 10,988 members.
- Artifacts: `E:/repos/Polly/.scorecard/dotnet/runs/06c22d4f-827d-4da5-af49-21e9ef92d654/`

### Newtonsoft.Json

- Commit: `09bb545d72969ad7fb4ea07db0d5c34f4fc07877`
- Entry point: `Src\Newtonsoft.Json.slnx`
- Run ID: `86daec32-53e9-4a74-be65-9c092264d3fd`
- Audit ID: `f41640ac-dd60-4df5-a49a-9ca5b40b4e83`
- Population: 9/16 loaded project/framework instances analyzed; 1,760 types and 25,381 members.
- Artifacts: `E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/86daec32-53e9-4a74-be65-9c092264d3fd/`

### SimplCommerce

- Commit: `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`
- Entry point: `SimplCommerce.sln`
- Run ID: `f9d3dbe6-09c6-44dd-89c9-69b62bdc1b03`
- Audit ID: `17638f2e-192a-4979-b0a5-f919c870de2a`
- Population: 42/49 loaded project/framework instances analyzed; 715 types and 3,823 members.
- Artifacts: `E:/repos/SimplCommerce/.scorecard/dotnet/runs/f9d3dbe6-09c6-44dd-89c9-69b62bdc1b03/`

### OrchardCore

- Commit: `4c101f5c6a6e6aca073a800797a223a958b28c0b`
- Entry point: `OrchardCore.slnx`
- Run ID: `bca2d7ce-4acb-44f0-9f23-0020219ebbba`
- Audit ID: `1ce987f1-22bb-49db-b5bd-a2fbf04dc84e`
- Population: 213/239 loaded project/framework instances analyzed; 5,576 types and 24,989 members.
- Artifacts: `E:/repos/OrchardCore/.scorecard/dotnet/runs/bca2d7ce-4acb-44f0-9f23-0020219ebbba/`

## Replay and verification

Restore each pinned entry point using its normal repository procedure. Newtonsoft.Json preflight needed build-only `EnableSourceControlManagerQueries=false` and `EnableSourceLink=false` overrides for its older SourceLink integration with the partial clone. No source changes, AssemblyVersion override, copied runner or analyzer working-directory workaround were needed for these scorecard runs.

Use the development skill worktree with its matching compatibility manifest:

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
node E:/repos/ai_tools-scorecard-integration/skills/code-scorecard/scripts/run-scorecard.mjs `
  --repo E:/repos/Polly --entry-point Polly.slnx `
  --cache E:/tools/caches/code-scorecard `
  --dotnet-package E:/repos/CodeMetrics.AI/TestResults/public-corpus-fixes/packages/CodeMetrics.AI.2.3.0.nupkg
```

Replace the repository and entry point using the provenance above. The helper defaults to 1,800 seconds; use `--timeout-seconds` for a justified longer run. Logs stream to stderr. Concurrent tool installation is serialized, and timed-out processes are terminated with their descendants. Local package overrides are content-hashed to isolate them from published packages.

Tested local NuGet package SHA-256: `1e630035ab4c2d3d91437a6db71dceebebb2ddcccfdb308a636c866fa48fea67`.

- Full analyzer suite: 509 passing tests, followed by 16 passing focused corpus regressions after adding the graph-scope case (510 distinct tests covered).
- Packaged skill integration and runtime checks: 16 passing tests; behavioral-evaluation harness: 3 passing tests.
- Formatting and whitespace checks pass.
- The 12 pinned calibration fixtures retain identical populations, scores, finding counts and accuracy labels. Only the .NET baseline tool version and ruleset changed; the JS/TS baseline is unchanged.

## Remaining limits

Polly still has synchronous-boundary and exception-handling findings. Newtonsoft.Json still has catch/default-result findings. These require call-site and behavioral review; this release does not assert that each finding is a defect or suppress them by repository name. Test-project name matching remains a coverage heuristic. Production metrics still count target-framework instances, while test-method signals use the documented union of source sites. Dependency observations include enabled test/sample projects and live feed results; package rows are not unique package counts. Four repositories do not establish broad calibration or cross-ecosystem comparability.

## Follow-up: logical partial-type aggregation

A source review of Newtonsoft.Json exposed a population defect: `JContainer.Async.cs` was scored as a two-member type independently of the other declaration. The collector now combines included declarations by Roslyn symbol within each project/framework compilation, calculates member aggregates once, and unions coupling references. Partial method/property signatures and implementations count once. Generated declarations remain excluded. CSV membership uses the logical identity so partials and same-named nested/generic types do not multiply member rows. Component hotspot samples include the logical identity and source files. Raw physical line counts still include declaration/formatting overhead.

These fresh runs compare with the subsequent component-reporting snapshot, at the same four source commits listed above. Both snapshots use development version 2.3.0 and ruleset `dotnet-2026-09-11`; package hashes distinguish the implementations. Thresholds, score formulas and overall weights are unchanged. Every run completed and passed validation with matching run/audit IDs. Target source remained unchanged; target test suites were not executed and no coverage was supplied.

| Repository | Method complexity before / after | Decomposition before / after | Combined before / after |
|---|---:|---:|---:|
| Polly | 9.3 / 9.3 | 9.3 / 10 | 9.3 / 9.6 |
| Newtonsoft.Json | 0 / 0 | 4.7 / 5.3 | 2.4 / 2.6 |
| SimplCommerce | 8.7 / 8.7 | 9.3 / 9.3 | 9 / 9 |
| OrchardCore | 6.7 / 6.7 | 6.7 / 6.7 | 6.7 / 6.7 |

All scores remain partial static assessments. Newtonsoft.Json's net8.0 `JContainer` now has one type row, 101 members, aggregate CC 263 and decomposition ratio 2.604 (the async fragment previously scored 17). `JValue` has 64 members and ratio 4.4375; `Operation` retains CC 69 and appears once in CSV for that framework.

Other changed dimensions: Newtonsoft.Json maintainability rises 7.3 to 8.7 after combining member populations. Polly architecture falls 9.3 to 5.6: its combined non-generic `Policy` has 43 structural dependencies, which limits the coupling component. Inspected partial files expose public factory APIs for policy construction; this measurement requires that context before making a responsibility judgment. No factory-specific scoring adjustment was made. All other dimension scores are unchanged. These are measurement corrections, not source improvements.

| Repository | Run ID | Audit ID |
|---|---|---|
| Polly | `ed8a28ed-e7dd-4b0b-bcd5-c6b41c2fd267` | `8cc05791-3662-4153-9503-c00d74c59ae9` |
| Newtonsoft.Json | `402c8b82-1f1f-40a3-ba2e-6639e082b255` | `1176e4d4-c957-4d52-9583-3ea5b4268862` |
| SimplCommerce | `117c4170-8cf2-4fac-9e3c-012faf802d2a` | `b012ccfe-5092-4bd8-a808-9fc9a69b7886` |
| OrchardCore | `9b559530-3aec-4a3c-8269-7d10d0c76af9` | `b8642cdb-508f-4a8b-8b71-355d021fd0ce` |

Package SHA-256: `5cbb328235a59266f1b1221a61c1a94e8f2265c0837e87c76388a5376d9533c0`. Local comparison, invocation results, logs, package, prior run IDs and full dimension deltas are under `E:/repos/CodeMetrics.AI/TestResults/partial-types/`; each repository retains its run-specific evidence/inspection/CSV.

Verification: 520 analyzer tests pass, including seven new partial-type regression cases. All six .NET calibration fixtures retain their scores and findings. Formatting and whitespace checks pass. This correction remains local on the development branch; it has not been published.
