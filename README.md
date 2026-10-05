# CodeMetrics.AI

CodeMetrics.AI is a suite of deterministic code analyzers that produce shared scorecard evidence for AI-assisted codebase review.

Its goal is to guide human and AI developers toward better, more maintainable and performant code. Scores express documented product expectations and identify opportunities for improvement; they do not prove defects or replace engineering judgment. The [complexity scoring policy](shared/scorecard-schema/calibration.md#complexity-scoring-product-decision) records the accepted method-CC bands and the distinction between product decisions and their scoring implementation. Real-world usage and developer feedback should inform explicit, versioned policy revisions.

Each analyzer runs in the package ecosystem natural to its target language, then writes the same default outputs:

| File | Description |
|------|-------------|
| `.scorecard/<ecosystem>/metrics.csv` | Raw code metrics in the shared CSV shape |
| `.scorecard/<ecosystem>/evidence.json` | Scored evidence across stable quality dimensions |

## Analyzers

| Ecosystem | Location | Package | Status |
|-----------|----------|---------|--------|
| .NET / C# | `analyzers/dotnet` | `CodeMetrics.AI` NuGet global tool | Available |
| JavaScript / TypeScript / React | `analyzers/javascript-typescript` | `codemetrics-ai` NPM package | Implemented; scores uncalibrated |
| Python | `analyzers/python` | Python package | Planned next wave |
| Rust | `analyzers/rust` | Rust crate | Planned next wave |

## Shared Contract

The shared scorecard contract is documented in `shared/scorecard-schema`.

Stable dimensions:

- `codeQuality`
- `maintainability`
- `errorHandling`
- `performanceAsync` — displayed as **Async/Blocking Usage**
- `security`
- `testing`
- `documentation`
- `dependencyManagement`
- `architecture`

## .NET Usage

```bash
dotnet tool install -g CodeMetrics.AI
code-metrics
```

See the [.NET analyzer README](analyzers/dotnet/README.md) for installation, usage, options, filtering, and code-annotation details.

### .NET code annotations

The .NET analyzer recognizes several annotations that affect analysis:

- `// amp-metrics: sync-required` marks an intentionally synchronous method.
- `[FromServices]` prevents action-injected dependencies from inflating class coupling.
- `[Authorize]` and `[AllowAnonymous]` inform security findings.
- Common xUnit, NUnit, and MSTest attributes identify test methods and skipped tests.

See [Code annotations and recognized attributes](analyzers/dotnet/README.md#code-annotations-and-recognized-attributes)
for exact placement, supported spellings, and scoring effects.

## JavaScript / TypeScript Usage

```bash
npx codemetrics-ai
```

The JS/TS analyzer implements source metrics, function-based complexity and maintainability, React hook/effect checks and bounded standard-Promise/Array callback checks. Its module dependency graph reports resolution coverage, type-only references, cycles and fan-in/out as unscored architecture evidence. Decomposition reports owned executable statements, and error-handling evidence resolves selected-source callback bodies without double-counting reuse. Architecture, decomposition, and error handling remain unscored; the other unsupported dimensions remain unavailable. See its [README](analyzers/javascript-typescript/README.md) for scope and scoring policy, and the [0.4.0 release notes](docs/releases/javascript-typescript-0.4.0.md) for migration requirements.

## Evidence and release verification

Analyzers now emit schema v3; see the [migration guide](shared/scorecard-schema/migration-v3.md). Use the shared [evidence CLI](docs/evidence-workflows.md) for baseline comparisons, optional CI gates, and SARIF export. The [pinned regression corpus](shared/calibration/README.md) checks accuracy labels and score distributions on every CI run.

## License

[MIT](LICENSE)
