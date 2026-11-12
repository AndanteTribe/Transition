# Transition

[![unity-meta-check](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Transition.svg)](https://github.com/AndanteTribe/Transition/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Transition.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.transition?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.transition/)

English | [日本語](README_JA.md)

## Overview

Transition manages groups of Unity scenes with SceneManager or Addressables. Create a `SceneGroup` from enum values and load it to add missing scenes and unload scenes no longer needed. A group may contain values from different enum types.

## Requirements

- Unity 2021.3 or later (C# 9 / .NET Standard 2.1)
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 or later
- [UniTaskPlus](https://github.com/AndanteTribe/UniTaskPlus) 1.1.1 or later
- [Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@1.19/manual/index.html) 1.19.19 or later, including when using SceneManager

## Installation

Open `Window > Package Manager`, select `[+] > Add package from git URL`, and add the following packages in order:

1. UniTask:

   ```text
   https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
   ```

2. UniTaskPlus:

   ```text
   https://github.com/AndanteTribe/UniTaskPlus.git?path=src/UniTaskPlus.Unity/Packages/jp.andantetribe.unitaskplus
   ```

3. Transition:

   ```text
   https://github.com/AndanteTribe/Transition.git?path=src/Transition.Unity/Packages/jp.andantetribe.transition
   ```

Addressables is installed as a Transition dependency. If your project uses assembly definitions, reference `Transition`.

## Quick Start

Keep a bootstrap scene loaded outside Transition's management. For this example, start from a scene named `System` and add `System`, `Game`, and `GameUI` to Build Settings.

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using Transition;

public enum GameScene
{
    Title,
    Game,
    Result,
}

public enum UIScene
{
    TitleUI,
    GameUI,
    ResultUI,
}

public sealed class GameFlow
{
    private readonly SceneGroupLoader _scenes = SceneGroupLoader.Create.SceneManager();

    public async UniTask LoadGameAsync(CancellationToken cancellationToken = default)
    {
        SceneGroup group;
        using (var builder = new SceneGroupBuilder())
        {
            builder.Add(GameScene.Game);
            builder.Add(UIScene.GameUI);
            group = builder.Build();
        }

        await _scenes.LoadAsync(group, cancellationToken);
    }
}
```

Create a `GameFlow` instance in your application and call `LoadGameAsync`. By default, enum member names are used as scene names. Groups are immutable and can be reused; dispose the builder after `Build` to return its working list to the pool. Calling `LoadAsync` with another group keeps shared scenes, loads missing scenes, and then unloads obsolete managed scenes.

Typed `Add` and `Contains` calls and the built-in scene loaders use a shared box cache: the first use boxes a value, and later uses reuse it. The cache has no fixed entry limit, so it retains each distinct value for its lifetime. Builder and group creation and scene operations can still allocate.

## Addressables and scene names

Register gameplay scenes with Addressables and use their addresses as enum names, or supply a resolver:

```csharp
var scenes = SceneGroupLoader.Create.Addressables(
    getSceneAddress: scene => $"Scenes/{scene}",
    loadingMode: SceneLoadingMode.Parallel);
```

`SceneGroupLoader.Create.SceneManager` also accepts a `getSceneName` resolver. Each identifier must map to a distinct physical scene. Different enum types can have members with the same name; use a resolver to distinguish them. Addressables keys must identify individual scenes, not multi-scene labels.

Choose `Sequential` (default) to load missing scenes in group order, or `Parallel` to start them concurrently. Parallel scene initialization order is not guaranteed.

## Failure and cancellation

Completed operations remain in effect after failure or cancellation; Transition does not roll back the whole transition. If a load fails, old scenes are not unloaded. Retrying the same group attempts the remaining work. Use `Contains`, `Count`, or `CurrentScenes` to inspect the managed state.

Cancellation prevents operations that have not started. The built-in loaders finish an already-started physical operation and record its result before cancellation is reported. In Parallel mode, all started loads are observed before returning.

Parallel load failures are reported as an `AggregateException`, even for a single failure. When failures and cancellations occur together, both are included in `InnerExceptions`; cancellation alone is reported as an `OperationCanceledException`. Sequential load and unload failures report their original exceptions.

## API

| Member | Description |
|--------|-------------|
| `SceneGroupLoader.Create.SceneManager(...)` / `Create.Addressables(...)` | Creates a loader with the selected backend. |
| `new SceneGroupBuilder()` | Creates a builder; dispose it after use. |
| `builder.Add(scene)` / `builder.Build()` | Adds a scene / creates an immutable group. |
| `LoadAsync(group, cancellationToken)` | Loads the target group. `SceneGroup.Empty` or `null` unloads all managed scenes. |
| `Count` / `Contains(scene)` / `CurrentScenes` | Gets the current managed state. `CurrentScenes` is a read-only live view. |
| `ResetAsync(cancellationToken)` | Unloads managed scenes and loads the initial scene in Single mode. |

The default initial scene name is `System`; set `initialSceneName` when creating the loader to use another Build Settings scene. `ResetAsync` affects all Unity scenes, including scenes outside the loader. Once Single loading starts, Reset waits for it to finish and returns success even if cancellation is requested.

Call Transition APIs on Unity's main thread. Each `SceneGroupLoader` needs its own scene loader; do not externally load or unload its managed scenes. Keep the bootstrap scene loaded outside the loader because Unity cannot unload its last scene.

## Custom scene loaders

Pass an `ISceneLoader` to `new SceneGroupLoader(sceneLoader)` to use a custom backend. Implement `LoadAsync<TEnum>` and `UnloadAsync<TEnum>` with `where TEnum : System.Enum`; `EnumBoxCache.Get(scene)` can reuse boxed identifiers.

Check cancellation before starting an operation. A successful load must retain scene ownership until a successful unload; a failed or canceled operation must leave ownership in a state that can be retried. Observe started physical operations to completion, and do not call and await the owning group loader from its scene loader. In Parallel mode, loads for different identifiers can overlap.

## License

This library is released under the MIT license.
