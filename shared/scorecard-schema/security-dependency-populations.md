# Security and dependency counting policy

The unpublished ruleset `dotnet-2026-09-14-source-package-populations` changes classification and counting, retaining all numerical score ladders. It builds on the evidence-fidelity corrections. A score change under this ruleset is not evidence of improved target code, and earlier rulesets cannot be used as compatible baseline gates.

## Security source findings

Policy `dotnet/security/source-sites-scoped-dependencies-v3` groups findings by normalized physical file, exact syntax span and category across project/framework observations. One site uses the maximum observed severity. Different files, same-line expressions, categories and conditional declarations remain distinct; observations without complete identities remain separate. Group evidence records `countingUnit`, `observationCount`, `affectedProjects` and `projectFrameworkObservations`. This uses the same source grouping implementation as Async/Blocking Usage without changing its behavior.

The CMAI4001 secret heuristic excludes a private field constant or local constant only when at least one use exists and every symbol-resolved use in the compilation is a pattern argument of the BCL `System.Text.RegularExpressions.Regex` API or `GeneratedRegexAttribute`. References across partial source files are checked. Public constants, mutable values, mixed credential/pattern use, regex input/replacement arguments, unresolved consumers and unrelated lookalike API types remain candidates. This is bounded semantic recognition, not regex execution or a general secret detector. It does not establish runtime security.

## Dependency identities and retained observations

Policy `dotnet/dependencyManagement/package-versions-v2` counts distinct package IDs and resolved versions for each vulnerability, deprecation or included-outdated input. Package ID/version casing is ignored. Repeating a version across projects or target frameworks cannot cross a count threshold; different resolved versions still count separately. Multiple advisories remain attached to their package rows without multiplying the version count. Missing version identity, including legacy text reports, remains unmerged.

All original package findings remain as project/TFM rows, preserving versions, advisories, deprecation reasons, alternatives and compatibility decisions. Each row records its counting unit and package observation count. Scoring inputs are package/version counts, not `findings.length`. Outdated candidates first receive their existing compatibility/Aspire disposition; a version with an included occurrence counts once even if another occurrence is incompatible. An unknown compatibility observation still blocks assessment. Compatibility/exclusion diagnostics retain observation counts. Unsupported framework strings count once across repeated projects. No restore, command or metadata failures are hidden by grouping.

## Project scope

Dependency Management assesses all enabled projects, including tests and benchmarks. Development dependencies affect build, test and maintenance work, so their advisories retain the existing dependency score effect.

Security imports distinct vulnerable package/versions from `production` and `unknown` project scopes. It excludes observations confined to `test` or `benchmark` projects. A package appearing in both production and development is still a Security input. Direct and transitive observations of the same vulnerable version contribute once to the Security import count.

Scope comes from the loaded project selection, including semantic test classification. Any analyzed production variant wins for a physical project. A benchmark-directory exclusion takes precedence over a test-like name for an excluded benchmark. Hosts, samples, missing paths and ambiguous/unrecognized classifications remain `unknown`; their advisories are conservatively retained in Security. This describes project use, not proof of what a published package contains or whether a vulnerable dependency is exploitable.

The dependency findings expose `dependencyScope`, `dependencyScopeBasis` and, for vulnerabilities, `securityScoreDisposition`. The separate `dependencyPopulation` and Security `dependencyVulnerabilityScope` summaries report included versions and excluded development observations. `scoreDisposition` and `findingEffects` in Dependency Management continue to describe that dimension's own scoring; an exclusion from Security does not exclude a package from Dependency Management. Failed vulnerability queries still withhold Security scoring.

## Interpretation

Reports should distinguish distinct package/version counts from report rows and show development-only advisories as maintenance concerns. Never describe a test/benchmark advisory as a vulnerability in the shipped library without additional evidence. Likewise, a clean static Security result establishes only that this scoped assessment found no scored signals. It is not a security certification.

Regression cases cover semantic regex positive/negative examples, shared source sites and missing identities, package versions across projects/TFMs, compatibility conflicts, and production/development/unknown scope. The four pinned second-wave repositories provide before/after evidence; no target code or numerical thresholds are adjusted to improve their scores.
