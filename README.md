# Transition
[![unity-meta-check](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Transition.svg)](https://github.com/AndanteTribe/Transition/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Transition.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.transition?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.transition/)

English | [日本語](README_JA.md)

## Overview

**Transition** loads the scenes you need and unloads the scenes you no longer need.

Pass an ordinary enum to `SceneCollection<TScene>.SetAsync`. The collection compares the target with its managed scenes, loads missing scenes, then unloads obsolete scenes. Successful operations remain recorded even if a later operation fails. Calling `SetAsync` with the same target retries only the remaining work.

## Requirements

- Unity 2021.3 or later (C# 9 / .NET Standard 2.1).
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 or later.
- [Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@1.19/manual/index.html) 1.19.19 or later, only for the optional Addressables package.

## Installation

Install UniTask first. In `Window > Package Manager > [+] > Add package from git URL`, add:

```text
https://github.com/AndanteTribe/Transition.git?path=src/Transition.Unity/Packages/jp.andantetribe.transition
```

For Addressables, also add:

```text
https://github.com/AndanteTribe/Transition.git?path=src/Transition.Unity/Packages/jp.andantetribe.transition.addressables
```

Install the core package before the Addressables package. If you use your own asmdef, reference `Transition`; also reference `Transition.Addressables` when using `AddressablesSceneCollection` or `AddressablesBackend`.

## Quick Start

Start the application in a bootstrap scene named `System`. Keep it outside the collection and leave it loaded. Add `System` and the following gameplay scenes to Build Settings.

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using Transition;

public enum GameScene
{
    Title,
    Game,
    UI,
}

public sealed class GameFlow
{
    private readonly SceneCollection<GameScene> _scenes =
        SceneCollection.Create<GameScene>();

    public async UniTask RunAsync(CancellationToken cancellationToken)
    {
        await _scenes.SetAsync(GameScene.Title, GameScene.UI, cancellationToken);

        // Keeps UI, loads Game, then unloads Title.
        await _scenes.SetAsync(GameScene.Game, GameScene.UI, cancellationToken);

        // Releases managed scenes and restarts from System.
        await _scenes.ResetAsync(cancellationToken);
    }
}
```

`SceneCollection.Create<TScene>()` creates a SceneManager-backed collection. Enum names are scene names by default. Zero is an ordinary scene identifier. Do not use bit flags; there is no 32-scene limit.

Call collection APIs on Unity's main thread and await the returned UniTask. A backend belongs to one collection; do not share it or operate it directly after passing it to the collection.

## Addressables

Register gameplay scenes with Addressables and give them addresses matching the enum names. The bootstrap/reset scene stays in Build Settings.

```csharp
var scenes = AddressablesSceneCollection.Create<GameScene>();

await scenes.SetAsync(GameScene.Game, GameScene.UI, cancellationToken);
```

To use different addresses:

```csharp
var scenes = AddressablesSceneCollection.Create<GameScene>(
    getSceneName: static scene => scene switch
    {
        GameScene.Title => "Scenes/Title",
        GameScene.Game  => "Scenes/MainGame",
        GameScene.UI    => "Scenes/SharedUI",
        _ => throw new System.ArgumentOutOfRangeException(nameof(scene)),
    });
```

Addresses must identify individual, distinct scenes, not multi-scene labels. Different enum values mapping to the same physical scene are not automatically deduplicated.

## SceneManager

`SceneManagerBackend` uses additive loading and stores the exact loaded Unity Scene internally. A resolver can map enum values to full Build Settings paths:

```csharp
var scenes = SceneCollection.Create<GameScene>(
    getSceneName: static scene => scene switch
    {
        GameScene.Title => "Assets/Scenes/Title.unity",
        GameScene.Game  => "Assets/Scenes/Game.unity",
        GameScene.UI    => "Assets/Scenes/UI.unity",
        _ => throw new System.ArgumentOutOfRangeException(nameof(scene)),
    },
    initialSceneName: "Boot");
```

Ambiguous short names are rejected; use full paths when names collide. Already-loaded scenes are not adopted. Do not load or unload the same physical scenes outside the backend during its ownership.

## Scene Targets and Current State

Fixed overloads accept one to five scenes with an optional final CancellationToken:

```csharp
await scenes.SetAsync(GameScene.Game);
await scenes.SetAsync(GameScene.Game, GameScene.UI, cancellationToken);

// Duplicate identifiers are ignored, keeping their first occurrence.
await scenes.SetAsync(GameScene.Game, GameScene.UI, GameScene.Game);

// Empty target: unload all managed scenes, leaving the bootstrap scene.
await scenes.SetAsync(cancellationToken);

bool hasUI = scenes.Contains(GameScene.UI);
int count = scenes.Count;
GameScene[] snapshot = scenes.GetCurrentScenes();
```

`GetCurrentScenes()` returns an independent array in successful-load completion order. The collection does not expose a mutable list or a live view.

Use `ReadOnlyMemory<TScene>` for larger or reusable targets:

```csharp
GameScene[] target = { GameScene.Game, GameScene.UI };
await scenes.SetAsync(target, cancellationToken);
```

One-to-five-scene overloads store input inline without a per-call params array or HashSet. Memory targets up to five scenes also copy inline. Larger targets copy to a private array before queuing so later changes to the caller's buffer cannot change an in-flight request. This is not a promise that every transition or backend performs zero allocations.

## Sequential / Parallel

Choose the mode once when creating the collection:

```csharp
var scenes = AddressablesSceneCollection.Create<GameScene>(
    loadingMode: SceneLoadingMode.Parallel);
```

- `Sequential` (default): missing scenes load in target order. Use this when a scene depends on a previously loaded scene.
- `Parallel`: missing scenes within one Set call start concurrently. Every started operation is observed, even after another fails or cancels.
- Set and Reset calls on the same collection are always serialized.
- Unloads are sequential, in reverse successful-load completion order.
- Load failure/cancellation prevents the unload phase. Unload failure stops further unloads.

Parallel mode provides no scene initialization order guarantee. Multiple faults produce an `AggregateException`; cancellation-only outcomes produce an `OperationCanceledException`.

## Failure and Cancellation

Completed work is retained; there is no transition-wide rollback.

For example, if the current state is `Title + UI` and the target is `Game + UI + Audio`, a successful Game load followed by a failed Audio load leaves `Title + UI + Game`. Retry the same target to load Audio and then unload Title.

Cancellation prevents new operations from starting. Already-started operations are observed until the backend reaches a stable state. Unity's built-in backends finish an already-started physical operation even after cancellation. The collection records that success before reporting cancellation.

If Game and Audio succeed while UI cancels in Parallel mode, Game and Audio remain managed, old scenes remain loaded, and Set reports cancellation. Inspect `Contains`, `Count`, or `GetCurrentScenes()` after catching an exception.

## Reset

`ResetAsync` uses the same transition gate as Set. It validates the initial Build Settings scene, unloads owned scenes through the backend, then directly loads the initial scene with `SceneManager.LoadSceneAsync(..., LoadSceneMode.Single)`.

```csharp
var scenes = SceneCollection.Create<GameScene>(initialSceneName: "Boot");

await scenes.ResetAsync(cancellationToken);
```

On success, the managed collection is empty. Boot/System remains outside the collection, and the same collection/backend can be reused.

Cancellation is honored before Single loading starts. Once it starts, Reset waits for completion and returns success even if the token is subsequently canceled. An earlier unload failure/cancellation retains still-owned scenes for retry.

Single loading affects the whole Unity scene environment, including scenes outside this collection. Use Reset as an application-level restart; do not run another collection or external scene operations alongside it.

Unity cannot unload its last loaded scene. Keep the bootstrap scene outside the collection for empty targets and Reset. See [Unity's UnloadSceneAsync API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/SceneManagement.SceneManager.UnloadSceneAsync.html).

## Custom Backends

The factories create a dedicated standard backend for each collection. To supply a custom backend, use the constructor directly: `new SceneCollection<GameScene>(backend)`.

Implement these two methods:

```csharp
public interface ISceneBackend<TScene> where TScene : struct, System.Enum
{
    UniTask LoadAsync(TScene scene, CancellationToken cancellationToken = default);
    UniTask UnloadAsync(TScene scene, CancellationToken cancellationToken = default);
}
```

The backend handles only one scene per operation and stores its own physical scene/resource ownership. The collection handles targets, deduplication, ordering, serialization, and Reset.

The contract is:

| Outcome | Required state |
|---------|----------------|
| Load succeeds | The scene is fully loaded and owned by the backend. |
| Load fails or cancels | No new ownership from that operation remains; the same identifier can be retried. |
| Unload succeeds | The scene is fully unloaded and its ownership is released. |
| Unload fails or cancels | The loaded scene and its ownership remain intact and can be retried. |

Use the token while cancellation is safe. After the point of no cancellation, finish and return success. Never cancel only the await while a physical operation continues unobserved. If Unload has already physically succeeded, do not throw cancellation afterward.

A typical non-cancelable engine load follows this sequence:

```csharp
cancellationToken.ThrowIfCancellationRequested();
var operation = StartPhysicalLoad(scene);
// Point of no cancellation.
await operation.ToUniTask(cancellationToken: CancellationToken.None);
// Record backend ownership, then return success.
```

`StartPhysicalLoad` is a placeholder for your engine/resource API. Reserve ownership bookkeeping before physical work where needed, and clean up failed loads before returning an error.

Different scene identifiers may be loaded concurrently in Parallel mode. Backend operations must not call and await Set/Reset on their owning collection: that would wait for the transition gate they already occupy. Complete Unity-related ownership work on the main thread.

## API

| Member | Description |
|--------|-------------|
| `SceneCollection.Create<TScene>(getSceneName = null, initialSceneName = "System", loadingMode = Sequential)` | Creates a SceneManager-backed collection. |
| `AddressablesSceneCollection.Create<TScene>(getSceneName = null, initialSceneName = "System", loadingMode = Sequential)` | Creates an Addressables-backed collection. |
| `new SceneCollection<TScene>(backend, initialSceneName = "System", loadingMode = Sequential)` | Creates a collection. |
| `SetAsync(scene1, ..., scene5, cancellationToken = default)` | Fixed overloads for one to five target scenes. |
| `SetAsync(ReadOnlyMemory<TScene>, cancellationToken = default)` | Target of any size. |
| `SetAsync(cancellationToken = default)` | Empty target. |
| `ResetAsync(cancellationToken = default)` | Releases ownership and reloads the initial scene. |
| `Count` / `Contains(scene)` | Current owned state, including partial success. |
| `GetCurrentScenes()` | Explicitly allocated state snapshot. |

## License

This library is released under the MIT license.
