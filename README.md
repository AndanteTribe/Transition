# Transition
[![unity-meta-check](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Transition.svg)](https://github.com/AndanteTribe/Transition/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Transition.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.transition?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.transition/)

English | [日本語](README_JA.md)

## Overview
**Transition** is a Unity scene transition system built on Addressables.

It manages multiple scenes simultaneously using bit-flag enums. `SceneControllerCore<TEnum>` compares the currently active set of scenes with a desired target set and automatically loads any missing scenes while unloading any that are no longer needed — all in a single `LoadAsync` call.

## Requirements
- Unity 2021.3 or later
- [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.19.19 or later
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 or later

## Installation
Open `Window > Package Manager`, select `[+] > Add package from git URL`, and enter the following URL:

```
https://github.com/AndanteTribe/Transition.git?path=src/Transition.Unity/Packages/jp.andantetribe.transition
```

## Quick Start

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Transition;
using UnityEngine;

// Define scenes as a bit-flag enum with int as the underlying type.
[Flags]
public enum SceneName : int
{
    None  = 0,
    Title = 1 << 0,
    Game  = 1 << 1,
    HUD   = 1 << 2,
}

public class GameManager : MonoBehaviour
{
    private readonly SceneControllerCore<SceneName> _controller = new();

    private async UniTaskVoid Start()
    {
        // Load the Game and HUD scenes additively.
        // Any scene in the target set that is not yet loaded will be loaded,
        // and any currently loaded scene not in the target set will be unloaded.
        await _controller.LoadAsync(SceneName.Game | SceneName.HUD, progress: null, destroyCancellationToken);

        // Transition to the Title scene (unloads Game and HUD, loads Title).
        await _controller.LoadAsync(SceneName.Title, progress: null, destroyCancellationToken);

        // Unload all currently active scenes.
        await _controller.UnloadAllAsync(progress: null, destroyCancellationToken);

        // Restart: unload all scenes and reload the System scene.
        await _controller.RestartAsync(progress: null, forceImmediate: false, destroyCancellationToken);
    }
}
```

## API

### `SceneControllerCore<TEnum>`

`TEnum` must be an `int`-backed bit-flag enum.

| Member | Description |
|--------|-------------|
| `CurrentScene` | Gets the currently active scene flags. |
| `LoadAsync(TEnum sceneName, IProgress<float>? progress, CancellationToken cancellationToken)` | Loads all scenes represented by the given flags and unloads any scenes not in the set. |
| `UnloadAllAsync(IProgress<float>? progress, CancellationToken cancellationToken)` | Unloads all currently active scenes. |
| `RestartAsync(IProgress<float>? progress, bool forceImmediate, CancellationToken cancellationToken)` | Unloads all scenes and reloads the System scene. If `forceImmediate` is `true`, skips semaphore synchronization. |

## License
This library is released under the MIT license.
