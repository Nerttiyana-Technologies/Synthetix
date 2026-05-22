# Synthetix diagnostics

Every mapping mistake Synthetix can detect has a stable `SYNTX####` id and a reference page below. Ids are never reused or renumbered, so editorconfig rules and `#pragma warning` suppressions keep working across versions.

| Id | Severity | Title |
|----|----------|-------|
| [SYNTX001](SYNTX001.md) | Error | Mapper class must be partial |
| [SYNTX002](SYNTX002.md) | Error | Mapping method must be partial |
| [SYNTX003](SYNTX003.md) | Error | No mapping could be created |
| [SYNTX004](SYNTX004.md) | Warning | Target member has no source |
| [SYNTX005](SYNTX005.md) | Info | Source member is unused |
| [SYNTX006](SYNTX006.md) | Error | Target member not found |
| [SYNTX007](SYNTX007.md) | Error | Source path not found |
| [SYNTX008](SYNTX008.md) | Error | Ambiguous flattening |
| [SYNTX009](SYNTX009.md) | Error | Circular reference detected |
| [SYNTX010](SYNTX010.md) | Error | Member type mismatch |
| [SYNTX011](SYNTX011.md) | Error | No usable constructor |
| [SYNTX012](SYNTX012.md) | Warning | Nullable mapped to non-nullable |
| [SYNTX013](SYNTX013.md) | Error | Explicit mapping required |
| [SYNTX014](SYNTX014.md) | Error | Conflicting configuration |
| [SYNTX015](SYNTX015.md) | Error | Converter method not found |
| [SYNTX016](SYNTX016.md) | Error | Mapper has no mapping methods |
| [SYNTX017](SYNTX017.md) | Warning | Mapping has drifted from the manifest |
| [SYNTX018](SYNTX018.md) | Error | Collection element cannot be mapped |
| [SYNTX019](SYNTX019.md) | Error | Unsupported collection type |
| [SYNTX020](SYNTX020.md) | Error | Polymorphic mapping is not exhaustive |
| [SYNTX021](SYNTX021.md) | Error | Invalid derived-type mapping |
| [SYNTX022](SYNTX022.md) | Warning | Member cannot be updated |
| [SYNTX023](SYNTX023.md) | Error | Mapping is not projectable |
| [SYNTX024](SYNTX024.md) | Error | Async mapping in a synchronous method |
