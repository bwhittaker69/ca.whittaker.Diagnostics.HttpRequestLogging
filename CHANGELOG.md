# Changelog

## 1.0.0-preview.1

- Introduce method-only defaults and explicit per-field capture options.
- Add mandatory sensitive-name redaction and explicit header/query/JSON allowlists.
- Bound JSON inspection and preserve normal downstream body reading.
- Omit malformed, unsupported, oversized and unreadable bodies.
- Preserve cancellation/downstream errors and tolerate diagnostic-provider failures.
- Include XML documentation, a runnable package-based sample and focused tests.
