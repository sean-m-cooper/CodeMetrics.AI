# .NET Analyzer

The .NET analyzer is packaged as the `CodeMetrics.AI` NuGet global tool and exposed through the `code-metrics` command.

## Location

```text
analyzers/dotnet/
```

## Usage

```bash
dotnet tool install -g CodeMetrics.AI
code-metrics
code-metrics --solution MyApp.slnx
code-metrics --skip-dependency-probe
code-metrics --output ./results/metrics.csv --scorecard-output ./results/evidence.json
```

## Outputs

| File | Description |
|------|-------------|
| `.scorecard/dotnet/metrics.csv` | VS-compatible raw metrics |
| `.scorecard/dotnet/evidence.json` | Scorecard evidence using schema version 2 |

## Local Development

```bash
dotnet test analyzers/dotnet/CodeMetrics.AI.slnx --verbosity minimal
dotnet pack analyzers/dotnet/src/CodeMetrics.AI/CodeMetrics.AI.csproj -c Release -o ./nupkg
```
