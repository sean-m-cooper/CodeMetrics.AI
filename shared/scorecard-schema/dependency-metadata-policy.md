# Dependency candidate and large-package metadata policy

The unpublished `dotnet-2026-09-15-package-metadata` ruleset changes observation classification and metadata acquisition. Numerical score ladders remain unchanged. Older rulesets are not compatible baseline gates.

## No reported upgrade candidate

The validated NuGet stable-only outdated query can return the literal `Not found at the sources` in latestVersion for unlisted or prerelease packages. This sentinel is not a version and supplies no upgrade candidate whose framework compatibility can be assessed. It remains visible under CMAI7004 as informational `excludedNoReportedCandidate`, with `frameworkCompatibility: notApplicable` and `candidateSelection: noReportedStableCandidate`. The raw latestVersion text, installed version and project/TFM rows remain intact. This is not an assertion that the installed package is current, supported, unlisted or safe.

Compatibility reports count these observations separately in `notApplicableObservations` and `noReportedCandidates`. They are not inserted as compatible results and never increment the outdated score input. Genuine missing, malformed or unknown versions remain unavailable; failed commands or error-bearing JSON cannot establish this disposition. Vulnerability and deprecation assessments remain active and independently affect their existing score inputs. Mixed assessments with unknown real candidates still fail.

The query remains stable-only. This change does not automatically propose prerelease upgrades or certify their compatibility. A prerelease or unlisted package may need a migration review even when no stable candidate is returned. In text-only historical reports the entire exact sentinel is preserved instead of interpreting its final word as a version.

## Large-package metadata

Normal downloads retain their 50 MiB limit. Above that size, a strong ETag (including the bounded legacy Azure unquoted hexadecimal form) and a server supporting exact byte ranges allow inspection of ZIP metadata without downloading bundled tool payloads. Four bounded range requests read the end record, central directory, manifest header, and manifest content. Every range must return HTTP 206, the exact requested Content-Range and total length, the same strong ETag, and no content encoding. If-Match prevents mixing package revisions. Ignored ranges, changed/missing validators, truncated/oversized responses and cancellation do not produce compatibility results.

Limits are 65,557 bytes for the ZIP tail, 8 MiB for the central directory, 1 MiB for compressed manifest content and 1 MiB for expanded manifest content. Manifest name, compression flags/method, archive bounds, declared length and CRC32 are validated. XML is character-bounded with DTDs and external resolution prohibited. Only one root nuspec is accepted. Classic single-disk ZIP is supported; ZIP64, split/encrypted manifests, unsupported compression and malformed metadata stay unavailable. Existing per-request and whole-assessment deadlines and concurrency limits remain in effect. No files are extracted or executed. Hosts without range support do not trigger an unlimited download fallback.

The same asset/manifest inspector determines framework compatibility for both paths. Metadata compatibility does not establish runtime, tool-host or upgrade safety. Failed inspection remains missing evidence, never a code defect or an automatic score.
