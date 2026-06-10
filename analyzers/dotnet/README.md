# CodeMetrics.AI

A .NET 10 global tool that performs deterministic static analysis on .NET solutions using the Roslyn compiler API. Produces VS-compatible code metrics and scored scorecard evidence across 9 quality dimensions.

## Install

```bash
dotnet tool install -g CodeMetrics.AI
```

## Usage

```bash
# Auto-discover solution in current directory
code-metrics

# Specify solution explicitly
code-metrics --solution MyApp.slnx

# Skip dependency probe (avoids dotnet list package calls)
code-metrics --skip-dependency-probe

# Custom output paths
code-metrics --output ./results/metrics.csv --scorecard-output ./results/evidence.json
```

## Output

| File | Description |
|------|-------------|
| `.scorecard/dotnet/metrics.csv` | VS-compatible raw metrics (same format as Visual Studio's Code Metrics Results) |
| `.scorecard/dotnet/evidence.json` | Scored evidence across 9 dimensions (schema v2) |

## Dimensions

The tool scores your codebase across 9 quality dimensions (0-10 scale):

| Dimension | Method |
|-----------|--------|
| Code Quality | Statistical — decomposition ratio and max member cyclomatic complexity |
| Maintainability | Statistical — maintainability index population/tail/extreme analysis |
| Error Handling | Rule-based — empty catches, throw ex, broad catches, sync blocking |
| Performance & Async | Rule-based — sync-over-async, Thread.Sleep, SaveChanges in loops |
| Security | Rule-based — hardcoded secrets, SQL interpolation, unsafe deserialization |
| Testing | Rule-based — test coverage, assertion density, placeholder detection |
| Documentation | Deduction-based — README, docs/, XML docs, public API coverage |
| Dependency Management | Rule-based — vulnerabilities, outdated, deprecated, version drift |
| Architecture & SOLID | Rule-based — project cycles, layering violations, metric hotspots |

## Raw Metrics

Per-type and per-member metrics collected via Roslyn:

- **Cyclomatic Complexity** — decision point counting
- **Lines of Code** — source lines (excluding comments/blanks/braces) and executable statements
- **Maintainability Index** — composite of CC, LOC, and Halstead Volume
- **Class Coupling** — distinct external type dependencies
- **Depth of Inheritance** — base type chain length

## MSBuild Integration

Copy `Directory.Build.targets` from the [scorecard-tooling](https://github.com/sean-m-cooper/ai_tools/tree/main/skills/code-scorecard/scorecard-tooling) directory to your solution root:

```bash
dotnet build /t:Scorecard
dotnet build /t:Scorecard /p:ScorecardConfiguration=Release
```

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `--solution` | Auto-discover | Path to .sln or .slnx file |
| `--output` | `.scorecard/dotnet/metrics.csv` | CSV output path |
| `--scorecard-output` | `.scorecard/dotnet/evidence.json` | JSON evidence output path |
| `--configuration` | `Debug` | Build configuration |
| `--skip-dependency-probe` | `false` | Skip dependency management checks |

## Project Filtering

The tool automatically skips non-production projects:

- Test projects (name contains "Tests")
- Aspire hosts (AppHost, ServiceDefaults, Hosting)
- Benchmarks, Samples, Demo, Playground projects

## Requirements

- .NET 10 SDK
- Solution must be buildable (`dotnet build` succeeds)

## License

[MIT](../../LICENSE)
