# First CodeTestament packages

Published 2026-10-30 from `2205e9c562ef45cf893f3b455fec3c98588536f0`:

| Distribution | Version | Release tag |
| --- | --- | --- |
| NuGet `CodeTestament.Scorecard` | 2.4.0 | `v2.4.0` |
| npm `codetestament` | 0.6.0-preview.3 | `js-v0.6.0-preview.3` |

The new commands are `codetestament-dotnet`, `codetestament`,
`codetestament-evidence`, `codetestament-python`, and
`codetestament-python-collect`. They coexist with old package commands.
Use the exact npm preview version; the existing stable npm release remains
`codemetrics-ai@0.5.0`.

Plugin 0.2.0 selects these published versions by default and honors supported
exact legacy repository pins. The Python adapter remains 0.15.0: Method
Complexity and Async/Blocking Usage are the only scored dimensions, and Error
Handling stays unscored. No scoring policies changed for this brand release.

CMAI codes, annotations, schema/ruleset identifiers, `.scorecard` paths and
historical evidence remain unchanged. A producer identity/version change is
incompatible with an ordinary baseline gate. Preserve old evidence, compare
paired runs on the same source, then adopt a fresh baseline. See the
[migration guide](../codetestament-migration.md).

The npm lockfile updates transitive `fast-uri` to 3.1.9 for the fixed production
advisories. Production dependency audit reports zero advisories; this does not
claim the development dependency tree is advisory-free.

## Verification

Both publication workflows passed. Clean registry installations and a default
mixed .NET/JS/Python skill audit passed on Windows with no package selectors or
local archives. The Python preview fixture also preserved its documented,
offloaded and unresolved cases and unscored Error Handling. Integration CI runs
the same registry checks on Windows and Linux, alongside packaged default and
legacy-pin compatibility tests. See the
[validation receipt](../../skills/code-scorecard/evals/validation/2026-10-30-published-defaults.md)
for executed evidence and limitations.
