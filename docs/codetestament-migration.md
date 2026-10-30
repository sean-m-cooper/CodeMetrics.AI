# Moving to CodeTestament

CodeMetrics.AI is becoming **CodeTestament**, with **CodeTestament Scorecard** as
the assessment product. NuGet `CodeTestament.Scorecard@2.4.0` and npm
`codetestament@0.6.0-preview.3` are published. GitHub is now
`sean-m-cooper/CodeTestament`, and plugin 0.2.0 selects these exact defaults.
Supported legacy repository pins remain honored.

The [local validation checkpoint](codetestament-migration-validation.md) records
executed tests, paired artifact hashes and remaining release checks.

## Published package identities

| Distribution | Version | Commands |
| --- | --- | --- |
| NuGet `CodeTestament.Scorecard` | `2.4.0` | `codetestament-dotnet` |
| npm `codetestament` | `0.6.0-preview.3` | `codetestament`, `codetestament-evidence`, `codetestament-python`, `codetestament-python-collect` |

The npm default command still analyzes JS/TS. Python remains preview, with at
most Method Complexity and Async/Blocking Usage scored (2/9 dimensions). Error
Handling stays unscored. The Python adapter version remains `0.15.0`; it is not
the npm distribution version. The explicit coverage collector executes tests;
ordinary Python analysis parses source without importing the target.

No stable npm build is being created from this preview source. Published
`codemetrics-ai@0.5.0` remains the existing stable release.

## Install published packages

```sh
dotnet tool install --global CodeTestament.Scorecard --version 2.4.0
npm exec --yes --package=codetestament@0.6.0-preview.3 -- codetestament --help
npm exec --yes --package=codetestament@0.6.0-preview.3 -- codetestament-python --help
```

The new commands coexist with legacy installations. The npm package is a preview;
use the exact version above. The skill installs both distributions into isolated
caches automatically. Existing `.scorecard` data and CMAI annotations stay valid.

## Build and install locally

From the repository root, in PowerShell:

```powershell
New-Item -ItemType Directory -Force TestResults/brand-migration/current | Out-Null
dotnet pack analyzers/dotnet/src/CodeMetrics.AI/CodeMetrics.AI.csproj -c Release -o TestResults/brand-migration/current
Push-Location analyzers/javascript-typescript
npm ci
npm pack --pack-destination ../../TestResults/brand-migration/current
Pop-Location

dotnet tool install CodeTestament.Scorecard --version 2.4.0 --tool-path ./TestResults/codetestament-tools --add-source ./TestResults/brand-migration/current
./TestResults/codetestament-tools/codetestament-dotnet --help

npm install --prefix ./TestResults/codetestament-npm --ignore-scripts --no-audit --no-fund ./TestResults/brand-migration/current/codetestament-0.6.0-preview.3.tgz
./TestResults/codetestament-npm/node_modules/.bin/codetestament --help
./TestResults/codetestament-npm/node_modules/.bin/codetestament-python --help
```

These installs are isolated from application manifests. The migration verifier
below copies each specified NuGet artifact into its own source directory, uses
a configuration with no public feeds, and isolates NuGet caches to ensure the
intended archives supply the installs. New packages own only their new command names, so
old and new packages can coexist. JS API imports change from `codemetrics-ai`
to `codetestament`; exports stay the same.

## Evidence and baseline compatibility

New top-level tool names are `CodeTestament.Scorecard`, `CodeTestament.JavaScript`
and `CodeTestament.Python`. The evidence CLI can require an exact name with
`--expected-name`, alongside version, ecosystem, source, run and audit checks.
Consumers should select the expected name and version from their tested package
record; schema validation alone does not establish which package was invoked.

Historical v2/v3 artifacts retain their recorded identities. Supported historical
Python profiles still validate under `CodeMetrics.AI.Python`; the new Python
identity is accepted only with adapter `0.15.0` and its current ruleset. Nested
Python producers remain exact protocol identifiers and cannot be rebranded in
saved JSON.

A tool-name or version change still makes a normal baseline comparison
incompatible. Do not edit historical evidence to make it pass. Preserve the old
package pin and baseline, run both packages on the same source/configuration in
fresh run directories, review parity, then generate and adopt a fresh CodeTestament
baseline. Roll back by restoring the old pin and its matching baseline.
`--allow-incompatible` remains exploratory, with null score deltas and no gates.

## Paired artifact verification

Build the legacy artifacts from commit `16d3118` and the new artifacts from this
branch. Retain the exact archives and use a fresh output directory on each run:

```powershell
node scripts/verify-brand-migration.mjs --legacy-npm TestResults/brand-migration/legacy/codemetrics-ai-0.6.0-preview.2.tgz --current-npm TestResults/brand-migration/current/codetestament-0.6.0-preview.3.tgz --legacy-nuget TestResults/brand-migration/legacy/CodeMetrics.AI.2.3.1.nupkg --current-nuget TestResults/brand-migration/current/CodeTestament.Scorecard.2.4.0.nupkg
```

The verifier installs both names together, checks command shims and package
identities, and compares .NET, JS/TS and Python evidence from the same fixtures.
Only tool name/version, timestamps, invocation IDs and .NET catalog retrieval
metadata may differ. Findings, fingerprints, policies, scores, scope, diagnostics,
populations and nested producer IDs must match. It also requires cross-brand
gates to reject comparison and compares the complete .NET catalog definitions.
The receipt records artifact SHA-256 hashes and retains both evidence files.
External dependency probes are disabled; this is fixture parity, not a new
calibration result or public-install verification.

## Intentional legacy identifiers

These remain unchanged in this migration:

- CMAI rule codes, rule IDs, `amp-metrics` directives and their semantics.
- Schema IDs, rulesets, dimension keys, scope IDs and SARIF fingerprint keys.
- `.scorecard` paths and the `code-scorecard` skill name.
- Nested `codemetrics-ai/...` source-facts and Python producer identifiers.
- Internal .NET namespaces, assembly/resource names, project and solution paths.
- Historical fixtures, release notes, calibration evidence and published tags.
- Historical URLs in recorded validation evidence; active repository links now use CodeTestament.

## Remaining migration work

The canonical skill lives in `skills/code-scorecard`; `ai_tools` carries a pinned
marketplace reference. Published producers now supply the default installations.
See [skill ownership and validation](scorecard-skill.md) for the Windows/Linux
integration and registry checks. Preserve old baselines and verify SARIF stream
behavior before changing a project's CI configuration. Legacy registry deprecation
and a stable CodeTestament npm release remain separate follow-up decisions.
See the [full migration plan](codetestament-migration-plan.md) for historical sequencing.
