# Adapter proof: 2026-10-10

The adapter refactor preserves JS/TS behavior on all six pinned selected-source samples: Express, Zod v4, TanStack Query core, TanStack React Query, Excalidraw, and Uptime Kuma server.

Every dimension's full evidence, the measured population, and configuration fingerprints matched the preceding npm/pnpm development results under `TestResults/js-ts-pnpm/corpus`. Raw CSV matched the pinned source-scope corpus. The corpus runner also checked every non-dependency dimension against the released 0.5.0 baseline. These are selected-source comparisons, not claims about each entire repository.

Revisions, source/runtime hashes, checks, and artifact paths are recorded in [the verification record](verification-2026-10-10.json). External corpus projects were not installed or executed.

## Verification

- 256 JS/TS tests passed.
- 10 Python tests and 5 shared conformance tests passed.
- All 6 frozen JS/TS calibration fixtures passed unchanged.
- All 11 architecture context fixtures and offline installed-package precedence checks passed.
- Installed-package smoke passed, including exact packaged shared-runtime/declaration content, scoring, evidence validation, baseline gates, SARIF, and npm/pnpm input protection.
- The Python producer parsed its own 17 functions successfully and exercised the shared preview engine. This proves integration, not calibration.
- `git diff --check` passed.

The whole-repository self-analysis test initially exceeded its default five-second timeout (approximately 5.5 seconds). Its integration timeout is now 15 seconds; assertions are unchanged. The complete successful JS/TS run took 84 seconds with two workers.

## Remaining boundary

Python remains a Python 3.12, complexity-only experimental producer. It includes test/stub bodies, does not produce scorecard evidence v3, and is not wired into the scorecard skill. Its optional numeric preview uses an explicitly experimental shared ladder. No Python/JS/.NET score comparability is established. Coverage, CRAP, duplication, and other Python dimensions are not implemented in this pass.
