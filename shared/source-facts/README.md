# Language adapter contract (experimental v1)

A language adapter measures source and emits **facts**. The shared engine consumes those facts; the ecosystem chooses which scoring policies are supported and calibrated. This is an internal protocol, separate from scorecard evidence schema v3. It does not confer nine-dimension support on a new language.

The first production consumer is the existing JS/TS function scoring path. Its selected population, TypeScript parser, measurement semantics, policies, evidence, and rounding are preserved. Python is the second producer and an experimental complexity-only proof. Roslyn is unchanged.

## Boundary

`contract.d.mts` defines the serializable shape; `contract.mjs` is the authoritative runtime validator. `schemaVersion: 1` rejects unknown fields and versions. Each document supplies:

- Language, adapter version, parser/version, and measurement policy provenance.
- Complete/incomplete status, scope exclusions, and failure diagnostics.
- Explicit capabilities. V1 exchanges function complexity only; coverage and MI are unsupported **by this contract**, regardless of what a larger analyzer supports.
- Function ID, file, member, one-based line, exclusive body ownership, and integer CC with baseline 1. Decision counts are language-specific and must sum to CC minus one, or be null when breakdowns are unavailable.

IDs are unique within a selected population and deterministic for the same source selection. They are not run IDs or durable identities across edits. Adapter facts cannot replace run/audit identities in scorecard evidence. V1 omits columns and offsets to avoid confusing Python UTF-8 offsets with TypeScript UTF-16 offsets.

Complete means the declared selection was parsed, not that every aspect of the ecosystem is measured. Incomplete documents require diagnostics and cannot produce a population score. Empty or unsupported populations also return null. Missing coverage never becomes zero coverage or a perfect score.

## Shared algorithms and policy

`population.mjs` implements interpolation, one-worst or selected-weakest population weighting, contribution records, and exact rational score arithmetic using `arithmetic.mjs`. Worst-function complexity uses the existing anchors CC 3→10, 5→8, 10→6, 20→4, 40→0; one worst function gets 40%, the remainder 60%. A single function has full weight. Rounding is decimal half-up to one decimal place.

JS/TS complexity now adapts its existing measurements into this contract. Its MI aggregation reuses the same generic engine with the existing MI ladder and weakest quintile; MI is **not** transported as a v1 source fact. The JS/TS package copies the canonical runtime and declarations into `dist` at build time using package-internal imports so installed packages do not depend on this repository. Edit the files here, never the generated `dist/shared-source-facts` copy. Run `npm run build` before direct TypeScript/Vitest development commands.

The preview command explicitly applies the shared complexity ladder as an **experimental policy**, not a calibrated Python scorecard. Identical numeric inputs produce identical policy results; that does not establish that CC measurements in different languages are equivalent. Keep language-specific decision policies and calibration visible.

## Prove a new adapter

1. Select a native parser or suitable syntax parser. Do not execute target code.
2. Document ownership, scope, decisions, unsupported grammar and measurements. Emit incomplete status on discovery/parse failure.
3. Produce v1 facts, validate them, and test nested ownership, identities, failures, missing and empty populations.
4. Add a real producer fixture to the conformance suite; use the shared runtime rather than copying its formula into each language.
5. Calibrate the language on representative projects before exposing production scores or wiring it into the scorecard skill.

```powershell
npm run build --prefix analyzers/javascript-typescript
py -3.12 -m unittest discover -s analyzers/python/tests -v
node --test shared/source-facts/conformance.test.mjs
py -3.12 analyzers/python/source_facts.py shared/source-facts/fixtures/owned-functions.py --output TestResults/python-facts.json
node shared/source-facts/preview.mjs TestResults/python-facts.json
```

Use `python3` with Python 3.12 on Linux/macOS. Output parents must exist and `--output` must name a new `.json` file. The preview reads one document and writes JSON to stdout. Invalid/incomplete input exits 2; a valid empty or unsupported document exits 0 with `population: null` and `status: unavailable`.

Conformance runs actual TypeScript and Python producers, checks exclusive ownership and common numeric behavior, and rejects malformed contracts. Existing JS/TS tests, package smoke, pinned calibration fixtures, and selected-source corpus comparisons guard behavior preservation. CRAP, coverage mapping, duplication, architecture, and other dimensions remain separate future capabilities.

The initial [verification record](verification-2026-10-10.md) documents the behavior-preserving corpus comparison and current limitations.
