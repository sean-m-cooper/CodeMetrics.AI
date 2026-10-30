# CodeScorecard candidate review

Reviewed: 2026-10-29

## Decision status

The user selected CodeScorecard for further review as a replacement for
CodeMetrics.AI. It is the preferred candidate, not a cleared or adopted public
brand. This document records the research and a proposed migration approach;
it does not authorize purchases, trademark filings, contacting other parties,
or publication under the new name.

The name describes the product well: explainable code-quality assessment for
developers and AI agents. It matches the existing code-scorecard skill and
does not imply a proof of correctness or measured runtime performance.

## Verified findings

### US trademark search

The official USPTO search interface returned no results for each of these
searches, with live and dead records included and no individual class filter:

- Wordmark: `CodeScorecard`
- Field tag search: `CM:codescorecard OR CM:"code scorecard" OR CM:"code score card"`
- Field tag search: `CM:(/.*code.*/ AND /.*scorecard.*/)`

These results do not establish clearance. They do not cover every similar
spelling, pronunciation, unregistered use, state registration, or jurisdiction.

The official TSDR record for **SCORECARD** shows:

| Field | Observed value |
| --- | --- |
| Owner | SCORECARD AI, INC. |
| US serial number | 98838742 |
| Registration number | 8189934 |
| Registration date | March 24, 2026 |
| Status | LIVE/REGISTRATION/Issued and Active |
| Register | Supplemental |
| International class | 042 |

Its listed services include SaaS for AI evaluation, software testing and
development, and scoring language models and their workflows. The registration
is a material similarity-review lead. This review makes no determination about
infringement, registrability, the effect of the Supplemental Register, or
whether adding "Code" provides sufficient distinction.

### Related products

- **Scorecard CLI** describes local code-quality gates using build, test, lint,
  coverage, dependency, and secret checks, with agent integration. Its product
  function and audience are particularly close to ours, despite the different
  full name.
- **OpenSSF Scorecard** assesses repository security practices. There is
  adjacent audience and terminology overlap.
- **Scorecard** at scorecard.io offers AI-agent evaluation and simulation.
  Its site independently confirms activity in an adjacent evaluation market.

Similarity and discoverability concerns are product judgments, not findings
that any of these organizations can prevent our use of CodeScorecard.

### Package, account, and domain checks

Read-only public API checks performed on the review date:

| Check | Result |
| --- | --- |
| npm `codescorecard`, `code-scorecard` | Both exact package endpoints returned HTTP 404 |
| PyPI `codescorecard`, `code-scorecard` | Both exact package endpoints returned HTTP 404 |
| NuGet search `codescorecard`, including prereleases | Zero results |
| GitHub repository search `codescorecard in:name` | Zero results |
| GitHub account `codescorecard` | HTTP 404 |
| Verisign RDAP `codescorecard.com` | HTTP 404 |
| Google Registry RDAP `codescorecard.dev` | HTTP 404 |

These are observations, not reservations or guarantees of namespace availability.
Domain registration eligibility and price still require registrar confirmation.
Earlier broader GitHub searches found related scorecard projects; the joined-name
result must not be interpreted as absence of similar projects.

WIPO's Global Brand Database returned an access challenge, so its trademark
records were not searched. Other national/state registers and a comprehensive
phonetic/common-law search remain outside this completed review.

## Recommendation and review brief

Keep CodeScorecard as the preferred candidate, but obtain a targeted trademark
professional's assessment before investing in public adoption. The new SCORECARD
registration finding means the earlier favorable exact-name screen is not enough
to recommend a public rename by itself.

Brief for that review (prepared only; not sent):

> We maintain CodeMetrics.AI, a developer tool producing deterministic,
> evidence-backed code-quality assessments for .NET, JavaScript/TypeScript,
> and a Python preview. It is distributed through NuGet/npm and used by a
> code-scorecard agent skill. We are considering CodeScorecard as its new name.
> Please assess use and registration of that name for downloadable developer
> software and any future hosted assessment service, including similarity to
> SCORECARD, US serial 98838742 / registration 8189934, the Scorecard CLI at
> scorecardcli.com, and OpenSSF Scorecard. Please evaluate distinctiveness,
> related goods/services, and relevant unregistered uses, and advise which
> jurisdictions should be searched for our intended distribution.

## Proposed migration approach if adopted

Current packaging was verified in the local manifests: NuGet `CodeMetrics.AI`
2.3.1 exposes `code-metrics`; npm `codemetrics-ai` 0.6.0-preview.2 exposes
`codemetrics-ai`, `codemetrics-evidence`, `codemetrics-python`, and
`codemetrics-python-collect`.

1. Confirm registrability/use advice, registrar availability, and package
   namespace eligibility. Select final package and command names together;
   .NET and npm global command names must not collide on machines using both.
2. Update display branding, package metadata, repository links, help, and
   installation documentation as one reviewed release. Keep the existing
   `code-scorecard` skill name unless a separate reason warrants changing it.
3. Provide an explicit transition from existing packages and commands. Preserve
   released packages, historical evidence identities, ruleset identifiers,
   diagnostic codes such as CMAI, run/audit IDs, and baseline comparability.
   Do not apply an indiscriminate repository-wide text replacement.
4. Update the companion skill's compatibility manifest and installation paths
   alongside the analyzer release. Document legacy-name handling explicitly.
5. Validate fresh installations and upgrades, mixed .NET/npm installations,
   CLI help, packaged catalogs and schemas, evidence validation, and skill
   discovery. Keep Python Error Handling unscored and preserve preview labeling.

This is a proposed sequence, not an implemented migration or release plan with
confirmed new identifiers.

## Sources / References

- [USPTO trademark search](https://tmsearch.uspto.gov/) - interactive queries recorded above.
- [Official SCORECARD TSDR record](https://tsdr.uspto.gov/#caseNumber=98838742&caseSearchType=US_APPLICATION&caseType=DEFAULT&searchType=statusSearch) - status, register, owner, and services verified in the browser on the review date.
- [USPTO search instructions](https://www.uspto.gov/trademarks/search/federal-trademark-searching) - combined-mark fields, exact wording, and expanded searches.
- [USPTO comprehensive clearance guidance](https://www.uspto.gov/trademarks/search/comprehensive-clearance-search-similar-trademarks) - scope beyond exact matches.
- [Scorecard CLI](https://scorecardcli.com/)
- [OpenSSF Scorecard](https://scorecard.dev/)
- [Scorecard AI product](https://www.scorecard.io/)
- [WIPO Global Brand Database](https://branddb.wipo.int/) - access challenge; records not searched.
- [npm exact-name endpoint](https://registry.npmjs.org/codescorecard)
- [npm hyphenated-name endpoint](https://registry.npmjs.org/code-scorecard)
- [NuGet search endpoint](https://azuresearch-usnc.nuget.org/query?q=codescorecard&prerelease=true&take=10)
- [PyPI exact-name endpoint](https://pypi.org/pypi/codescorecard/json)
- [PyPI hyphenated-name endpoint](https://pypi.org/pypi/code-scorecard/json)
- [GitHub repository search endpoint](https://api.github.com/search/repositories?q=codescorecard+in:name&per_page=10)
- [GitHub account endpoint](https://api.github.com/users/codescorecard)
- [Verisign .com RDAP endpoint](https://rdap.verisign.com/com/v1/domain/codescorecard.com)
- [Google .dev RDAP endpoint](https://pubapi.registry.google/rdap/domain/codescorecard.dev)
- Local manifests: `analyzers/javascript-typescript/package.json` and
  `analyzers/dotnet/src/CodeMetrics.AI/CodeMetrics.AI.csproj`.
