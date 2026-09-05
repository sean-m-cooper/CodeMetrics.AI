# JavaScript / TypeScript / React Analyzer

The JavaScript / TypeScript analyzer is packaged as the `codemetrics-ai` NPM package and exposed through the `codemetrics-ai` command.

## Location

```text
analyzers/javascript-typescript/
```

## Supported Inputs

- `.js`
- `.jsx`
- `.ts`
- `.tsx`
- `package.json`
- `tsconfig.json`

## Metrics Policy

Cyclomatic complexity starts at `1` per function-like unit and counts runtime decision points such as `if`, loops, `catch`, non-default `switch` cases, ternaries, logical operators, and nullish coalescing.

TypeScript type-only declarations do not add runtime complexity. JSX markup does not add complexity by itself, but expressions inside JSX are analyzed.

## React

React components and custom hooks are first-class analysis targets. React-specific findings are mapped into the stable scorecard dimensions.

The implementation currently scores codeQuality, maintainability, and imported React hook/effect checks. Other dimensions are explicitly skipped. Scores are uncalibrated across ecosystems. See the [analyzer README](../../analyzers/javascript-typescript/README.md) for discovery, metric definitions, exclusions, and comparison options.
