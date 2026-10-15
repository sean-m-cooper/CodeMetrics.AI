# CodeMetrics.AI Python Analyzer

Experimental source-facts adapter for Python 3.12, using only the standard library. It parses files without importing or executing the target project. No package installation or target build is required.

This is **not a full Python scorecard**. The packaged `codemetrics-python` CLI now wraps function-complexity [source facts](../../shared/source-facts/README.md) in standard scorecard evidence v3. Method Complexity is the first scored dimension, using the accepted [Python scoring policy](METHOD_COMPLEXITY.md); the other eight dimensions remain unavailable. There is no CSV. Any aggregate is a partial assessment with 1/9 dimensions scored, equal to Method Complexity. The companion code-scorecard skill supports this workflow through an explicit local development package.

## Packaged evidence workflow

The existing `codemetrics-ai` npm package bundles the Python scripts, shared population engine, and canonical evidence validator. This first delivery does not create a separate PyPI distribution or duplicate scoring arithmetic in Python. Node.js 20+ and Python 3.12 are required. The development archive still reports npm version 0.5.0; published 0.5.0 does **not** contain this new command. Use a locally built archive until a coordinated release.

```powershell
# From the repository root, with npm dependencies already installed:
npm pack ./analyzers/javascript-typescript --pack-destination TestResults
# After installing that local archive into an isolated tooling directory:
codemetrics-python --project E:/repos/my-python-project/pyproject.toml --scorecard-output evidence.json
codemetrics-evidence --input evidence.json --inspect-output inspection.json
```

For repository development, invoke `node analyzers/javascript-typescript/dist/python-cli.js` after `npm run build --prefix analyzers/javascript-typescript`. Entry points can be directories, `pyproject.toml`, `.py`, or `.pyi` files. A manifest selects its directory without executing or interpreting build configuration. Paths in findings are relative to the selected directory, or a selected file's parent. Windows uses `py -3.12`; other platforms use `python3`. Pass `--python` with an absolute Python 3.12 executable path to override. The interpreter runs isolated from project startup hooks and imports only the packaged analyzer and standard library. Target code is never imported or executed.

`--scorecard-output` is required and must name a new `.json` file. Existing files are never overwritten. `--run-id` and `--audit-id` accept caller-supplied UUIDs; otherwise fresh IDs are generated. A complete selection exits 0 even when empty; parser/runtime/read/review failures exit 2 with incomplete evidence where the entry point and output are valid. Invalid CLI paths/options can fail before evidence is written. No previous output is reused.

The evidence embeds facts at `dimensions.codeQuality.sourceFacts`. Its `methodComplexity` contains the production population, policy, and per-function contributions supporting the dimension `score`. Historical evidence retains its original `experimentalPreview`. `codeQuality.duplication` contains exact-body review groups and explicit exclusions under the same run/audit IDs. `codeQuality.coverageReview` contains optional line-coverage/CRAP evidence, with missing, conditional, and source-bound observations kept distinct. One informational finding is emitted per duplicate group, with every occurrence in its observations. Code Quality is scored as Method Complexity when production implementations are available, skipped when none are eligible, and failed on incomplete analysis; the other eight dimensions are unsupported. CC ≥ 10 produces informational review findings. `population.members` counts selected implementations; the schema-required `types: 0` is explicitly unmeasured, not an absence-of-classes claim. Filters count observed files; pruned directories do not imply enumerated file counts.

The shared validator checks the v3 envelope and embedded facts, recomputes the score and contributions, and rejects invented dimension scores, inconsistent populations, and mismatched expected run IDs. Incomplete results have no usable score or findings. Historical imports remain historical. Matching supported versions, policies, selections, and scopes can compare Method Complexity and apply score-drop gates. Historical previews, empty eligible populations, and incomplete evidence cannot gate. Informational SARIF export is supported.

The CLI accepts the same selection flags described below. In the skill helper, prefix selection flags with `python-`, for example `--python-include 'src/**'`, and pass `--ecosystem python` for an explicit directory scope. The helper requires `--npm-package <local-archive.tgz>` and writes a fresh run directory with validated inspection. Python's packaged adapter version is `0.9.0`, independently of the enclosing npm version and unchanged source-facts producer `0.4.0-experimental`.

## Optional coverage review and explicit collection

The [coverage and CRAP review](COVERAGE.md) imports coverage.py JSON through `codemetrics-python --coverage-json`, with optional `--collection-manifest`. Missing or ambiguous coverage stays unavailable. The separate `codemetrics-python-collect` command explicitly runs a chosen test module using a caller-supplied interpreter with coverage.py and test dependencies. It binds newly generated coverage to before/after source hashes. Analysis rejects stale source or substituted reports when provenance is supplied; ordinary imports remain conditional. Both paths ship in the development archive; the skill imports evidence but never silently collects coverage. Neither enables a dimension score.

## Raw source-facts workflow

The packaged [exception-handler reviewer](ERROR_HANDLING.md) records handler bodies, local documentation, and owned control-transfer syntax without scores. Its first pinned corpus study includes import probes, deliberate fallback paths, and framework exception dispatch. The adapter embeds it at `dimensions.errorHandling.review`, and the skill presents category counts and source context. Python's Error Handling dimension remains unavailable as a score.

The packaged [exception-context reviewer](ERROR_HANDLING_CONTEXT.md) adds surrounding try/else/finally structure, nearby statements and prose, and narrowly recognized source patterns with an explicit unresolved category. It preserves the original handler observations and all scores. The adapter embeds context at `dimensions.errorHandling.contextReview`; the skill reports recognized shapes and unresolved cases without grading them.

The packaged [decomposition reviewer](DECOMPOSITION.md) measures owned statements and block nesting as separate unscored evidence. Its first corpus study covers Requests, Starlette, HTTPie, and Django. It is embedded at `dimensions.codeQuality.decomposition` and presented by the skill without grades or automatic extraction recommendations.

From the repository root (Python 3.12 required):

```powershell
py -3.12 analyzers/python/source_facts.py E:/repos/my-python-project
py -3.12 analyzers/python/source_facts.py E:/repos/my-python-project --output TestResults/python-facts.json
node shared/source-facts/preview.mjs TestResults/python-facts.json
```

Use `python3` on Linux/macOS. The first command emits facts to stdout. `--output` requires an existing parent and a new `.json` file and never overwrites existing files. A failed read/parse or unsupported parser version produces `status: incomplete` and exit 2; partial facts cannot produce a score preview. An empty selection is complete but unmeasured. The Node preview is optional and explicitly uncalibrated across languages.

## Measurement policy: `python/owned-function-complexity-v2`

Each selected `def`, `async def`, method, or lambda implementation body is an independent owner with baseline CC 1. Nested function and class bodies do not increase their parent's complexity. Nested definitions are discovered and measured separately. Decorators, class headers, annotations, default parameter expressions (including functions inside them), and module/class initializer **decisions** are excluded. Functions assigned in module/class bodies are still owners. File selection and declaration classification are recorded separately from CC measurement.

| Construct | Decision increment |
| --- | --- |
| `if` / `elif` | 1 each |
| `for`, `async for`, `while` | 1 each |
| `except` / `except*` handler | 1 each |
| Conditional expression | 1 |
| `and` / `or` | operands minus 1 |
| Chained comparison | comparisons minus 1 |
| Comprehension generator / filter | 1 each |
| Refutable `match` case | 1 |
| Match guard | 1, plus decisions within its expression |
| Match `or` pattern | alternatives minus 1, except whole-case simple membership lists |

A whole-case OR list of value/singleton patterns or class patterns without positional/keyword subpatterns contributes only its case increment. An optional `as` capture of the whole subject does not change that rule. Guards and body decisions still count. Structural alternatives, ORs nested inside structural patterns, and separate cases retain their increments. This is a source-complexity convention, not a claim of runtime equivalence. Policy v1 counted every OR alternative; historical evidence retains that policy and its original values.

An irrefutable wildcard/capture case contributes no case increment. `else`, `finally`, `with`, `await`, `yield`, calls, and assertions add no decision by themselves. These are explicit initial product policies, not evidence of equivalent CC across languages or runtime performance. Comprehension decisions belong to the containing function; this measures syntax, not interpreter-generated scopes.

Discovery inventories `.py` and `.pyi` files; standard dependency/cache/build directories listed in output scope are pruned and recorded. Their contents are not enumerated, so pruned-directory entries are not counts of unseen files. Symlinks and directory junctions are excluded. Selected files over 2 MiB and inventories exceeding 10,000 files fail explicitly. Python encoding declarations are honored. IDs hash relative POSIX file path, line, native parser column, and node kind. Native columns are used only inside opaque IDs; public locations are one-based lines. Changing source positions changes identities.

Syntax parsing is not semantic validation or compilation. Missing coverage, maintainability, dependency, architecture, and other scorecard measurements stay unsupported. Python 3.13+ grammar is not claimed by this 3.12 proof.

## Population policy: `python/source-selection-v1`

Python emits **source-facts v2** with a machine-readable selection inventory and per-function population. JS/TS continues to emit v1 facts internally; its scoring is unchanged. The source-facts producer remains `0.4.0-experimental`; the packaged scoring wrapper is `0.9.0`.

The scored population comprises selected production **candidates**, not a verified installed-package or reachable-code population. The following rules make that selection inspectable:

| Evidence | Default disposition |
| --- | --- |
| `.pyi` file | Declaration file; never receives implementation credit |
| `test`/`tests` directory, `test_*.py`, `*_test.py`, or `conftest.py` | Test population; excluded by default |
| `docs`/`doc`/`examples`/`benchmarks` directory or root `setup.py`/`noxfile.py` | Support population; excluded by default |
| Recognized generated-file comment within the first 20 lines | Generated population; excluded by default |
| Other selected `.py` file | Production candidate |
| Resolved overload, abstract method, or direct protocol method with only a docstring/pass/ellipsis body | Recorded declaration; no implementation credit |

Ordinary `pass`, ellipsis, or docstring-only functions remain implementations unless the declaration evidence above exists. Abstract/protocol methods with executable bodies remain implementations. Raising `NotImplementedError` is executable behavior and is not automatically removed. Comments are not judged as business decisions. Declaration recognition uses module-level imports from `typing`, `typing_extensions`, and `abc`, supports aliases and qualified references, and conservatively gives up on names rebound anywhere in the file. Custom decorators, indirect protocol inheritance, conditional/local imports, and dynamic rebinding are not resolved. This avoids treating a familiar-looking name as proof of a declaration.

Use explicit root-relative patterns for real package boundaries and special layouts:

```powershell
py -3.12 analyzers/python/source_facts.py E:/repos/django --include 'django/**' --production-path 'django/test/**'
py -3.12 analyzers/python/source_facts.py E:/repos/my-project --include 'src/**' --exclude 'src/vendor/**'
py -3.12 analyzers/python/source_facts.py E:/repos/my-project --include-tests --include-support --include-generated
```

`--include`, `--exclude`, `--test-path`, and `--production-path` are repeatable. Patterns use case-sensitive `fnmatch` against POSIX relative paths, with a leading `**/` also matching zero directories. `*` can span `/`; these are not Git-ignore patterns. Absolute paths, backslashes, and parent traversal are rejected. Explicit exclusions win over inclusion. An explicit production classification wins over test/support path conventions; generated markers still require `--include-generated`. Inclusion flags add those populations to review evidence and retain their labels; they do not enter the Method Complexity score. `.pyi` and recognized declaration bodies remain excluded. No target configuration file or build backend is executed or implicitly adopted.

The selection inventory records normalized options and their fingerprint, observed files with disposition/reason, pruned directories, and excluded declaration IDs. Successfully read source has a SHA-256 of decoded text with normalized newlines, encoded as UTF-8. Excluded unread files have a null digest. A selected parse/read failure makes the document incomplete; files outside the declared selection do not fail that selection. A selected population with no implementation bodies has no score. Generated markers are comment tokens, so matching text inside strings/docstrings does not exclude a file. Markers are a convention, not proof that a generated file has never been edited.

## Pinned corpus

The first corpus contains Requests, Starlette, HTTPie, and Django. [The manifest](../../shared/calibration/corpus/python-selection.json) pins Git revisions, package selections, and the explicit Django testing-helper override. The runner verifies clean checkouts, validates facts through the shared engine, checks every retained owner's CC against the original adapter plus the independently implemented, reviewed membership adjustment, and checks that each original owner is retained or explicitly excluded exactly once.

```powershell
# Export the trusted reference analyzer from the recorded commit using Git, preserving UTF-8.
py -3.12 -c "import pathlib,subprocess; pathlib.Path('TestResults/python-reference.py').write_bytes(subprocess.check_output(['git','show','e91af93:analyzers/python/source_facts.py']))"
py -3.12 analyzers/python/corpus.py --repos E:/repos --output TestResults/python-corpus --reference-analyzer TestResults/python-reference.py
```

The `TestResults` directory must exist. The reference is verified against Git before importing it. Target source is only parsed, never installed or executed. The runner checks [frozen population baselines](../../shared/calibration/baselines/python-selection.json); `--record` is reserved for explicitly reviewed changes. These are early regression baselines, not cross-language calibration or full Python scores.

The [first corpus review](../../shared/source-facts/python-selection-2026-10-10.md) records measurements, exclusions, source context, and remaining limits.

The [complexity calibration study](../../shared/scorecard-schema/python-complexity-calibration.md) reviews 20 source bodies, tests Python idioms and accepted population expectations, and supports the narrow OR-membership adjustment. The approved narrow adjustment is now implemented as measurement policy v2. The accepted Method Complexity baseline is now the first supported dimension score; other evidence remains unscored. The study report remains historical v1 evidence. In a checkout of calibration checkpoint `b6f953b`, reproduce it with `py -3.12 analyzers/python/calibration.py --repos E:/repos --output TestResults/python-calibration-new` (a new output directory is required).

## Tests

The [duplication review](DUPLICATION.md) detects exact whole-function bodies with a five-statement minimum, source provenance, and review-only groups. It is bundled with the development analyzer and presented by the skill, while remaining unscored. The standalone tool also supports a configurable minimum; that option is not exposed by the packaged CLI.

```powershell
py -3.12 -m unittest discover -s analyzers/python/tests -v
npm run build --prefix analyzers/javascript-typescript
node --test shared/source-facts/conformance.test.mjs
node analyzers/python/package-smoke.mjs
```

Collection integration checks require coverage.py 7.6.1 in a Python 3.12 environment. Set `CODEMETRICS_COVERAGE_PYTHON` to that interpreter for Node/package tests if the default interpreter does not have it. Coverage.py is not needed for ordinary source analysis or coverage import.
