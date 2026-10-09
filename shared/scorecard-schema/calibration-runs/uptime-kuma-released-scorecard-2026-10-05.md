# Uptime Kuma server: released 0.4.0 scorecard

**Overall 6.7/10 — partial assessment, 3/9 dimensions scored.** Source-only server
selection at `e7420f8fa546a8baa7ac4bf9d10a32545d4346e0`. The Vue client and other
packages are outside this selection. npm 0.4.0 and the merged scorecard skill ran
successfully; this is not a runtime-performance, full security, or test-quality
assessment.

| Dimension | Score | Source and scope | Evidence |
|---|---:|---|---|
| Architecture & SOLID | N/A | Unscored module evidence | 213 modules; 669/975 literal references resolved, 117 out of scope and 306 unresolved. Coverage has gaps. |
| Method Complexity | 5.5 | Deterministic, partial owned-function CC | 1,142 functions; worst `start/beat` CC 126. One worst receives 40%, remaining mean 60%. |
| Testing | N/A | Unsupported | No deterministic test or coverage assessment. |
| Security | N/A | Unsupported | No deterministic security assessment. |
| Error Handling | N/A | Unscored handler evidence | 341 distinct bodies: 8 unexplained empty, 3 documented empty, 330 containing code; zero recognized callback-resolution gaps. Code presence is not proof of correct recovery. |
| Documentation | N/A | Unsupported | No deterministic documentation assessment. |
| Dependency Management | N/A | Unsupported | No package health/compatibility assessment. |
| Async/Blocking Usage | 10 | Deterministic, partial bounded usage | 0/524 affected eligible owners. General Promise flow, concurrency safety, blocking-I/O completeness, and runtime speed are not assessed. |
| Maintainability | 4.7 | Deterministic, partial owned-function MI | Weakest 229 functions receive 40%; remaining 913 receive 60%, without overlap. |
| **Overall** | **6.7** | **Partial assessment, 3/9 scored** | `(5.5 + 10 + 4.7) / 3`; the six unavailable dimensions and decomposition preview are excluded. |

Decomposition remains unscored. Twenty-three functions exceed 40 owned statements;
all have MI below 65 and twenty also have CC above 10. The size preview is not an
additional deduction. Server/session initialization and registration examples
require responsibility review rather than automatic splitting.

## Evidence

- Tool `codemetrics-ai` **0.4.0**, schema **3**, variant `source`; pin source
  `compatibility-manifest`; calibration `uncalibrated` across ecosystems.
- Ruleset `javascript-typescript-2026-10-04-local-handlers`.
- Current audit ID `2d551c48-2013-49d1-9393-8a56b6aa33d8`.
- Current run ID `e7676f66-3550-4c82-aac1-079ae9bcc02e`.
- Baseline audit ID `1ad6d5d6-d326-4f32-9156-2a3212ee865b`; run ID
  `c0c59e8a-0909-4bcd-bf3b-6403dddc15ab`.
- Configuration fingerprint
  `e0741fecbd03437726a961f9163a684e97e8d23fc4e93ba03b91c78462e2b1da`.
- 214 candidate files, 213 analyzed; one declaration file excluded; 1,142 members.
- Fresh, complete, usable; analyzer, validator, and comparison exit **0**;
  diagnostics and suppression declarations empty.
- Current artifacts:
  `E:/repos/uptime-kuma/.scorecard/javascript-typescript/runs/e7676f66-3550-4c82-aac1-079ae9bcc02e/`
  (`evidence.json`, `inspection.json`, `metrics.csv`, `comparison.json`).
- The compatible repeat has no new/resolved/changed findings and no score drops.
  Source, package scripts, and dependency installation were not changed or run.

Zero recognized callback gaps does not establish comprehensive callback coverage:
custom APIs and unresolved external types are outside the bounded detector. The
graph gaps likewise limit architectural conclusions. Separate scope selection
from findings: 117 out-of-scope references are not 117 missing dependencies.

## Three review priorities

1. **Monitor validation (`server/model/monitor.js:1630`, CC 50).**
   Basis: Metrics + current source. The function combines general interval checks,
   repeated JSON validation, and monitor-specific checks. Review extracting
   coherent validation groups, starting with characterization tests for invalid
   inputs and the ordering/text of the first reported error. This is a supported
   refactoring candidate, not a claim of incorrect validation.

2. **Heartbeat orchestration (`server/model/monitor.js:431`, `start/beat`, CC 126,
   own MI 0, 289 statements).** Basis: Metrics + current source. The callback owns
   monitor dispatch, heartbeat state, and failure/retry handling. Map its state
   transitions and externally visible effects before deciding which protocol
   operations can move behind existing monitor-type boundaries. Preserve execution
   order, retry behavior, and scheduling. The source review establishes mixed work,
   not a tested refactoring design or safe parallelism.

3. **Socket-disconnection failure contract
   (`server/uptime-kuma-server.js:586`).** Basis: Metrics + current source. A function
   documented for password resets catches failures from refresh/disconnect and
   continues. Inspect how session revocation and remaining clients are handled,
   and decide whether the failure needs reporting. This is an unresolved review
   candidate, not a confirmed security vulnerability or an Error Handling penalty.

Two current handler observations illustrate why raw empty counts are insufficient:
`general-socket-handler.js:143` is followed by an explicit failed callback result;
`maintenance.js:278` has an explanation for an optional-date fallback before the
try. These source contexts were checked this run. Other silent paths remain open
questions; the report does not infer developer intent where none is documented.
