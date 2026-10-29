# Python coverage validation: Starlette

Date: 2026-10-13. [Machine-readable results](python-coverage-starlette-2026-10-13.json) record commands, identities, hashes, dependency versions, and example measurements. This pass validates collection with an asynchronous framework, a broader test selection, and newer coverage tooling. It does not calibrate a score.

## Environment and scope

- Clean Starlette revision `96d5023c55b29224edb8d684868c49f681d079a9` at `E:/repos/starlette`; production selection `starlette/**`.
- Windows, Python 3.12.10, coverage.py 7.16.1, pytest 9.1.1. Starlette's frozen `uv.lock` dev environment was installed outside the checkout, with no changes to source or lockfile.
- Installed development npm archive from the preceding packaging validation; adapter `0.5.0-experimental`, SHA-256 `d3dc223b73ff3f93b1ae8dd090e07264c2c595630bfc397b0c9c599895c1e33d`.
- Pytest's AnyIO plugin was enabled explicitly with `-p anyio.pytest_plugin`, exercising asyncio and Trio test variants. Benchmark modules were not executed.

## The full-suite failure remains a failure

The complete `tests` selection produced **1,338 passed, seven failed, two skipped, and two expected failures**. A direct pytest control without the collector reproduced all seven failures:

| Test family | Cases | Observed failure |
| --- | ---: | --- |
| `test_missing_env_file_raises` | 1 | Windows path backslashes used as an unescaped regex |
| `test_staticfiles_with_package` | 2 | Checkout fixture contains CRLF; expected response contains LF |
| `test_staticfiles_relative_directory_symlinks` | 2 | Same CRLF/LF response mismatch |
| `test_staticfiles_with_invalid_dir_permissions_returns_401` | 2 | POSIX permission expectation does not hold on this Windows filesystem |

The collector retained an incomplete receipt. Passing that receipt into the installed analyzer produced incomplete evidence with no coverage estimates, summary, findings, or complexity preview. This is a real failure-path validation, not a successful full-suite coverage run. No upstream tests, permissions, or source files were changed to obtain a pass.

## Explicit subset result

A separate new collection excluded those four named test families (seven parametrized cases). It produced **1,338 passed, two skipped, seven deselected, and two expected failures**. The two skips explicitly identify Unix-only `os.pathconf`. The exact exclusion expression is recorded in the receipt and JSON summary.

The installed analyzer accepted the matching receipt and reported:

- **508** selected functions; **459** source-bound estimates; zero conditional estimates.
- **49 unavailable**: 25 parents with nested definitions, 23 bodies without reportable lines, and one lambda. These exclusions are not uncovered-code findings.
- Experimental complexity preview **7.1**; zero exact-body duplication groups under the existing policy.
- All nine dimension scores unavailable; no overall grade or CRAP threshold.

| Example | CC | Covered / executable body lines | Line-based CRAP |
| --- | ---: | ---: | ---: |
| `IdentityResponder.send_with_compression` | 19 | 54 / 54 | 19 |
| `StaticFiles.get_response` | 17 | 26 / 28 | 17.1053 |
| `BaseSchemaGenerator.get_endpoints` | 15 | 24 / 24 | 15 |
| `URL.replace` | 14 | 27 / 27 | 14 |
| `Router.app` | 14 | 36 / 36 | 14 |

The table rounds only the displayed CRAP value; evidence retains the full arithmetic. Full line coverage leaves complexity intact. In particular, the two missing body lines in `StaticFiles.get_response` cannot be interpreted independently of the documented test exclusions. These observations do not establish assertion quality, branch completeness, or defects.

## Regression protection

A new authored integration fixture calls an async function through `asyncio.run`, awaits `asyncio.to_thread`, and leaves another async body uncalled. It checks independent line observations for all three bodies: threaded 2/3, awaiting 3/4, and unused 0%. Merely importing an async definition must not count its body as executed.

All **64 Python tests passed with coverage.py 7.6.1**, and all **64 passed with 7.16.1**. The installed package's **14 evidence tests** also passed with the newer coverage environment. CI now defines both coverage versions on Ubuntu and Windows; only the Windows executions were performed locally. This validation does not claim that the new remote CI matrix has passed.

## Reproduce and inspect

With the pinned checkout and frozen dev environment prepared, use a new output directory for each collection:

```powershell
codemetrics-python-collect E:/repos/starlette --include 'starlette/**' --python E:/temp/codemetrics-starlette-coverage/Scripts/python.exe --output-directory E:/temp/starlette-coverage-new --timeout 600 -- -p anyio.pytest_plugin -q tests
# The separately labeled Windows subset adds:
# -k 'not (test_missing_env_file_raises or test_staticfiles_with_package or test_staticfiles_with_invalid_dir_permissions_returns_401 or test_staticfiles_relative_directory_symlinks)'
codemetrics-python --project E:/repos/starlette --include 'starlette/**' --coverage-json E:/temp/starlette-coverage-new/coverage.json --collection-manifest E:/temp/starlette-coverage-new/provenance.json --scorecard-output E:/temp/starlette-evidence-new.json
```

Local artifacts are archived under `TestResults/python-coverage-starlette-2026-10-13/`, including both collections, both evidence results, the non-collector control log, and dependency inventory. The original failed receipt remains intact. Collection `523bd68b-28bd-459b-a50f-c7c4f236d985` identifies the passing subset; evidence run `e1f44309-ff54-4fd0-b6c4-ec33fb294077` identifies its later analysis. Neither replaces the failed full-suite collection.

The remaining limits are unchanged: unsigned local receipts, selected-source pre/post observations rather than an immutable filesystem, no subprocess aggregation, and no assertion-quality guarantee. Future Linux corpus validation should use the full suite rather than automatically inheriting this Windows exclusion list.
