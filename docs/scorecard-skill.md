# CodeTestament scorecard skill

`skills/code-scorecard` is the canonical skill. Its instructions, compatibility
records, helper scripts, tests, evaluation fixtures and optional MSBuild tooling
are maintained with the analyzers. The `ai_tools` marketplace references this
repository at an exact commit; it no longer bundles a second implementation.

## Install or test

For a local Claude Code test from this checkout:

```sh
claude --plugin-dir .
```

Invoke `/codetestament:code-scorecard`. The unqualified skill name stays
`code-scorecard`. Other agents can load `skills/code-scorecard/SKILL.md` directly;
when copying it into a host's skill directory, copy the **whole folder**, including
scripts, compatibility data and supporting documents.

After these commits are pushed, install through the existing ai_tools marketplace:

```text
/plugin marketplace add sean-m-cooper/ai_tools
/plugin install codetestament@ai-tools
/reload-plugins
```

Or install directly from this repository:

```text
/plugin marketplace add sean-m-cooper/CodeMetrics.AI
/plugin install codetestament@codetestament
/reload-plugins
```

The current GitHub name is still `CodeMetrics.AI`. Do not substitute the proposed
repository rename until it exists. These remote installation paths require the
plugin commit and marketplace reference to be published. Local verification does
not establish that a remote installation works.

For Codex, add the repository with `codex plugin marketplace add
sean-m-cooper/CodeMetrics.AI`, then select CodeTestament from that marketplace in
the desktop app. The root portable `plugin.json` and Claude compatibility manifest
describe the same skill. No MCP server, credentials or install hooks are required.

Existing installations of `ai-tools` do not automatically install a separate
plugin. Install `codetestament`, update the `ai-tools` plugin, and reload the host.
The new Claude invocation is `/codetestament:code-scorecard`; replace saved
`/ai-tools:code-scorecard` invocations. Existing analyzer pins, `.scorecard`
artifacts, CMAI annotations and historical evidence stay valid under the same
compatibility checks. No automatic user configuration or cache migration occurs.

## Package and release contract

The plugin's `0.1.0` version is independent of analyzer versions. The compatibility
manifest still selects the verified public legacy defaults. New CodeTestament
packages require their exact selection and local archives until registry
publication is verified. See [bootstrap](../skills/code-scorecard/bootstrap.md).
Python remains a preview with at most two scored dimensions; Error Handling
remains unscored.

Make skill changes here. Run `node scripts/verify-scorecard-plugin.mjs` after
staging new skill files: it exports only tracked plugin files to an isolated
directory, checks manifest parity and runs the offline tests there. The
`code-scorecard-integration.yml` workflow tests both current checkout packages and
the pinned legacy packages on Windows and Linux. The historical legacy source pin
is a compatibility fixture, not the location of current development.

For releases, commit and push this repository first, verify CI and installation,
then update the `sha` in the ai_tools marketplace entry. The reference must point
to a commit containing the plugin manifest and complete skill. Never maintain a
parallel skill implementation there. Change analyzer defaults only after the
corresponding registry packages pass clean-cache verification.

The imported skill and [scorecard guide](code-scorecard.md) retain
[CC BY 4.0](../skills/code-scorecard/LICENSE) and their
[provenance notice](../skills/code-scorecard/NOTICE.md). Analyzer code retains the
root MIT license. Historical eval records keep their original paths and dates.

Packaging references: [Claude marketplace source formats](https://code.claude.com/docs/en/plugins/marketplace-reference)
and [OpenAI plugin packaging](https://developers.openai.com/plugins/build/plugins).
