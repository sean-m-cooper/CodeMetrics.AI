# Published CodeTestament defaults — 2026-10-30

## Package publication and Windows registry checks

- Producer commit: `2205e9c562ef45cf893f3b455fec3c98588536f0`.
- NuGet 2.4.0: [successful workflow](https://github.com/sean-m-cooper/CodeTestament/actions/runs/41083314676).
- npm 0.6.0-preview.3: [successful workflow](https://github.com/sean-m-cooper/CodeTestament/actions/runs/41083315477).
- Windows: `published-packages.test.mjs` and `published-python.test.mjs`: 2/2 passed.
- Empty-cache default audit: .NET, JS/TS and Python complete; exact CodeTestament
  producer names/versions; one shared audit ID and distinct fresh run IDs;
  registry provenance, no local archive hashes, and available .NET rule catalog.
- Mixed audit ID: `8fb275a8-4ac4-4843-88db-0f60f28d989b`.
- Dedicated Python fixture: Method Complexity 10, Async/Blocking Usage 8;
  1 affected/25 eligible owners, 1 unresolved owner; exactly two dimensions
  scored, Error Handling skipped. Documented intent and offloading preserved.
- Plugin 0.2.0 isolated offline tests: 21/21 passed; skill frontmatter valid.
- Local receipts/logs retained under `TestResults/codetestament-release/`
  (generated audit artifacts are intentionally not committed).

## Evidence boundaries

These are execution/packaging tests, not a new corpus calibration or an
independent agent report evaluation. The producer release workflows tested
analyzers before upload; this default switch changes selection and instructions,
not measurement/scoring policy. Supported legacy exact pins remain explicit.

npm accepted `--tag preview` but also assigned `latest` to this first preview.
The channel-reconciliation workflow was denied tag removal by the configured
npm token (HTTP 403). Exact-version installs pass; channel correction is tracked
separately and must not be described as verified until the registry confirms it.

Windows packaged integration: 37/37 passed. Cross-brand and legacy-pin integration:
7/7 passed, including rejection before inspection for an altered producer name.

