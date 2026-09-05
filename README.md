# CodeMetrics.AI

CodeMetrics.AI is a suite of deterministic code analyzers that produce shared scorecard evidence for AI-assisted codebase review.

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
- `performanceAsync`
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

The JS/TS analyzer implements source metrics and React hook/effect checks. Other dimensions are explicitly skipped. See its [README](analyzers/javascript-typescript/README.md) for scope and scoring policy.

## Evidence and release verification

Analyzers now emit schema v3; see the [migration guide](shared/scorecard-schema/migration-v3.md). Use the shared [evidence CLI](docs/evidence-workflows.md) for baseline comparisons, optional CI gates, and SARIF export. The [pinned regression corpus](shared/calibration/README.md) checks accuracy labels and score distributions on every CI run.

## License

[MIT](LICENSE)
