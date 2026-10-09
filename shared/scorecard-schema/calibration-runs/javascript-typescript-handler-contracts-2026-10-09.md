# JS/TS handler contract calibration, 2026-10-09

The 0.5.0 release candidate recognizes **220 of 426 distinct handler bodies
(51.6%)**, up from 87 (20.4%) in the unpublished v1 classifier. Express now has a
fully assessed population and scores 10. Other selections remain unavailable
where recognition is incomplete or no handlers are present. These results measure
classifier coverage within selected source, not the libraries' runtime quality.

The [policy](../javascript-typescript-error-handling-policy.md) defines the bounded
contracts and scoring formula. The [machine-readable report](javascript-typescript-handler-contracts-2026-10-09.json)
records source revisions, analyzer source hash, fresh run/audit IDs, fingerprints,
evidence hashes, reviewed cases, and the packaged Express consumer check.

| Selected source | Distinct bodies | Assessed | Unknown | Unresolved callback uses | Error Handling score |
| --- | ---: | ---: | ---: | ---: | --- |
| Express | 2 | 2 | 0 | 0 | 10 |
| Zod v4 | 22 | 10 | 12 | 0 | Unavailable |
| TanStack Query Core | 16 | 2 | 14 | 0 | Unavailable |
| TanStack React Query | 0 | 0 | 0 | 0 | Unavailable: no population |
| Excalidraw app | 45 | 33 | 12 | 1 | Unavailable |
| Uptime Kuma server | 341 | 173 | 168 | 0 | Unavailable |
| Total | 426 | 220 | 206 | 1 | Not aggregated |

The callback gap is a use site, not another resolved body. Unknowns are neither
deductions nor credited handling. Any unknown body or unresolved callback use
withholds the dimension score; an absent population is also unavailable.

## What changed

Express's two outcomes are caller-supplied error propagation and a private
optional-result helper whose observed callers guard the result. CommonJS exports
are accounted for: an escaping helper cannot borrow a private-only contract.

Uptime Kuma gains recognition for source-backed reporting wrappers and forwarding
caught errors, including structured payloads, to caller-supplied callbacks. A
documented logger can filter by runtime level, but still requires a local
payload-bearing implementation path to a standard console sink. This is evidence
of a reporting contract, not guaranteed delivery. Zod and Excalidraw also gain
recognition for explicit result containers. Names alone confer no exemption.

All 18 prior reviewed cases match their pinned source and supporting-range hashes.
All 11 fallback/declared-intent counterexamples avoid deductions; recognizing
Express's optional-result flow does not imply recognizing every remaining case.
The two previously identified terminal silent discards remain observations within
unscored populations, not confirmed defects or judgments about business intent.

## Validation and reproduction

The corpus runner verifies clean tracked source and exact revisions, reuses the
released 0.4.0 source selections, checks byte-identical CSV, and checks equality of
every dimension other than Error Handling. It does not install sample dependencies,
build sample applications, or execute their code.

New regression controls exercise callback mutation, reassigned catch bindings,
local no-op callbacks, arbitrary returns, reporting lookalikes, conditional and
unreachable output, unguarded result reads, mutation, and direct/aliased/shorthand
exports. Existing tests retain shared-body counting, Promise continuation,
documented-intent, unknown-population, rounding, and policy compatibility checks.

The updated skill installed the local 0.5.0 tarball into a fresh E-drive cache and
ran Express using the same source selection. Analysis and inspection exited zero;
inspection was usable, run/audit IDs matched, and Error Handling scored 10 from
two assessed bodies. This packaged consumer check is distinct from the six
source-level corpus runs and from post-publication registry verification.

From `analyzers/javascript-typescript`, after recreating the released corpus with
`released-context-corpus.mjs` if needed:

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
npm run build
node scripts/handler-disposition-corpus.mjs E:/repos E:/repos/CodeMetrics.AI/TestResults/js-ts-context-calibration/corpus E:/repos/CodeMetrics.AI/TestResults/js-ts-0.5-release/corpus
npm test
npm run test:package
npm run test:architecture
```

Ruleset `javascript-typescript-2026-10-09-handler-contracts` and policy
`failure-disposition-v2` require fresh score baselines. Architecture and
decomposition remain unscored; no cross-ecosystem comparability is claimed.
