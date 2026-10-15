# Experimental Python decomposition evidence

This source-only reviewer measures **owned AST statements** and **maximum block nesting** independently. It shares the current source-facts selection and complexity policy, but adds no scores, gates, deductions, or automatic extraction recommendations. It never imports or executes target code.

```powershell
py -3.12 analyzers/python/decomposition.py E:/repos/my-project --include 'src/**' --output TestResults/decomposition.json
py -3.12 analyzers/python/decomposition_corpus.py --repos E:/repos --output TestResults/python-decomposition-new
```

Python 3.12 is required. The standalone CLI accepts directories or Python files and the same include/exclude/population switches as `source_facts.py`. A manifest is not a source-file entry point; pass its containing directory. `--output` requires a new `.json` file with an existing parent. Supplied UUID `--run-id` and `--audit-id` are preserved; otherwise both are freshly generated. Exit codes are 0 for complete, including empty selections, and 2 for incomplete measurements or invalid invocation.

The development `codemetrics-python` adapter `0.11.0` embeds this report at `dimensions.codeQuality.decomposition` under ruleset `python-2026-10-15-exception-operations-review-v1`. The npm archive includes the producer and this policy document. The scorecard skill requires that local archive and presents separate statement/nesting observations; only Method Complexity is scored; decomposition remains unscored. Existing complexity, duplication, and coverage measurement policies are unchanged.

The package analyzes decomposition automatically from the same source-facts population and invocation IDs. Its validator checks producer/selection provenance, unique and complete owner membership, source digests, statement accounting, nesting bounds, and recomputed per-population nearest-rank summaries. These are integrity checks, not an independent AST remeasurement or authentication of an edited report. A missing or invalid report fails validation; an incomplete decomposition review makes the combined run incomplete and withholds its score and findings. Historical `0.5.0-experimental` coverage-era evidence remains valid under its original contract, without invented decomposition measurements. No new decomposition findings, score gates, or thresholds are enabled.

## Policy: `python/owned-statements-block-nesting-v1`

Each selected `def`, `async def`, or method owns its body. Count each included `ast.stmt` node once, including control statements and the statements in their suites. A multiline call is one expression statement; semicolon-separated statements remain separate. A chained assignment is one statement. Comments and formatting do not contribute.

The leading function docstring, `pass`, bare ellipsis, `global`, `nonlocal`, annotation-only assignments, and Python type-alias declarations are excluded. Imports, assignments with values, calls, returns, raises, assertions, deletion, and other executable statement forms remain counted. This is a source-syntax convention, not a count of runtime instructions: a later bare string expression remains an expression statement.

Nested function, lambda, and class definitions form boundaries. Their headers, decorators, defaults, annotations, and bodies do not contribute to the parent's measurement. Selected nested functions and methods are independently measured; class initializer statements are outside this function metric. `excludedSyntax` makes these boundaries visible. Moving work into an unmeasured initializer does not establish better decomposition.

Lambdas remain selected owners for complexity, but receive `status: unavailable`, reason `lambda-expression-body`, and `measurement: null` here. Do not treat them as zero-statement implementations. Declaration-only functions excluded by the source-facts policy remain visible in selection provenance. A selected implementation containing only excluded syntax can legitimately have zero owned statements; that is a measurement, not a perfect score.

### Block nesting

The function body starts at depth zero. Each `if`, loop, `with`/`async with`, `try`/`try*`, or `match` adds one level for its suites. `else`, `except`, `finally`, and match cases are sibling arms at their containing construct's depth. They do not add another level by themselves. An `elif` is a sibling condition at the same depth as its initial `if`; an explicit nested `if` inside `else` adds depth. Source indentation positions distinguish the two because their unpositioned ASTs can otherwise be identical.

Comprehensions, boolean expressions, conditional expressions, and patterns do not add **block** nesting or standalone statements. The existing complexity metric still counts its applicable decisions. This policy does not measure expression size, expression nesting, runtime call depth, or cognitive complexity. Context managers and cleanup scopes can produce justified nesting; the measurement does not recommend removing them.

### Evidence and population

Each function row records its stable source-facts ID, location, source digest, population, synchronous/async/lambda kind, and existing CC. A measured row adds `ownedStatements`, `maxBlockNesting`, a statement-kind histogram, and counts of excluded syntax. All rows are `review-only`.

Summaries partition production, test, support, and generated populations and distinguish selected, measured, and unavailable bodies. Each measured population reports nearest-rank p50/p90/p95/max independently for size and nesting: quantile index is `ceil(percent * count / 100) - 1`. An empty measured population has null distributions, not zero percentiles. Do not merge populations or projects into a score.

The report includes source-selection provenance, source-facts producer/policy, a versioned measurement policy, effective configuration/fingerprint, and fresh review IDs. Source files are reread with the existing hash, link, and file-size checks. Owner identity must match the original population. Selected-source errors, changed content/owners, parser failures, or the 100,000-owner budget make the entire review incomplete, with diagnostics but no function rows or summary. No partial result can imply a clean project. These observations are not an immutable-filesystem guarantee or authentication of an edited report.

## Corpus review and calibration

The [first corpus study](../../shared/scorecard-schema/calibration-runs/python-decomposition-2026-10-13.md) covers the pinned Requests, Starlette, HTTPie, and Django selections. It verifies unchanged source digests and every existing function fact, including CC; it saves full rows and separate size/nesting rankings. Large low-CC bodies use the existing CC 1–5 band for sampling, not an exemption or a size threshold.

The initial study supports separate review signals. Long registration and initialization routines show why statement count alone is insufficient for an automatic decomposition penalty. Short but deeply nested routines show why size alone can miss meaningful structure. Neither extreme proves a defect or safe refactoring opportunity. Threshold counts in the study are calibration observations only; this implementation contains no decomposition scoring ladder.

Automated checks run with normal Python test discovery. They protect wrapping/semicolon semantics, ownership, declaration and lambda boundaries, flat versus nested conditions, exception and match arms, async constructs, expression scope, separate populations, empty distributions, provenance failure, non-execution, IDs, and output preservation.
