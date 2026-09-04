# Transition
[![unity-meta-check](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Transition.svg)](https://github.com/AndanteTribe/Transition/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Transition.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.transition?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.transition/)

English | [日本語](README_JA.md)

## Overview
**Transition** is a Unity scene transition system that can load scenes through Addressables or Unity's `SceneManager`.

You declare the final set of scenes you need. `SceneControllerCore<TScene>` compares that target with the scenes managed by the controller, loads any missing scenes, and unloads any scenes that are no longer needed — all in a single `LoadAsync` call.

Scene identifiers can be ordinary enums, bit-flag enums, strings, or your own value types. For normal target reconciliation, the Addressables and SceneManager-specific load/unload details are resolved by their respective loaders rather than by the core transition logic.

## Requirements
- Unity 2021.3 or later
- [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.19.19 or later
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 or later
- [UniTaskPlus](https://github.com/AndanteTribe/UniTaskPlus) 0.1.0 or later

## Installation
Open `Window > Package Manager`, select `[+] > Add package from git URL`, and enter the following URL:

```
https://github.com/AndanteTribe/Transition.git?path=src/Transition.Unity/Packages/jp.andantetribe.transition
```

## Setup

When using `RestartAsync`, add a scene named **`"System"`** to `File > Build Settings`. `RestartAsync` loads this scene in Single mode as the application's initial scene.

- When using Addressables, register each target scene with Addressables.
- When using `SceneManager`, add each target scene to `File > Build Settings`.
- If your own asmdef explicitly selects its references, reference `Transition`. Also reference `Transition.Addressables` when using `AddressablesSceneController`.

## Quick Start

The following example uses bit-flag identifiers with Addressables.

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Transition;

// A flags controller requires [Flags] and int as the underlying type.
[Flags]
public enum SceneName : int
{
    None  = 0,
    Title = 1 << 0,
    Game  = 1 << 1,
    HUD   = 1 << 2,
}

public sealed class GameFlow
{
    private readonly SceneControllerCore<SceneName> _controller =
        AddressablesSceneController.CreateFlags<SceneName>();

    public async UniTask RunAsync(CancellationToken cancellationToken)
    {
        // Load Game and HUD additively. Managed scenes outside this target are unloaded.
        await _controller.LoadAsync(
            progress: null,
            cancellationToken,
            SceneName.Game | SceneName.HUD);

        // Transition to Title, unloading Game and HUD.
        await _controller.LoadAsync(
            progress: null,
            cancellationToken,
            SceneName.Title);

        // The current state is exposed as a SceneSet, even for a flags controller.
        var currentFlags = _controller.CurrentScenes.ToFlags();

        await _controller.UnloadAllAsync(progress: null, cancellationToken);

        // Unload managed scenes and reload System in Single mode.
        await _controller.RestartAsync(
            progress: null,
            forceImmediate: false,
            cancellationToken);
    }
}
```

## Addressables

### Using ordinary scene identifiers

Use `CreateScenes` when each enum value represents one scene rather than a collection of bit flags. Multiple target scenes can be passed directly without manually creating a `SceneSet`.

```csharp
public enum GameScene
{
    Title,
    Stage100,
    UserInterface,
}

private readonly SceneControllerCore<GameScene> _controller =
    AddressablesSceneController.CreateScenes<GameScene>();

await _controller.LoadAsync(
    cancellationToken,
    GameScene.Stage100,
    GameScene.UserInterface);
```

By default, `ToString()` is used as the Addressables address. Supply a resolver when the logical scene identifier and Addressables address differ.

```csharp
private readonly SceneControllerCore<GameScene> _controller =
    AddressablesSceneController.CreateScenes<GameScene>(static scene => scene switch
    {
        GameScene.Title         => "Scenes/Title",
        GameScene.Stage100      => "Scenes/Stage100",
        GameScene.UserInterface => "Scenes/UI",
        _ => throw new ArgumentOutOfRangeException(nameof(scene)),
    });
```

## SceneManager

Use `SceneManagerController` to load scenes from Build Settings without using Addressables APIs.

```csharp
private readonly SceneControllerCore<GameScene> _controller =
    SceneManagerController.CreateScenes<GameScene>();

await _controller.LoadAsync(
    cancellationToken,
    GameScene.Stage100,
    GameScene.UserInterface);
```

By default, `ToString()` is used as the scene name. A resolver can return either a scene name or a path from Build Settings.

```csharp
private readonly SceneControllerCore<GameScene> _controller =
    SceneManagerController.CreateScenes<GameScene>(static scene => scene switch
    {
        GameScene.Title         => "Assets/Scenes/Title.unity",
        GameScene.Stage100      => "Assets/Scenes/Stage100.unity",
        GameScene.UserInterface => "Assets/Scenes/UI.unity",
        _ => throw new ArgumentOutOfRangeException(nameof(scene)),
    });
```

`SceneManagerController.CreateFlags<TEnum>()` is also available when you want to use the bit-flag input style with Build Settings scenes.

## Bit Flags

Bit flags provide a convenient input format for selecting multiple scenes.

```csharp
[Flags]
public enum SceneName : int
{
    None  = 0,
    Title = 1 << 0,
    Game  = 1 << 1,
    HUD   = 1 << 2,
}

var controller = AddressablesSceneController.CreateFlags<SceneName>();

await controller.LoadAsync(
    progress: null,
    cancellationToken,
    SceneName.Game | SceneName.HUD);
```

A flags enum must:

- have `[Flags]`;
- use `int` as its underlying type;
- use `0` for the empty value;
- use one bit for each individual scene.

Because an `int` has 32 bits, the flags input format is limited to 32 individual values. This limit applies only to flags. `CreateScenes` and `SceneSet<TScene>` can represent larger scene collections.

Use `SceneSet.FromFlags` and `ToFlags` when converting explicitly:

```csharp
SceneSet<SceneName> target =
    SceneSet.FromFlags(SceneName.Game | SceneName.HUD);

SceneName flags = target.ToFlags();
```

`SceneSet.Of(SceneName.Game | SceneName.HUD)` creates one composite identifier; it does not split the value into individual flags. Use `SceneSet.FromFlags` for that purpose.

## SceneSet

`SceneSet<TScene>` is an immutable collection that removes duplicate identifiers while preserving their first-occurrence load order.

```csharp
SceneSet<GameScene> target = SceneSet.Of(
    GameScene.Stage100,
    GameScene.UserInterface);

await _controller.LoadAsync(
    target,
    progress: null,
    cancellationToken);
```

Use `With` to derive a target from a reusable scene set. The original set is not modified.

```csharp
SceneSet<GameScene> sharedUI = SceneSet.Of(
    GameScene.UserInterface);

SceneSet<GameScene> stage100 = sharedUI.With(
    GameScene.Stage100);

await _controller.LoadAsync(
    stage100,
    progress: null,
    cancellationToken);
```

Use `SceneSet<TScene>.Empty` for an explicit empty target. Calling `LoadAsync` with an empty target unloads every scene managed by that controller.

```csharp
await _controller.LoadAsync(
    SceneSet<GameScene>.Empty,
    progress: null,
    cancellationToken);
```

Only scenes loaded through a controller are represented by its `CurrentScenes` and reconciled by that controller. Scenes loaded or unloaded externally are not automatically synchronized with its state.

## Custom Scene Loaders

For another loading backend, implement `ISceneLoader<TScene>`. A successful load returns an `ISceneHandle`, which represents ownership of that loaded scene and is later used by the controller to unload it. The following is an implementation skeleton; the actual load and handle logic depends on your backend.

```csharp
public sealed class CustomSceneLoader : ISceneLoader<GameScene>
{
    public UniTask<ISceneHandle> LoadAsync(
        GameScene scene,
        IProgress<float>? progress,
        CancellationToken cancellationToken)
    {
        // Load one scene and return its lifetime.
        throw new NotImplementedException();
    }
}

var controller = new SceneControllerCore<GameScene>(new CustomSceneLoader());
```

The core API does not require an Addressables address, `AsyncOperationHandle`, `SceneInstance`, or release key. Backend-specific ownership remains inside the returned `ISceneHandle`.

## Cancellation

The built-in loaders check cancellation before starting a scene load. Once a Unity or Addressables load has started, they observe the physical operation and any required rollback before reporting cancellation. Once an unload has started, they also wait for that physical operation to finish before reporting cancellation.

## Operational Notes

- Normal controller operations are serialized. `RestartAsync(forceImmediate: true)` bypasses that serialization and must not run concurrently with another operation on the same controller. Use `false` for normal transitions.
- `IProgress<float>` reports the progress of each individual scene operation. With multiple scenes, it is not a single monotonic progress value for the complete transition.
- Different logical identifiers that resolve to the same physical scene are not automatically treated as duplicates.
- The SceneManager loader does not support another loader concurrently loading the same scene, because Unity's `AsyncOperation` does not identify the `Scene` that it created.

## API

### Controller Factories

| Member | Description |
|--------|-------------|
| `AddressablesSceneController.CreateFlags<TEnum>(getSceneName?)` | Creates an Addressables-backed controller that expands bit flags. |
| `AddressablesSceneController.CreateScenes<TScene>(getSceneName)` | Creates an Addressables-backed controller for ordinary scene identifiers. An enum overload uses `ToString()`. |
| `SceneManagerController.CreateFlags<TEnum>(getSceneName?)` | Creates a SceneManager-backed controller that expands bit flags. |
| `SceneManagerController.CreateScenes<TScene>(getSceneName)` | Creates a SceneManager-backed controller for ordinary scene identifiers. An enum overload uses `ToString()`. |

### `SceneControllerCore<TScene>`

| Member | Description |
|--------|-------------|
| `CurrentScenes` | Gets the scenes currently managed by this controller. |
| `LoadAsync(params TScene[] targetScenes)` | Reconciles the managed scenes with the target identifiers. |
| `LoadAsync(CancellationToken cancellationToken, params TScene[] targetScenes)` | Reconciles the managed scenes with the target identifiers and supports cancellation. |
| `LoadAsync(IProgress<float>? progress, CancellationToken cancellationToken, params TScene[] targetScenes)` | Reconciles to multiple identifiers while reporting each scene operation's progress. |
| `LoadAsync(SceneSet<TScene> targetScenes, IProgress<float>? progress, CancellationToken cancellationToken)` | Reconciles to an explicitly created scene set. |
| `UnloadAllAsync(IProgress<float>? progress, CancellationToken cancellationToken)` | Unloads all scenes managed by this controller. |
| `RestartAsync(IProgress<float>? progress, bool forceImmediate, CancellationToken cancellationToken)` | Unloads managed scenes and reloads `System` in Single mode. `forceImmediate: true` skips operation serialization. |

### `SceneSet`

| Member | Description |
|--------|-------------|
| `SceneSet.Of(params TScene[] scenes)` | Creates an ordered, duplicate-free set of identifiers. |
| `sceneSet.With(params TScene[] scenes)` | Creates a new set by appending identifiers without modifying the original set. |
| `SceneSet.FromFlags(params TEnum[] flags)` | Expands `int`-backed enum flags into individual identifiers. |
| `sceneSet.ToFlags()` | Aggregates a flags scene set into one enum value. |
| `SceneSet<TScene>.Empty` | Gets an empty target set. |

## License
This library is released under the MIT license.
