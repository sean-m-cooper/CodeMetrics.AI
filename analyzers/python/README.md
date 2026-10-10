# CodeMetrics.AI Python Analyzer

Experimental source-facts adapter for Python 3.12, using only the standard library. It parses files without importing or executing the target project. No package installation or target build is required.

This is **not a full Python scorecard**. It produces function-complexity facts under the [adapter contract](../../shared/source-facts/README.md), not scorecard evidence v3, CSV, or nine-dimension scores. The code-scorecard skill does not route Python projects to it yet.

## Run

From the repository root (Python 3.12 required):

```powershell
py -3.12 analyzers/python/source_facts.py E:/repos/my-python-project
py -3.12 analyzers/python/source_facts.py E:/repos/my-python-project --output TestResults/python-facts.json
node shared/source-facts/preview.mjs TestResults/python-facts.json
```

Use `python3` on Linux/macOS. The first command emits facts to stdout. `--output` requires an existing parent and a new `.json` file and never overwrites existing files. A failed read/parse or unsupported parser version produces `status: incomplete` and exit 2; partial facts cannot produce a score preview. An empty selection is complete but unmeasured. The Node preview is optional and explicitly uncalibrated across languages.

## Measurement policy: `python/owned-function-complexity-v1`

Each `def`, `async def`, method, or lambda body is an independent owner with baseline CC 1. Nested function and class bodies do not increase their parent's complexity. Nested definitions are discovered and measured separately. Decorators, annotations, default parameter expressions (including functions inside them), and module/class initializer **decisions** are excluded. Functions assigned in module/class bodies are still owners. Test code and stub bodies are included; this prototype does not classify production versus tests or infer abstract implementations.

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
| Match `or` pattern | alternatives minus 1 |

An irrefutable wildcard/capture case contributes no case increment. `else`, `finally`, `with`, `await`, `yield`, calls, and assertions add no decision by themselves. These are explicit initial product policies, not evidence of equivalent CC across languages or runtime performance. Comprehension decisions belong to the containing function; this measures syntax, not interpreter-generated scopes.

Selection recursively includes `.py` files; standard dependency/cache/build directories listed in output scope are excluded. Symlinks and directory junctions are excluded. Files over 2 MiB and selections exceeding 10,000 files fail explicitly. Python encoding declarations are honored. IDs hash relative POSIX file path, line, native parser column, and node kind. Native columns are used only inside opaque IDs; public locations are one-based lines. Changing source positions changes identities.

Syntax parsing is not semantic validation or compilation. Missing coverage, maintainability, dependency, architecture, and other scorecard measurements stay unsupported. Python 3.13+ grammar is not claimed by this 3.12 proof.

## Tests

```powershell
py -3.12 -m unittest discover -s analyzers/python/tests -v
npm run build --prefix analyzers/javascript-typescript
node --test shared/source-facts/conformance.test.mjs
```
