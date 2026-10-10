# JS/TS npm and pnpm dependency observations, development v1

Ruleset: `javascript-typescript-2026-10-09-dependency-evidence`.
Scope: `javascript-typescript/dependencyManagement/lock-observations-v1`.
This is an unreleased extension to the 0.5.0 codebase. Schema v3 is unchanged.
Fresh baselines are required; other dimension policies and raw metrics are unchanged.

## Product boundary

Dependency Management remains **unscored**. Inventory, deprecation, update, and
advisory observations are evidence for review. No numerical ladder has been
approved. Findings are informational and add no score deductions or warning/error
gate failures. An explicit informational gate can still select these findings.
Security remains outside this implementation; an advisory is recorded once under
Dependency Management, not duplicated into a second scored dimension.

A locked or newer version does not establish neglect, upgrade safety, runtime
reachability, or correctness. An absent observation does not establish safety.
Exact pins, prereleases, optional peers, and overrides are legitimate inputs.

## Local inventory

Every analysis reads selected package manifests and the nearest enclosing supported
lockfile within the repository. The nearest declared `packageManager` selects npm
or pnpm. An unsupported declared manager, a nearer unsupported lock, or ambiguous
npm/pnpm locks without a declared manager remains a gap. `npm-shrinkwrap.json`
takes precedence over `package-lock.json` at the same level. npm lockfile versions
2 and 3 with a `packages` map and pnpm lockfile version 9 are supported.
Manifest declarations remain visible when resolved inventory is unavailable.

Traversal begins with each selected manifest's dependencies, development
dependencies, optional dependencies, and peer dependencies. Optional declarations
override duplicate ordinary declarations. Resolved package entries are located
through their nested/hoisted `node_modules` paths. Repository-local workspace
links resolve through the lockfile and their existing manifests. Unrelated,
unreachable lock entries are excluded. Transitive development dependencies are
not traversed; a development root's runtime closure retains its development use.

Package name, resolved version, and source class define the grouping identity.
Each group retains installation locations and uses: selected manifest/project,
requesting lock location, requested name/range, root dependency kind, directness,
and optional path context. Aliases retain the requested and actual names.
Production/development closures can overlap; their counts must not be summed.
Source classes distinguish public npm registry, workspace, and other/unknown
sources. Grouping does not establish byte-identical contents for non-public sources.

Manifest/lock declaration differences, missing required references, unsupported
formats, invalid JSON, missing/non-semver versions, unsafe paths, and inaccessible
workspace targets produce gaps. Absent optional dependencies/peers are notices.
A locked version outside a requested range is retained with a notice: overrides
or peer-resolution policy may explain it. Replacement package identities are
likewise retained with their original requested name and a notice; a range for
the original name cannot establish a candidate for its replacement. Declared overrides are exposed, but the
analyzer does not reimplement or certify npm's override/installation policy.

`inventory.status: complete` means the supported reachable lockfile inventory was
read successfully. It does not verify `node_modules`, platform applicability,
peer compatibility, installability, or runtime behavior. No target installation,
lockfile regeneration, or lifecycle script runs. Inputs are protected from CLI
output collisions. Input files are limited to 20 MiB; traversal is bounded.

## pnpm v9 scope

The adapter reads the project `importers`, `packages`, and `snapshots` maps. It
accepts one project YAML document or an environment document followed by the
project document, as used by pnpm 12. It records the one-based `projectDocument`
index and explicitly marks the environment document excluded. Package-manager
binaries and config dependencies in that environment document are outside the
selected-project inventory; this is not a whole-lockfile vulnerability audit.
Ambiguous document layouts, duplicate YAML keys, custom tags, and aliases are
rejected. Older pnpm formats remain unsupported rather than being guessed.

Each resolved reference must have its exact snapshot and package record. Peer
and patch suffixes remain in snapshot locations; they do not multiply the
package/version/source group. Direct npm aliases preserve requested and actual
names. `link:` references use existing repository-local manifests and importers;
linked development dependencies are not traversed. File, git, custom registry
identities, and injected file-based workspace snapshots are explicit gaps.

`pnpm-workspace.yaml` supplies default/named catalogs and overrides. Catalog
uses retain their original `range` and an additional `resolvedRange`, checked
against the catalog lock record or an explicitly matching catalog override.
Simple exact-name overrides that pnpm expands into importer specifiers are
recognized when current configuration matches the recorded override. Complex
selectors or hook-transformed declarations that cannot be reconciled remain
coverage gaps; the analyzer never executes pnpm hooks or certifies installability.
The workspace configuration is protected from output collisions and hashed in
`configurationInputs`. This adapter extends dependency inventory for selected
packages; source selection still follows the analyzer's existing discovery rules.

An integrity-only pnpm package record provides a locked identity but not registry
provenance. It is retained as `otherSource`; current npm configuration cannot prove
where the historical artifact came from. Only explicit public npm tarball URLs
qualify for optional registry queries. A complete local inventory can therefore
coexist with unavailable metadata. Catalog-derived semver ranges are usable for
candidate lookup only when registry provenance is established.

## Optional registry observations

`--dependency-metadata` opts into public npm registry GET requests and the bulk
advisory endpoint. Offline runs record `notRequested`. Only records whose locked
artifact URL uses HTTPS on `registry.npmjs.org` without URL credentials qualify.
Other/private-source URLs, workspace packages, and unknown provenance are not
queried. Repository npm configuration and authentication tokens are not read.
This is not an authenticated private-package audit.

The default limit is 100 distinct package names (direct uses first, then lexical
order within each group), four concurrent metadata requests,
a 60-second scheduling budget, ten seconds per request, and 5 MiB per response.
Budget omissions, failed HTTP/TLS requests, redirects, invalid responses, and
missing installed-version metadata stay unavailable. Limits never trigger package
installation or disabled TLS verification. A request already started can finish
after the scheduling deadline, within its own timeout. Each run queries afresh;
timestamps and response hashes identify observations rather than implying that
registry data is deterministic across dates.

For each eligible package/version, preserve:

- Installed-version deprecation text and Node engine declaration.
- The valid `latest` dist-tag, or null when unavailable. It need not be a safe or
  suitable upgrade.
- For each direct use, the highest published version satisfying its declared
  semver range (`wanted`), whether it is newer, and whether `latest` falls outside
  that range. Prerelease handling follows pinned node-semver semantics. Exact
  pins do not become outdated deductions. Transitive upgrade recommendations are
  not inferred from a root manifest.
- Bulk-feed advisories matching the locked version, deduplicated by advisory ID
  within the package/version group. Preserve severity, affected range, and source
  URL; reachability and exploitability are explicitly unassessed. Advisory range
  matching includes prereleases; this is a reported exposure signal for review.

Deprecation/update metadata and advisory assessment have independent availability.
One can remain useful when the other fails. Overall metadata is `available` only
when both are available for every recorded group, `partial` when some assessment
is available, and `unavailable` otherwise. A valid empty advisory response means
no advisory was reported for the submitted versions at that time. Failed, absent,
or malformed responses never mean zero vulnerabilities. Null candidate fields
also do not certify that a version is current.

## Consumer and calibration contract

Read `dependencyInventory`, `dependencyMetadata`, dimension status, and scope.
Report gaps and notices distinctly. Keep package/version groups separate from
locations, workspace use counts, and advisory counts. Omit this dimension from
the overall denominator. Historical evidence without these fields remains
unsupported. Offline and metadata-enabled runs have different configuration
fingerprints and cannot share a comparison baseline.
API callers must pass `dependencyMetadata: true` to `analyze` before calling
`enrichDependencies`; this preserves the same opt-in and fingerprint boundary.

The public corpus runner compares every other dimension and raw CSV with the
saved 0.5.0 results. Its optional live-feed sample limits queries to 25 distinct
names and a 30-second scheduling budget per selection; it is not a complete
vulnerability audit. Fixture tests separately cover successful and failed feeds,
intentional pins, prereleases, aliases, private sources, workspaces, duplicates,
optional peers, and overrides. A future scoring proposal needs representative
supported inventories and labeled outcomes; this pass does not authorize one.

References: [npm lockfile structure](https://docs.npmjs.com/cli/v11/configuring-npm/package-lock-json/),
[npm audit protocol](https://docs.npmjs.com/cli/v11/commands/npm-audit/),
[npm outdated terminology](https://docs.npmjs.com/cli/v11/commands/npm-outdated/),
[node-semver](https://github.com/npm/node-semver),
[pnpm v9 format](https://github.com/pnpm/spec/blob/master/lockfile/9.0.md),
and [pnpm multi-document lockfiles](https://github.com/pnpm/pnpm.io/blob/main/docs/lockfile.md).
