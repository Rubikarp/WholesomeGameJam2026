# Asset Migration

Editor-only, automatic migrator that rewrites serialized asset data when a registered package's version
changes. Byte-preserving text splices on Unity YAML; no prompts, no menu.

## How it runs

After package registration or a completed asset import, the migrator picks a pass:

- **Full** — when a registered package's version differs from its ledger stamp, or when this project copy has
  no discovery state yet (a ledger stamped on another machine does not count as migrated here). Every
  `IMigration` runs over the assets that can match it; only this pass advances stamps.
- **Incremental** — otherwise, over just the YAML assets that entered the project in that import, and only
  with idempotent migrations. Legacy data also arrives through branches, package imports and file restores,
  which no version change announces.

Batch mode is skipped so headless CI never rewrites assets mid-build. The project must use **Asset
Serialization Mode = Force Text**; the migrator fails visibly instead of treating binary scenes or prefabs as
already current.

Idempotent asset-local migrations commit one candidate at a time, so one damaged asset does not block valid
assets or unrelated migrations. All idempotent work completes before one-shot migrations can run; one-shot
and project-prepared migrations stage all candidates together.
Every failure remains visible and leaves package versions stale for the next domain reload. Successful
one-shot migrations record completion immediately; a `Library/` recovery journal prevents an interrupted
commit from replaying a value rewrite.

- **Discovery** — a token index (`Library/LightSide/MigrationIndex.json`, a rebuildable cache) maps
  type/script tokens to asset guids, so only assets that can match are read. Routine imports maintain it;
  migration passes validate its Unity artifact version and rebuild stale or malformed state from source
  assets. The index records which assets it has actually read, and every full pass scans the ones it has
  not — a cache built while the project was still importing repairs itself instead of hiding the gap. Managed-type tokens are indexed for every serialized reference and every prefab-instance override
  that names one; script tokens only for guids a migration names.
- **State** — `ProjectSettings/LightSide/MigrationLedger.json`: per-package version stamps (the trigger)
  and the ids of one-shot migrations already applied. Committed through version control.

## Layout

- The framework (contract, discovery, index, runner, trigger, state, Unity-YAML reader) lives in the
  LightSide.Core package under `Editor/Migration/` — stable, don't edit here.
- `UniTextMigratedPackage.cs` — registers UniText with the trigger (`IMigratedPackage`).
- `Migrations/` — the migrations. **Add a file here per breaking change.**

## Add a migration

Rename a `[SerializeReference]` type:

```csharp
internal sealed class FooToBar : RenameManagedType
{
    public FooToBar() : base(new TypeSignature("FooModifier", "LightSide", "LightSide.UniText"), "BarModifier") { }
}
```

Rename a serialized field key inside a set of types:

```csharp
internal sealed class RulesFieldMigration : RenameManagedField
{
    public RulesFieldMigration() : base("effects", "rules", typeof(InteractiveModifier)) { }
}
```

The `typeof` form covers that type and every non-abstract subclass, consumer ones included. Pass
`TypeSignature`s instead when the set spans identities the project no longer declares — list both sides of a
type rename and the field rename holds whichever order the two run in. Either form derives its own discovery
tokens.

Move base-class fields into a new nested struct field:

```csharp
internal sealed class PaintFieldsToStruct : MoveFieldsToStruct
{
    protected override Type BaseType => typeof(PaintModifierBase);
    protected override string StructField => "paint";
    protected override HashSet<string> FieldNames => new() { "mapping", "shape", "angle" };
}
```

Anything else — implement `IMigration` directly: declare `Tokens`, walk `ctx.Documents`, edit through
`ctx.Edit` (span splices), produce/delete assets through `ctx.Assets`.

## Rules that bite

- **Idempotent vs one-shot.** Renames, reshapes, and existence-checked asset creation are idempotent —
  they stop matching once applied, so set `Idempotent = true`. A value rewrite whose result is
  indistinguishable from valid current data (e.g. `weight: 700` → the new Auto sentinel `0`) is **not**:
  set `Idempotent = false` and it runs exactly once via the ledger stamp. Marking such a migration
  idempotent corrupts real data on the second pass.
- **No-op on non-matching shape.** `Migrate` is called on every candidate document; change nothing unless
  the exact old shape is present.
- **Persisted inputs only.** An open scene, prefab, or asset with unsaved changes is rejected for that pass;
  save it and reload the scripting domain to retry without overwriting editor state.
- **One-shot work edits YAML only.** Non-idempotent migrations cannot queue asset creation, deletion, or
  moves because those side effects cannot share the recovery journal's exact replay contract.
- **One edit per span.** Don't queue overlapping splices for the same node in one migration.
- **Deterministic target paths** for `ctx.Assets.Create`, with an existence check — otherwise a second run
  duplicates the asset.

## When NOT to use this

- **A field rename that must survive AssetBundles** → `[FormerlySerializedAs]`. It is engine-level and free,
  and it reaches runtime-loaded bundles this editor tool cannot.
- **Renaming/moving a `.cs` type that must resolve at runtime** → `[MovedFrom]`. This engine rewrites
  in-project assets only.
- **Object references** (Font/Material/Texture) carried by an old type → keep the type resolvable
  (`[MovedFrom]`); do not round-trip them through text.

## Reach

A `[SerializeReference]` object is serialized in two shapes: in its owner's `references: RefIds` block, and —
when a prefab instance owns it — as `managedReferences[rid]` modifications on that instance. Type renames
cover both. Field-level migrations (`RenameManagedField`, `MoveFieldsToStruct`) cover only the `RefIds`
shape; a field override on a prefab instance still names the old path and Unity drops it on load.

In-project text-serialized scenes, prefabs, materials, and `.asset` files under `Assets/` plus embedded/local
packages. Native binary asset payloads, immutable package caches, prebuilt AssetBundles, and runtime save/UGC
data are out of reach — cover those with the attributes above.
