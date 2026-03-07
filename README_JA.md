# Transition
[![unity-meta-check](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Transition.svg)](https://github.com/AndanteTribe/Transition/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Transition.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.transition?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.transition/)

[English](README.md) | 日本語

## 概要
**Transition** は、Unity Addressables を基盤としたシーン遷移システムです。

ビットフラグ列挙型を使って複数シーンを同時管理します。`SceneControllerCore<TEnum>` は、現在アクティブなシーンセットと目標のシーンセットを比較し、不足しているシーンを自動でロード、不要になったシーンを自動でアンロードします。これらを `LoadAsync` の1回の呼び出しで実行します。

## 要件
- Unity 2021.3 以上
- [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.19.19 以上
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 以上

## インストール
`Window > Package Manager` からPackage Managerウィンドウを開き、`[+] > Add package from git URL` を選択して以下のURLを入力します。

```
https://github.com/AndanteTribe/Transition.git?path=src/Transition.Unity/Packages/jp.andantetribe.transition
```

## クイックスタート

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Transition;
using UnityEngine;

// シーンをビットフラグ列挙型（基底型はint）として定義します。
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
        // Game シーンと HUD シーンを加算的にロードします。
        // 目標セットに含まれていて未ロードのシーンはロードされ、
        // 目標セットに含まれていない現在のシーンはアンロードされます。
        await _controller.LoadAsync(SceneName.Game | SceneName.HUD, progress: null, destroyCancellationToken);

        // Title シーンへ遷移します（Game と HUD をアンロードし、Title をロード）。
        await _controller.LoadAsync(SceneName.Title, progress: null, destroyCancellationToken);

        // 現在アクティブなすべてのシーンをアンロードします。
        await _controller.UnloadAllAsync(progress: null, destroyCancellationToken);

        // リスタート：すべてのシーンをアンロードし、System シーンをリロードします。
        await _controller.RestartAsync(progress: null, forceImmediate: false, destroyCancellationToken);
    }
}
```

## API

### `SceneControllerCore<TEnum>`

`TEnum` は基底型が `int` のビットフラグ列挙型である必要があります。

| メンバー | 説明 |
|---------|------|
| `CurrentScene` | 現在アクティブなシーンフラグを取得します。 |
| `LoadAsync(TEnum sceneName, IProgress<float>? progress, CancellationToken cancellationToken)` | 指定フラグで表されるすべてのシーンをロードし、セットに含まれないシーンをアンロードします。 |
| `UnloadAllAsync(IProgress<float>? progress, CancellationToken cancellationToken)` | 現在アクティブなすべてのシーンをアンロードします。 |
| `RestartAsync(IProgress<float>? progress, bool forceImmediate, CancellationToken cancellationToken)` | すべてのシーンをアンロードし、System シーンをリロードします。`forceImmediate` が `true` の場合、セマフォによる同期をスキップします。 |

## ライセンス
このライブラリは、MITライセンスで公開しています。
