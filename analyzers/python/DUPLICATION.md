# Experimental Python duplication review

This capability reports exact repeated function bodies for source review. The development `codemetrics-python` package (adapter `0.7.0`) embeds it at `dimensions.codeQuality.duplication` in evidence v3. The code-scorecard skill presents it as unscored review evidence. The standalone CLI remains available for focused analysis. Coverage integration does not change the duplication policy or complexity formula.

```powershell
py -3.12 analyzers/python/duplication.py E:/repos/my-project --include 'src/**' --output TestResults/duplication.json
py -3.12 analyzers/python/duplication_corpus.py --repos E:/repos --output TestResults/python-duplication-new
```

Python 3.12 is required. Target source is parsed without imports, dependency installation, or execution. The first command accepts the same file selection flags as `source_facts.py`; its target is a directory or Python file, not a project manifest. Output must be a new `.json` file with an existing parent directory. The corpus runner requires clean pinned checkouts and a new output directory.

## Measurement boundary

Policy `python/exact-function-body-v1` compares whole selected implementation bodies after native AST parsing. It omits source positions and a leading function docstring. Comments and formatting do not enter the AST. It retains identifier names, attributes, literal values, operators, statement order, and all other body structure. Equality uses the complete normalized structure; the displayed hash is an identity, not the equality test.

Function names, signatures, default parameters, decorators, and enclosing classes are outside the compared body. The same body may refer to different bindings or behave differently in different contexts. Equal bodies do **not** prove semantic equivalence, interchangeable APIs, unnecessary code, or safe extraction. Conversely, renamed copies, partial blocks, and structurally different implementations are not detected. Literal spellings that parse to the same value can match; this is structural equality, not byte equality.

The default minimum is **five statement nodes** per body. Statements within control-flow blocks count; physical lines do not determine eligibility. The leading docstring, `pass`, `global`, `nonlocal`, and annotations without values do not contribute to this count, although non-docstring syntax remains part of the equality key. `--minimum-statements` changes the minimum (integer ≥ 2) and the configuration fingerprint. This is an initial noise-control threshold, not an established refactoring threshold.

Parents containing a nested function, class, or lambda are excluded rather than compared with missing body regions. Nested function implementations are still measured separately if independently eligible; lambdas are excluded from this whole-statement-body proof. Async and synchronous functions are compared separately. Production, test, support, and generated populations are also kept separate, even when selection overrides include them. Recognized declaration-only bodies and excluded files follow the existing source-selection policy.

Each group has a stable policy-specific ID, body digest, statement count, and sorted occurrences with function ID, relative file, function start/end lines, population, and source digest. A body occurring three times forms one group with three occurrences, not three pairwise findings. No overlapping block counts or duplicated-line percentage is produced. Summary counts distinguish selected functions, eligible functions, excluded functions, matching groups, and matched functions; zero matches only describes this narrow search.

## Freshness and incomplete analysis

Each invocation gets a fresh run ID and audit ID; UUID-valued `--run-id` and `--audit-id` allow a caller to supply its own. IDs are separate from body fingerprints. The report records selection inventory, parser/producer versions, policy, effective configuration, and configuration fingerprint. Source digests must match the initial population pass when files are reread. This checks consistency between reads; it is not a filesystem snapshot or a promise that source cannot change after observation.

A selected parse/read failure, source mismatch, changed function identity, or exceeded resource budget makes the report incomplete. Incomplete reports expose diagnostics and inventory, with **no groups and no summary**. No partial results become a score or a claim of no duplication. Complete empty selections explicitly report zero eligible functions. Exit codes are 0 for complete and 2 for incomplete or CLI errors. Prior output files are never overwritten or reused.

In addition to the source analyzer's file-size and file-count limits, this proof allows at most 100,000 selected owners and 64 MiB of normalized eligible bodies. Limits are recorded in the configuration. It fails explicitly rather than truncating a group list. The standalone report remains its own experimental artifact. The packaged analyzer embeds it in the extensible Code Quality section of evidence v3, alongside unchanged source-facts v2. Packaged runs fix the minimum at five statements, share the exact source population and invocation IDs, and validate source provenance, owner membership, exclusions, and group accounting. Validation checks evidence consistency, not equivalence by rereading target source. If either measurement is incomplete, the combined analysis is incomplete and its Method Complexity score and findings are withheld.

The [first corpus review](../../shared/scorecard-schema/calibration-runs/python-duplication-2026-10-10.md) records findings and limitations. Automated tests run through the existing Python CI discovery command:

```powershell
py -3.12 -m unittest discover -s analyzers/python/tests -v
```
