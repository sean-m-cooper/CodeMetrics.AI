import { copyFileSync, mkdirSync } from 'node:fs';

// One canonical runtime, copied into the package. No repository-relative runtime imports.
const source = new URL('../../../shared/source-facts/', import.meta.url);
const destination = new URL('../dist/shared-source-facts/', import.meta.url);
mkdirSync(destination, { recursive: true });
for (const name of ['arithmetic', 'contract', 'population']) {
  for (const extension of ['mjs', 'd.mts']) {
    const file = `${name}.${extension}`;
    copyFileSync(new URL(file, source), new URL(file, destination));
  }
}

const python = new URL('../dist/python/', import.meta.url);
mkdirSync(python, { recursive: true });
for (const file of ['async_adapters.py', 'ASYNC_ADAPTERS.md', 'async_context.py', 'async_provenance.py', 'ASYNC_CONTEXT.md', 'async_usage.py', 'async_bindings.py', 'ASYNC_USAGE.md', 'source_facts.py', 'source_selection.py', 'duplication.py', 'analysis.py', 'decomposition.py', 'DECOMPOSITION.md', 'error_handling.py', 'ERROR_HANDLING.md', 'error_handling_context.py', 'ERROR_HANDLING_CONTEXT.md', 'error_handling_transfers.py', 'ERROR_HANDLING_TRANSFERS.md', 'error_handling_operations.py', 'ERROR_HANDLING_OPERATIONS.md', 'METHOD_COMPLEXITY.md', 'coverage_review.py',
  'coverage_provenance.py', 'coverage_collect.py', 'coverage_worker.py', 'README.md', 'DUPLICATION.md', 'COVERAGE.md'])
  copyFileSync(new URL(`../../python/${file}`, import.meta.url), new URL(file, python));
