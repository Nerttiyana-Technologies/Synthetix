# SYNTX017: Mapping has drifted from the manifest

**Severity:** Warning

The current mapping no longer matches the mapping manifest committed to source control. The manifest is Synthetix's auditable record of every mapping decision, so drift means a mapping changed without the manifest being refreshed.

## Cause

A mapping method's resolved behaviour differs from the committed `.manifest.md` / `.manifest.json` files.

## How to fix it

Run `dotnet build -t:SynthetixUpdateManifest` to refresh the manifest, then review and commit the change so the new mapping is recorded deliberately.
