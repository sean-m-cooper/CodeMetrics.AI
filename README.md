# CodeMetrics.AI

CodeMetrics.AI is a suite of deterministic code analyzers that produce shared scorecard evidence for AI-assisted codebase review.

Each analyzer runs in the package ecosystem natural to its target language, then writes the same default outputs:

| File | Description |
|------|-------------|
| `.scorecard/metrics.csv` | Raw code metrics in the shared CSV shape |
| `.scorecard/evidence.json` | Scored evidence across stable quality dimensions |

## Analyzers

| Ecosystem | Location | Package | Status |
|-----------|----------|---------|--------|
| .NET / C# | `analyzers/dotnet` | `CodeMetrics.AI` NuGet global tool | Available |
| JavaScript / TypeScript / React | `analyzers/javascript-typescript` | `codemetrics-ai` NPM package | Scaffolded |
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

See `analyzers/dotnet/README.md` for .NET-specific usage.

## JavaScript / TypeScript Usage

```bash
npx codemetrics-ai
```

The JS/TS analyzer scaffold lives in `analyzers/javascript-typescript`.

## License

[MIT](LICENSE)
