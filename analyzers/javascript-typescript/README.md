# codemetrics-ai

NPM analyzer for JavaScript, TypeScript, and React projects.

## Usage

```bash
npx codemetrics-ai
npx codemetrics-ai --project ./package.json
npx codemetrics-ai --tsconfig ./tsconfig.json
npx codemetrics-ai --output .scorecard/javascript-typescript/metrics.csv --scorecard-output .scorecard/javascript-typescript/evidence.json
```

## Outputs

| File | Description |
|------|-------------|
| `.scorecard/javascript-typescript/metrics.csv` | Shared metrics CSV |
| `.scorecard/javascript-typescript/evidence.json` | Scorecard evidence using schema version 2 |

## Development

```bash
npm install
npm run build
npm test
```
