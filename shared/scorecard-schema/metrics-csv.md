# Metrics CSV Contract

Default path (see `ecosystems.md` for the ecosystem id rule):

```text
.scorecard/<ecosystem-id>/metrics.csv
```

Header:

```text
Scope,Project,Namespace,Type,Member,Maintainability Index,Cyclomatic Complexity,Depth of Inheritance,Class Coupling,Lines of Source code,Lines of Executable code
```

## Column Meanings

| Column | Meaning |
|--------|---------|
| Scope | `Type` or `Member` |
| Project | Analyzer-specific project or package name |
| Namespace | Namespace, module path, or package path |
| Type | Type, component, hook, module bucket, or equivalent analyzer unit |
| Member | Method, function, component body, hook callback, or equivalent member unit |
| Maintainability Index | 0-100 maintainability index |
| Cyclomatic Complexity | Runtime decision complexity |
| Depth of Inheritance | Inheritance depth where meaningful, otherwise 0 |
| Class Coupling | Analyzer-specific coupling count |
| Lines of Source code | Non-blank source lines excluding comments and braces-only lines where supported |
| Lines of Executable code | Executable statement count or closest language-specific equivalent |

## .NET partial types

The development 2.3.0 collector emits one type row per logical type in each analyzed project/framework compilation. Included partial declarations are combined before computing member aggregates. Partial member signatures and implementations contribute once, and each member is emitted once under its owning type. Generated and excluded files remain excluded. Type/member display names and CSV columns are unchanged; short names are not unique identifiers for nested types, generic arities, or overloads.

Raw source-line counts sum included declarations and retain physical header/formatting overhead. Moving identical members between partial files does not change complexity, decomposition, member-averaged maintainability, or the union of coupled types. It can change physical source lines. These corrected populations can change scores relative to earlier development 2.3.0 artifacts even though thresholds are unchanged; compare package hashes and run IDs as well as versions.
