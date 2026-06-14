# CodeMetrics.AI Analyzer Suite And JS/TS Package Design

## Purpose

CodeMetrics.AI should become a suite of deterministic code analyzers that can run against modern codebases in their native ecosystems while producing a shared scorecard contract for AI-assisted review. The existing .NET analyzer remains the first implementation. The next analyzer is a JavaScript/TypeScript/React NPM package.

The long-term goal is that the same AI skill can run the appropriate analyzer for a repository, then consume `.scorecard/evidence.json` and `.scorecard/metrics.csv` regardless of implementation language.

## Analyzer Waves

Wave 1:

- .NET / C# analyzer, existing global tool.
- JavaScript / TypeScript / React analyzer, new NPM package.

Wave 2:

- Python analyzer.
- Rust analyzer.

Future analyzers:

- Java / Kotlin.
- Go.
- Swift / iOS.
- PHP / Laravel.
- Ruby / Rails.

## Repository Layout

The repository should be reorganized into a language-neutral analyzer suite:

```text
analyzers/
  dotnet/
    CodeMetrics.AI.slnx
    README.md
    src/
      CodeMetrics.AI/
    tests/
      CodeMetrics.AI.Tests/

  javascript-typescript/
    package.json
    tsconfig.json
    README.md
    src/
    tests/

  python/
    README.md

  rust/
    README.md

shared/
  scorecard-schema/
    evidence.schema.v1.json
    metrics-csv.md
    dimensions.md
    examples/
      dotnet-evidence.json
      javascript-typescript-evidence.json

docs/
  analyzers/
    dotnet.md
    javascript-typescript.md
    python.md
    rust.md
```

Root-level files become suite-level files:

- `README.md`: suite overview, analyzer list, shared output contract.
- `LICENSE`: shared license.
- `.github/`: suite CI and per-analyzer publishing workflows.

The current .NET project keeps its package identity, command name, and project names, but moves under `analyzers/dotnet/`.

## Shared Scorecard Contract

All analyzers produce the same default output paths:

```text
.scorecard/metrics.csv
.scorecard/evidence.json
```

The evidence contract standardizes:

- `schemaVersion`.
- `generatedAtUtc`.
- `tool.name`.
- `tool.version`.
- project/package metadata.
- filters and skipped inputs.
- population counts.
- dimension keys.
- finding shape: `category`, `severity`, `file`, `line`, `project`, `type`, `member`, `package`, `message`.
- dimension result shape: `status`, `score`, `basis`, `findings`, plus language-specific extra data.

The stable dimension keys are:

- `codeQuality`.
- `maintainability`.
- `errorHandling`.
- `performanceAsync`.
- `security`.
- `testing`.
- `documentation`.
- `dependencyManagement`.
- `architecture`.

Each analyzer may interpret these dimensions idiomatically for its ecosystem, but the key names and 0-10 scoring scale remain stable for AI consumers.

## JS/TS Analyzer Package

The JavaScript/TypeScript analyzer is a standalone NPM CLI under `analyzers/javascript-typescript/`. It does not depend on the .NET implementation at runtime. It shares the scorecard contract and naming conventions.

Example CLI:

```bash
npx codemetrics-ai
npx codemetrics-ai --project ./package.json
npx codemetrics-ai --tsconfig ./tsconfig.json
npx codemetrics-ai --output .scorecard/metrics.csv
npx codemetrics-ai --scorecard-output .scorecard/evidence.json
```

The analyzer uses the TypeScript compiler API to parse `.js`, `.jsx`, `.ts`, and `.tsx`. It discovers files from `tsconfig.json` when present. If no TypeScript config exists, it scans common roots such as `src`, `app`, `pages`, `components`, and `lib`.

It skips known non-source paths, including:

- `node_modules`.
- build outputs such as `dist`, `build`, `.next`, `out`, and `coverage`.
- generated files.
- lockfile-heavy or vendored directories.

## JS/TS Metrics Policy

Cyclomatic complexity is treated as a shared metric with a language-specific counting policy.

Policy:

- Start at `1` per function-like unit.
- Count runtime decision points such as `if`, loops, `catch`, non-default `switch` cases, ternaries, logical `&&`, logical `||`, nullish coalescing `??`, and comparable branch constructs.
- Count functions, class methods, arrow functions, object methods, React components, and custom hooks as member-level units.
- TypeScript type-only declarations do not add runtime complexity.
- JSX markup does not add complexity by itself, but JavaScript expressions inside JSX do.

Other shared-ish metrics:

- Lines of code: source and executable line counting adapted to JS/TS syntax.
- Halstead volume: token-based with JS/TS token classification.
- Maintainability index: same formula family as the .NET analyzer, calibrated for JS/TS.
- Coupling: import/export and identifier-reference based.
- Depth of inheritance: class `extends` chain where resolvable. Functional React components commonly have depth `0`.

CSV output keeps the existing column shape, with JS/TS-specific meanings:

- `Project`: package name from `package.json` or workspace package name.
- `Namespace`: module path.
- `Type`: class name, React component name, hook name, or file/module bucket.
- `Member`: function, class method, component render function, hook callback, or exported function.

## React Treatment

React is supported from the first JS/TS analyzer version.

React components are detected as:

- class components.
- named functions returning JSX.
- exported JSX-producing functions.
- PascalCase arrow functions returning JSX.

Custom hooks are detected as functions named `useX`.

React-specific findings are mapped into the existing dimensions instead of creating new top-level dimensions. Examples include:

- high component cyclomatic complexity.
- oversized component bodies.
- too many hooks or effects in one component.
- hooks called conditionally.
- async effect callbacks.
- missing effect dependency arrays.
- large JSX blocks.
- weak component test coverage.

## Alternative Approaches Considered

Recommended approach: TypeScript compiler API plus deterministic AST rules.

- Pros: parses JS, JSX, TS, and TSX; supports React awareness; keeps output deterministic; allows semantic checks later.
- Cons: requires owning the rule engine and calibration.

Alternative: ESLint plugin/ruleset.

- Pros: familiar ecosystem and existing parser/rule infrastructure.
- Cons: output parity is messier; package feels like linting rather than code metrics.

Alternative: hybrid wrapper over existing tools.

- Pros: fastest prototype.
- Cons: harder to make deterministic, explainable, and stable across environments.

The standalone TypeScript compiler API approach is the best fit for CodeMetrics.AI.

## Migration Plan

1. Move the current .NET solution, source, and tests into `analyzers/dotnet/`.
2. Update solution and project relative paths, package README inclusion, tests, README links, and GitHub Actions.
3. Add a suite-level root README.
4. Add shared scorecard schema docs and examples.
5. Add the JS/TS analyzer package scaffold.
6. Add Python and Rust next-wave analyzer docs.
7. Verify the .NET test suite still passes after the move.
8. Add JS/TS fixture tests before implementing analyzer behavior.

## Testing Strategy

.NET migration testing:

- Run the existing .NET test suite after the directory move.
- Verify the .NET tool still produces `.scorecard/metrics.csv` and `.scorecard/evidence.json`.
- Verify packaging metadata still includes the correct README and package identity.

JS/TS analyzer testing:

- Parser fixtures for `.js`, `.jsx`, `.ts`, and `.tsx`.
- Fixture projects with `package.json`, `tsconfig.json`, and workspace layouts.
- React component and hook fixtures.
- Cyclomatic complexity policy tests.
- Output contract compatibility tests against `shared/scorecard-schema`.

Shared contract testing:

- Validate analyzer evidence examples against `evidence.schema.v1.json`.
- Keep metrics CSV documentation aligned with generated CSV headers.

## Implementation Notes

The initial NPM package and CLI name is `codemetrics-ai`.

The first JS/TS implementation prioritizes deterministic AST-based metrics and output contract compatibility before deeper semantic rules.
