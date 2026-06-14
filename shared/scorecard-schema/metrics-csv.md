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
