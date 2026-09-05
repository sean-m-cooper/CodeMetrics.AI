# Evidence v3

The .NET 2.0.0 and JS/TS 0.2.0 implementations emit schema v3. The v2 schema and examples remain available for historical evidence. The CSV header is unchanged. Strict v2 consumers must select `evidence.schema.v3.json` and accept `schemaVersion: 3` before upgrading the analyzer.

V3 adds a required `analysis` block with completion status, bounded diagnostics, a ruleset identifier, calibration status, configuration fingerprint, and suppression declarations. Absolute checkout paths are not part of the comparison identity. Tool versions, rulesets, configuration fingerprints, subject names/variants, and dimension availability must match for comparable runs. A different source revision is expected and is not an incompatibility.

Findings include stable `ruleId`, SHA-256 `fingerprint`, explicit confidence, structured `observations`, and optional member locations. Finding paths and `subject.root` use the nearest Git repository root (including worktrees), falling back to the analysis directory outside Git. This preserves monorepo locations for SARIF uploads. Whitespace and line movement do not change identities. Renaming a file/member or changing the offending statement may produce a resolved/new pair. Repeated identical findings use occurrence ordinals.

Scored dimensions expose a `scoring` explanation: final score, aggregate loss, and the policy's observations or basis. Existing .NET ladders are aggregate policies; assigning a fictional deduction to every finding would double-count shared signals. Architecture hotspots explicitly expose measurements and thresholds, with detailed coupling exclusions retained in `couplingProvenance`.

Suppression entries currently record declarations, their location, categories, and optional reason. `status: declared` does not assert that the directive suppressed a finding. Existing reasonless directives retain their behavior and appear without a reason. Consumers should expose that distinction during review.

Failed and skipped dimensions must omit `score`. Incomplete .NET source analysis preserves partial findings but removes source-derived scores. Dependency command failures remain dimension failures. Consumers must never interpret an unavailable score as zero or ten.

`calibration: baseline` identifies .NET as the reference implementation; it does not assert that every rule has been empirically validated. JS/TS remains `uncalibrated`. The checked-in fixture distributions are regression baselines, not evidence that ecosystem scores can be averaged or ranked against each other.

New outputs also include structured dimension `scope`: stable `id`, `coverage` (`partial`/`unsupported`), `includes`, and `excludes`. This is optional in the v3 schema so existing v3 files remain readable. A missing scope is unknown. Comparison requires matching scopes; consumers must label scoped scores and keep broader qualitative commentary separate.

The packaged evidence CLI supports v2 compatibility reads through `--inspect-output`. It preserves the historical document and reports unknown completeness/scope rather than synthesizing v3 metadata. Only v3 supports comparisons, quality gates and SARIF. Consumers can use `--expected-ecosystem`, `--expected-version`, `--expected-entry-point`, `--expected-root`, and `--expected-variant` to validate a requested run's provenance.

Current producers emit `analysis.runId` and `analysis.auditId` UUIDs. They remain optional in the schema for historical v3 reads. Fresh consumers must supply independently known `--expected-run-id`/`--expected-audit-id` values to the evidence CLI; missing or mismatched fields are rejected. These IDs appear in comparison/SARIF envelopes and do not affect finding fingerprints, scores, rulesets, configuration fingerprints or comparison compatibility.
