# Flexible Image v2-to-v3 migration

The migration window performs a direct YAML transformation because v3 modularizes previously inline data. It scans only project assets discovered through Unity's Asset Database.

## Discoverable files

- Scenes (`.unity`)
- Prefabs (`.prefab`)
- Animation Clips (`.anim`) for hazardous moved bindings
- `QuadDataPreset` assets (`.asset`)

Animation Clips are diagnostic: a direct binding to a moved v2 property blocks migration and must be repaired manually. The selection toolbar migrates scenes, prefabs, and presets.

## Status meanings

| Status | Meaning | Action |
|---|---|---|
| Ready | v2 data can be transformed | Review and select |
| Version3 | Relevant data is already modular | No action |
| Binary | The file is not readable Unity YAML | Convert under v2 with Force Text |
| Blocked | Known unsafe structure was found | Resolve the reported condition |
| Failed | Discovery or parsing raised an error | Inspect details and Console |

Known blockers include moved prefab override paths, direct animation bindings to moved properties, mixed v2/v3 documents, and unsupported managed-reference structures.

## Transaction behavior

Before writing, the tool transforms selected files in memory and asks for final confirmation. It then:

1. Disallows automatic refresh and starts Asset Database editing.
2. Copies originals into a timestamped directory under `Library/FlexibleImage/MigrationBackups/` and writes a manifest.
3. Replaces selected files through temporary files.
4. Restores refresh, synchronously imports, and rediscovers every migrated file.
5. Restores originals if writing or validation fails.

The Library backup is a recovery aid, not a substitute for version control.

## Module policy

The transformer preserves modules whose v2 state was configured and omits modules in their implicit-off state. This keeps migrated YAML modular and compact. Code or animation that expects to activate an omitted module later must add that module explicitly after migration.
