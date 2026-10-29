# CodeTestament rename and migration plan

Date: 2026-10-29
Status: brand selected by Sean; local producer migration started on
`codex/codetestament-migration`. See the [implementation guide](codetestament-migration.md).

## Outcome

Adopt **CodeTestament** as the product family and **CodeTestament Scorecard** as
the assessment product. Suggested tagline: **Code quality. Backed by evidence.**
Publish the renamed analyzers and update the existing `code-scorecard` skill so
new users receive CodeTestament while existing users retain working installations,
explicit version pins, annotations, and historical results.

The first release changes branding and distribution identities, not scoring
formulas, thresholds, populations, or supported language scope. Python stays a
preview; Error Handling remains unscored. New analyzers, dashboards, cryptographic
attestations, and a unified cross-language launcher are separate work.

The plan does not itself execute a repository rename, purchase a domain, publish
a package, or alter a user's project manifest.
The [name review](codetestament-name-review.md) records the completed US/public
screen and the outstanding international and legal-clearance limits. Selecting
the brand does not turn that research into legal clearance.

## Proposed public names

| Surface | Current | Target |
| --- | --- | --- |
| Product family | CodeMetrics.AI | CodeTestament |
| Product display name | CodeMetrics.AI scorecard | CodeTestament Scorecard |
| GitHub repository | `sean-m-cooper/CodeMetrics.AI` | `sean-m-cooper/CodeTestament` |
| NuGet global/local tool | `CodeMetrics.AI` | `CodeTestament.Scorecard` |
| .NET command | `code-metrics` | `codetestament-dotnet` |
| npm package | `codemetrics-ai` | `codetestament` |
| JS/TS command | `codemetrics-ai` | `codetestament` |
| Evidence command | `codemetrics-evidence` | `codetestament-evidence` |
| Python command | `codemetrics-python` | `codetestament-python` |
| Python coverage collector | `codemetrics-python-collect` | `codetestament-python-collect` |
| Agent skill | `code-scorecard` | Keep `code-scorecard` |
| Output directory | `.scorecard` | Keep `.scorecard` |

The npm default command continues to analyze JS/TS; it is not a new language
dispatcher. .NET uses a distinct executable so both packages can coexist in a
global PATH. New packages do not claim the old command names: legacy packages
already provide them, and duplicate command shims would complicate coexistence.
No separate PyPI distribution is introduced.

Domain preference: `codetestament.dev`, with `.com` as an optional redirect if
acquired. Registrar availability, ownership, pricing, and publisher permissions
must be checked at execution time. Neither domain is required to run the CLI.

## Verified starting point

Local manifests and integration code were inspected on the plan date:

- .NET: `analyzers/dotnet/src/CodeMetrics.AI/CodeMetrics.AI.csproj`, version
  `2.3.1`, .NET 10, NuGet package `CodeMetrics.AI`, command `code-metrics`.
- npm: `analyzers/javascript-typescript/package.json`, version
  `0.6.0-preview.2`, Node 20+, four current commands listed above. Python 3.12 is
  required for the bundled Python adapter.
- Python adapter/evidence version: `0.15.0` in the skill compatibility manifest;
  this is different from the npm distribution version.
- Companion checkout: `E:/repos/ai_tools-js-0.4-release`, branch
  `codex/scorecard-js-dependencies`; skill at `skills/code-scorecard`.
  Confirm the intended release checkout/branch before implementing there.
- Skill `compatibility.json` pins repository revision
  `16d311832fa4d1b934129a0cdb2b66d728443ecd`, .NET `2.3.1`, and the npm/evidence
  distribution `0.6.0-preview.2`. Its Python pin is `0.15.0`.
- Skill `scripts/runtime.mjs` hard-codes package names, executable paths and
  package directories. Cache directories currently key primarily on version and
  optional local-package identity. `scripts/run-scorecard.mjs` recognizes only
  the legacy package names in repository pins.
- Shared `src/evidence-tools.ts` rejects different `tool.name` and `tool.version`
  when comparing baselines. Python validators additionally check exact producer
  names and supported version/profile combinations.
- Release workflows use `v*` tags for NuGet and `js-v*` tags for npm. npm
  prerelease tags publish to `preview`; stable tags publish to `latest`.

These are local observations, not proof that registry state still matches them
when migration starts. Recheck published versions, tags and ownership first.

## Compatibility decisions

### Preserve durable contracts

Keep schema v3, the nine dimension keys, ecosystem IDs, scope IDs, rulesets,
calibration policies, fingerprints, run IDs, audit IDs, source provenance,
configuration semantics, and output layout. Preserve historical v2/v3 fixtures
and calibration artifacts as records of the tool that actually produced them.

Keep **CMAI diagnostic codes** and existing suppression/intent syntax, including
`amp-metrics`, operational. Rebranding does not require developers to edit their
source comments. Catalog descriptions and help text can adopt the new brand;
stable rule references and their meaning must not change.

Keep schema `$id` values and nested Python/source-facts producer identifiers as
protocol identifiers in this release, including legacy `codemetrics-ai/...`
strings. Their validators and provenance checks must not become permissive.
Keep .NET source namespaces, solution/project filenames, assembly resource names,
and existing internal paths initially. A later internal cleanup can be isolated
from the distribution migration. Document these intentional legacy references.

### New evidence identifies its actual tool

Use an explicit identity table, covered by fixtures, rather than blanket string
substitution or prefix matching:

| Ecosystem | New top-level `tool.name` |
| --- | --- |
| .NET | `CodeTestament.Scorecard` |
| JS/TS | `CodeTestament.JavaScript` |
| Python | `CodeTestament.Python` |

The new reader must still read supported old artifacts under their original
names. Validate allowed name/version/profile combinations; do not accept any name
that resembles CodeMetrics or CodeTestament. Python's adapter version remains
separate from npm package version. A distribution rename alone does not invent a
new Python scoring profile or change its nested producer contracts.

Update rule-catalog tool identity and advertised retrieval command together with
the .NET emitter. Verify CLI version, package version, catalog version and evidence
version using the existing rules. SARIF driver branding changes for new runs;
preserve rule IDs and fingerprints, and verify whether the upload integration
treats the new driver as a separate analysis stream before switching CI defaults.

### Baseline transition is explicit

Keep strict comparison behavior: changing a tool name or version remains
incompatible by default. Do not rewrite baseline JSON, alter historical evidence,
automatically alias different versions, or use `--allow-incompatible` to pass gates.

Provide a documented, reproducible migration procedure:

1. Retain the old package pins and baseline for rollback.
2. On the same source revision and configuration, run the old package and the new
   package into separate fresh run directories with distinct run IDs.
3. Compare substantive metrics, findings, scope, populations and scores in the
   migration validation harness. Allow only documented identity/version/timestamp,
   invocation ID and display-text differences. Record the package pair and source
   revision used; unavailable external probes must remain explicit.
4. After review, generate and adopt a new baseline using the new package. Subsequent
   normal gates compare new-brand evidence to that new-brand baseline.

An exploratory cross-brand report may use the existing incompatible mode, which
must continue to suppress score deltas and reject gating. A rename is never
reported as a code-quality improvement. Reading old evidence and comparing it
to new evidence are distinct capabilities.

## Delivery sequence

### 1. Establish the migration branch and release inventory

- Work on `codex/codetestament-migration` from the agreed current source revision.
  Preserve unrelated work and local `.scorecard` output; never commit run artifacts
  merely because they are present in the workspace.
- Inventory publisher ownership/permissions, package namespaces, release tags,
  secrets' package/repository restrictions, and any OIDC/trusted-publisher
  bindings. Verify new identities can be published before changing defaults.
- Record exact legacy artifacts and checksums used for paired validation.
- Search maintained docs, manifests, CI, helper scripts, package tests, catalogs,
  SARIF, and feedback templates. Classify each old-name occurrence as public
  branding, executable identity, protocol identifier, or historical record.
- Verify both repositories' applicable instructions and release branches. Use the
  skill-authoring workflow when actually editing the skill.

Exit: agreed mapping, reproducible old artifacts, no unexplained namespace or
publisher blocker. Domain acquisition is a separate explicit action.

### 2. Implement and package the producer rename locally

- Change NuGet package metadata and command in the existing csproj; keep its
  internal path stable. Update README/help/version output, catalog documentation,
  repository links and packed metadata.
- Change npm package name, bin entries, README and package-lock root metadata.
  Preserve the exported JavaScript API; document the new module import path.
- Update top-level evidence emitters and supported identity checks, while retaining
  historical readers and nested producer contracts as described above.
- Update packaged Python documentation, coverage-collector help and errors, shared
  evidence command examples, schema packaging, and installation smoke tests.
- Fix maintained documentation that mixes stable and preview versions; label
  language maturity separately from the distribution channel.
- Build real `.nupkg` and `.tgz` artifacts. Test them through isolated installs,
  not only through source-tree entry points.

Exit: new packages run locally; old/new installations coexist; all historical
readers and contract tests pass. No scoring-policy changes appear in the diff.

### 3. Prepare the skill migration against local artifacts

Update `skills/code-scorecard/compatibility.json`, `scripts/runtime.mjs`,
`scripts/run-scorecard.mjs`, `scripts/rule-catalog.mjs`, `SKILL.md`, `bootstrap.md`,
language guides, integration workflow, script tests and relevant evaluations.

- Represent supported installations as exact **package + distribution version +
  command + emitted tool identity/version** records. Preserve Python's separate
  adapter version. A flat shared list of version strings is insufficient.
- Recognize exact pins for both old and new package names. A legacy pin continues
  using its tested legacy package; do not silently substitute the new package.
- If a repository declares both names for the same ecosystem, stop with an
  actionable ambiguity message or require an explicit selection. Never choose
  based on manifest entry order. Unsupported versions/ranges still fail clearly.
- Include package identity and version in cache keys; include archive content
  identity for local overrides. Validate installed name as well as version.
  Old caches remain intact and must not satisfy a new-package request.
- Update catalog command resolution, evidence CLI paths, expected identities,
  source repository revision, and fresh-run summaries together.
- Keep the skill entry point `code-scorecard`. Explain CodeTestament branding and
  retained CMAI annotations without duplicating the analyzer's rule catalog.
- Test exact local package overrides before enabling any unpublished default.

Exit: isolated-cache legacy pins, new pins, default selection, historical imports,
Python preview, mixed-language audits and failures all behave as documented.
The public skill defaults stay unchanged until published packages are verified.

### 4. Publish producers, then switch consumers

Recommended version approach: retain ecosystem version lineage rather than reset
everything to 1.0. Tentative next versions, subject to registry/tag checks:

| Distribution | Proposed release | Source boundary |
| --- | --- | --- |
| NuGet `CodeTestament.Scorecard` | `2.4.0` | Rename from tested .NET 2.3.1 behavior |
| npm `codetestament`, stable | `0.5.1` | Rebrand the published 0.5.0 stable source; no preview-only features |
| npm `codetestament`, preview | `0.6.0-preview.3` | Rebrand current 0.6.0-preview.2 lineage; Python remains opt-in |

The stable npm rebrand needs a separate source/tag boundary. Do not build it by
stripping the prerelease suffix from the current preview checkout. If producing
both channels is not ready, publish only the explicit new preview and keep legacy
stable users on their existing tested package until the stable rebrand is ready.

- Update NuGet workflow package installation/identity smoke checks and npm
  package-name checks. Keep the existing `v*` / `js-v*` tag families with explicit
  source-branch/channel handling; never reuse or move historical release tags.
- Publish new artifacts, then install the exact published versions in empty caches
  on Windows and Linux. Validate reports, catalogs, versions and preview labels.
- Explicitly verify npm `latest` and `preview`; publishing a preview must never
  accidentally make it the default stable installation.
- Only after producer verification, release the skill with new defaults and its
  tested source revision. Legacy exact pins continue to work.
- Publish migration notes with old/new command tables, package import changes,
  local/global installation procedures, baseline steps and rollback instructions.

Exit: public registry artifacts, clean-cache skill runs and channel resolution
are verified. A successful upload job alone is not completion.

### 5. Rename repository and announce migration

- Rename the existing GitHub repository to CodeTestament, retaining its history,
  issues, releases and tags. Check target-name availability first.
- Update remotes in active checkouts, badges, package URLs, integration workflows,
  source checkouts, docs and any publisher bindings that depend on repository name.
- Verify old repository links and Git operations redirect. Do not reuse the old
  repository name. GitHub Pages URLs and consumers of actions hosted in the renamed
  repository require separate checks; GitHub does not redirect those action calls.
- Keep the current `E:/repos/CodeMetrics.AI` folder during the active migration.
  Moving local roots/worktrees is a later housekeeping task, with no C-drive clones.
- Once replacements and the skill are proven, mark legacy packages as renamed and
  point to the replacements through registry deprecation metadata and release notes.
  Do not unpublish historical versions, remove exact pins, or silently auto-update
  installations. Avoid deprecating a stable channel whose replacement is only preview.

Keep legacy download/history access indefinitely. Support the legacy pin path for
at least the next two CodeTestament release cycles, and publish a retirement policy
before removing compatibility. This is not a promise to dual-publish every future
feature under both brands. No forwarding-package or auto-install wrapper is needed
for the initial migration.

## Verification required before release

| Area | Required evidence |
| --- | --- |
| Analyzer regression | Existing .NET/JS/TS/Python suites, formatting, source-facts conformance, calibration checks and architecture corpus pass |
| Package contents | Schemas, Python files, guides, rules catalog and exported APIs present in installed artifacts |
| Coexistence | Old/new .NET and npm packages work side by side; no executable shim is overwritten |
| Platform coverage | Windows/Linux; Node 20/22 where currently supported; Python 3.12; .NET 10 |
| Evidence compatibility | Supported historical v2/v3 imports work; unsupported/spoofed identities still fail; stale IDs and incomplete runs remain unusable |
| Scoring parity | Frozen fixtures and representative pinned corpus selections retain findings, populations and scores under the controlled rename comparison |
| Baselines | Cross-brand comparisons fail by default; exploratory output cannot gate; freshly generated new-brand baselines work |
| Skill | Exact legacy/new pins, ambiguous dual pins, local overrides, cache separation, fresh run/audit IDs and catalog lookup tested |
| Python | At most 2/9 dimensions scored; Error Handling unscored; unsupported async populations unavailable; coverage collection remains explicit |
| SARIF | Stable rule IDs/fingerprints, correct driver identity and inspected upload categorization |
| Publication | Exact published versions installed in clean caches; stable/preview tags and the released skill verified |

Use the pinned regression corpus and one installation-level smoke per supported
ecosystem first. Expand to full external-project reruns only if parity changes,
dependencies, path handling or other findings warrant it. This migration should
not require unrelated source refactors to improve scores.

## User-facing upgrade and rollback

Final release notes must use verified exact versions in place of the proposed
versions above. The flow is: install the new package alongside the old, run a fresh
assessment, review and create the new baseline, update CI commands/pins, then
optionally uninstall the old package. `dotnet tool update CodeMetrics.AI` does not
perform this cross-package migration; users install CodeTestament.Scorecard explicitly.

For npm users, document both global CLI migration and project dependency/import
changes, including regeneration of the appropriate lockfile using their package
manager. Preserve npm/pnpm support. The skill does not edit these files for users.

If validation or publication fails, leave the skill's public defaults and old
package channels unchanged. If a problem is discovered after the switch, restore
the exact last-known-good skill/package pins and old baseline, retaining new-run
artifacts for diagnosis. Stop promotion of the faulty new release and publish a
corrected version; do not move tags or mutate historical packages. Repository
branding can remain CodeTestament while a distribution is rolled back.

## Suggested implementation commits

1. `docs: adopt CodeTestament and record migration contract`
2. `feat: package analyzers as CodeTestament with historical evidence support`
3. Companion repo: `feat: support legacy and CodeTestament scorecard installations`
4. `test: verify rename parity and installed-package migration`
5. `chore: prepare CodeTestament release channels and migration documentation`
6. After published verification, companion repo: `chore: switch tested scorecard defaults to CodeTestament`

Track the stable npm backport as a distinct release commit. The exact commit split
can follow dependencies; each consumer-default change must follow its published
producer. This plan is documentation only; none of these implementation or
publication steps is marked complete.

## Sources / References

- Local files inspected: root README; .NET csproj and AGENTS.md; npm package.json;
  `.github/workflows/ci.yml`, `publish.yml`, `publish-npm.yml`;
  `docs/evidence-workflows.md`; `docs/releases/python-preview-0.6.0-preview.2.md`;
  `src/evidence-tools.ts`, `src/evidence-reader.ts`, Python evidence/producer
  identity checks under the npm analyzer; `shared/scorecard-schema/evidence.schema.v3.json`.
- Companion skill files inspected: `compatibility.json`, `bootstrap.md`,
  `scripts/runtime.mjs`, `scripts/run-scorecard.mjs`.
- [CodeTestament name review](codetestament-name-review.md)
- [GitHub repository rename behavior and exceptions](https://docs.github.com/en/repositories/creating-and-managing-repositories/renaming-a-repository)
- [npm package/version deprecation](https://docs.npmjs.com/deprecating-and-undeprecating-packages-or-package-versions/)
- [NuGet package deprecation and alternative-package metadata](https://learn.microsoft.com/en-us/nuget/nuget-org/deprecate-packages)
