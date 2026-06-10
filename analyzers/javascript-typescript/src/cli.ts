#!/usr/bin/env node

import { defaultEvidencePath, defaultMetricsPath } from "./scorecard-contract.js";

const args = new Set(process.argv.slice(2));

if (args.has("--help") || args.has("-h")) {
  console.log(`codemetrics-ai

Deterministic code metrics and scorecard evidence for JavaScript, TypeScript, and React projects.

Usage:
  codemetrics-ai [options]

Options:
  --project <path>            Path to package.json
  --tsconfig <path>           Path to tsconfig.json
  --output <path>             Metrics CSV output path (default: ${defaultMetricsPath})
  --scorecard-output <path>   Evidence JSON output path (default: ${defaultEvidencePath})
  -h, --help                  Show help
`);
  process.exit(0);
}

console.log("codemetrics-ai JS/TS analyzer scaffold");
console.log(`Metrics output: ${defaultMetricsPath}`);
console.log(`Evidence output: ${defaultEvidencePath}`);
