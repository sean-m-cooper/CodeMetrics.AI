# Repository rename verification

GitHub was renamed from `sean-m-cooper/CodeMetrics.AI` to
`sean-m-cooper/CodeTestament` on 2026-10-29 (local date). The repository ID remained
1247701388. Both migration branches were pushed before the rename. The existing
local directory remains `E:/repos/CodeMetrics.AI`; origin now uses
`https://github.com/sean-m-cooper/CodeTestament.git`.

## Verified behavior

- The old GitHub API URL and Git remote resolve to the renamed repository.
- The new remote exposes the pushed migration branch at its expected commit.
- GitHub still lists the `NPM_TOKEN` and `NUGET_API_KEY` secret names. Values were
  neither retrieved nor printed. The publishing workflows use these secrets and
  their existing tag triggers; they have no repository-name-specific OIDC rule.
  Secret presence does not establish validity or permission to publish new names.
- Fresh npm and NuGet archives contain the new repository URLs. Package versions,
  commands, source revision pins and Python preview boundaries are unchanged.
- An isolated Claude profile under `TestResults/repo-rename/plugin-profile`
  installed `codetestament@ai-tools` from the pushed ai_tools migration branch.
  Its initial installed receipt records source commit
  `53f7e28afc04661d7d77187148a42b62b6897ed8`.
- The installed skill completed a fresh Python fixture run using the newly packed
  npm archive: analysis and validation exited zero, run/audit IDs matched, and
  Error Handling remained skipped. Local output is in
  `TestResults/repo-rename/installed-plugin-run.json`.
- The initial full CI run passed all eight analyzer jobs. Calibration failed
  because its frozen baselines still named the legacy tools. A local measured
  run of all 12 .NET/JS fixtures established that **only tool name and version**
  changed. Corpus hashes, rulesets, populations, scores, findings, accuracy counts
  and distributions are exactly equal to the previous baselines. Updating these
  four identity values preserves strict comparison; no alias or relaxed gate was
  introduced. The measured report is `TestResults/repo-rename/calibration.json`.

## Review and release boundary

The migration is on [CodeTestament PR 17](https://github.com/sean-m-cooper/CodeTestament/pull/17)
and the reference is on [ai_tools PR 20](https://github.com/sean-m-cooper/ai_tools/pull/20).
Consult their latest checks for CI after the calibration identity correction.
Default-branch marketplace discovery requires merging these changes. Branch-based
installation is covered above; no regular user plugin settings were changed.

No release tag was created and no package was published during repository rename
verification. New package publishing permissions and actual registry installation
remain release checks. Public skill defaults remain on verified legacy packages.
Historical validation files and protocol/schema identifiers retain their original
identities. See [the migration guide](codetestament-migration.md) for next steps.
