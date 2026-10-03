# LightSide Core

Shared runtime and Editor foundation for LightSide Unity packages: reusable utilities, serialized-state tooling, attributes, drawers, editor UI, the shared frame loop and playback clocks, and the motion primitives every animated package builds on.

LightSide packages install Core as a dependency through the `media.lightside` scoped registry; projects and package authors can also use its public runtime and Editor APIs directly.

## Motion primitives

`LightSide.Motion` carries the vocabulary shared by everything that animates: easing curves that a designer can author, spring parameters, and the weight envelope one animated application fades in and out on.

```csharp
Ease ease = EasingType.CubicOut;
var height = ease.Lerp(0f, 2f, t);
```

The tweening engine that drives these over time — motions, sequences, timelines and inspector-authored animation — is the separate [MoveIt](https://unity.lightside.media) package.

## Package authoring

Generated Runtime mutation and Editor reconciliation contracts are described in [State Mutations](Docs~/SERIALIZED_STATE.md).

Inspector, window, drawer, and popup composition rules are described in [Editor UI](Docs~/EDITOR_UI.md).

## License

MIT — see [LICENSE.md](LICENSE.md).
