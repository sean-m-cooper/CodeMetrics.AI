# Python preview: codemetrics-ai 0.6.0-preview.2

This opt-in npm preview adds Python adapter 0.15.0 to CodeMetrics.AI. It scores
Method Complexity and Async/Blocking Usage and retains the supporting source
evidence for review. **Python Error Handling remains unscored.** The .NET tool
stays at 2.3.1. The npm `latest` tag stays on stable 0.5.0; use the exact preview
version or npm's `preview` tag explicitly.

This corrects the first preview's CLI help, which still described one scored
dimension. The help now advertises both supported dimensions, their compatible
comparison support, and unscored Error Handling. Python adapter version, scoring
policies, and evidence remain unchanged from 0.6.0-preview.1.

## Get started

Install Node.js 20+ and Python 3.12. From the target repository:

```sh
npm exec --yes --package=codemetrics-ai@0.6.0-preview.2 -- codemetrics-python --project ./pyproject.toml --scorecard-output ./python-evidence.json
npm exec --yes --package=codemetrics-ai@0.6.0-preview.2 -- codemetrics-evidence --input ./python-evidence.json --inspect-output ./python-inspection.json
```

A directory or `.py` file is also a supported entry point. Output files must be
new; choose fresh filenames for subsequent runs. Windows defaults to `py -3.12`;
other platforms use `python3`. Use `--python` with an absolute Python 3.12 path
when necessary. Ordinary analysis does not install, import, or execute target
code. The separate coverage collector explicitly runs tests and requires their
dependencies and coverage.py in a caller-supplied environment.

The updated [code-scorecard skill](https://github.com/sean-m-cooper/ai_tools/tree/main/skills/code-scorecard)
installs the exact preview into an isolated cache, generates fresh evidence, and
validates its run/audit identity before reporting. It no longer requires a local
archive for Python. There is no separate PyPI distribution.

## What the scores mean

- Method Complexity scores selected production implementations under the
  documented worst-function/population policy.
- Async/Blocking Usage scores the fraction of eligible production async owners
  with recognized eager blocking patterns. Repeated paths do not count an owner
  more than once. Supported documented choices and callback/deferred contexts
  retain their recorded exclusions.
- Unresolved calls stay visible. An async score of 10 is not assurance that every
  call is nonblocking, nor a measurement of latency or throughput.
- A Python aggregate is a **partial assessment, at most 2/9 dimensions scored**.
  Without eligible async owners, async is unavailable rather than 10. Error
  Handling, decomposition, duplication, and coverage/CRAP observations remain
  unscored; do not fill unavailable dimensions with invented grades.

The package includes Python guides and the async scoring policy in `dist/python`.
Historical Python evidence retains its original contract. Generate new baselines
when changing adapter versions or rulesets; policy changes are not source
improvements.

## Other contents and compatibility

The same npm preview includes unscored npm v2/v3 and pnpm v9 dependency inventory
for JS/TS. Optional `--dependency-metadata` requests bounded public registry and
advisory observations; it is never enabled implicitly. JS/TS now uses ruleset
`javascript-typescript-2026-10-09-dependency-evidence`, requiring fresh baselines.
Existing exact project pins remain respected by the skill. JS/TS Error Handling
scoring is unchanged; the unscored restriction above applies to Python.

## Feedback

We welcome early-adopter feedback on useful findings, false positives, missed
patterns, and setup failures. Include tool/adapter versions, the selected scope,
run status, and a small shareable reproduction when possible. Review evidence
before sharing it: paths, symbol names, and source observations can contain
private project information. Avoid sending secrets or proprietary source.

Validation includes Python unit and installed-package tests, shared evidence
contracts, and pinned selections from Requests, Starlette, HTTPie, and Django.
The [packaging validation record](../../shared/scorecard-schema/calibration-runs/python-async-scoring-package-2026-10-28.md)
records those results and their limits. Release verification additionally checks
the published artifact, clean-cache skill installation, and a generated report.
