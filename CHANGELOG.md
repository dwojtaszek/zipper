# Changelog

## Unreleased

### Breaking Changes

- Loadfile-Only Mode `_properties.json` audit files now use `camelCase` JSON property names.
- Common schema updates include `FileName` -> `fileName`, `Format` -> `format`, `TotalRecords` -> `totalRecords`, `ChaosMode.Enabled` -> `chaosMode.enabled`, and `InjectedAnomalies[*].RecordID` -> `injectedAnomalies[*].recordID`.
- Repository-managed tooling has been updated to the new audit file schema. Any external tooling that parses `_properties.json` must be updated before consuming this branch.
