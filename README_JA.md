# Transition
[![unity-meta-check](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Transition.svg)](https://github.com/AndanteTribe/Transition/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Transition.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.transition?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.transition/)

[English](README.md) | 日本語

## 概要

**Transition**は、必要なSceneをロードし、不要になったSceneをアンロードするUnity向けライブラリです。

通常のenumで最終的に必要なSceneを指定すると、`SceneCollection<TScene>.SetAsync`が現在の管理状態との差分を計算します。不足Sceneをロードした後、不要Sceneをアンロードします。途中で失敗しても成功済みの処理を保持するため、同じTargetを再指定すると残りだけを再試行できます。

## 必要環境

- Unity 2021.3以降（C# 9 / .NET Standard 2.1）
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10以降
- [Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@1.19/manual/index.html) 1.19.19以降（Addressables追加パッケージを使う場合のみ）

## インストール

UniTaskを先にインストールしてください。`Window > Package Manager > [+] > Add package from git URL`から、次を追加します。

```text
https://github.com/AndanteTribe/Transition.git?path=src/Transition.Unity/Packages/jp.andantetribe.transition
```

Addressablesを使う場合は、Coreの後に次の追加パッケージもインストールします。

```text
https://github.com/AndanteTribe/Transition.git?path=src/Transition.Unity/Packages/jp.andantetribe.transition.addressables
```

独自asmdefを使う場合は`Transition`を参照してください。`AddressablesSceneCollection`や`AddressablesBackend`を使うAssemblyには`Transition.Addressables`の参照も必要です。

## Quick Start

アプリを起動するSceneを`System`として用意し、Collectionの管理対象に含めず、ロードしたままにします。`System`と以下のゲーム用SceneをBuild Settingsに登録してください。

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

        // UIを保持し、GameをロードしてからTitleをアンロードします。
        await _scenes.SetAsync(GameScene.Game, GameScene.UI, cancellationToken);

        // 管理Sceneを解放し、Systemから再スタートします。
        await _scenes.ResetAsync(cancellationToken);
    }
}
```

`SceneCollection.Create<TScene>()`はSceneManagerを使用するCollectionを生成します。規定ではenum名をScene名として使用します。値0も通常のScene IDです。BitFlagは使用しないため、32 Sceneという制限はありません。

CollectionのAPIはUnityのメインスレッドから呼び、返されたUniTaskをawaitしてください。BackendはCollectionごとに一つ用意し、共有やCollection外からの直接操作をしないでください。

## Addressablesでの使用

ゲーム用SceneをAddressablesへ登録し、enum名に一致するAddressを設定します。起動／Reset用SceneはBuild Settingsに置きます。

```csharp
var scenes = AddressablesSceneCollection.Create<GameScene>();

await scenes.SetAsync(GameScene.Game, GameScene.UI, cancellationToken);
```

Addressがenum名と異なる場合は変換関数を指定できます。

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

Addressは、それぞれ異なる単一Sceneを指すようにしてください。複数Sceneを指すLabelは使用しません。異なるenum値が同じ物理Sceneを指す場合、自動では重複除去しません。

## SceneManagerでの使用

`SceneManagerBackend`はAdditiveロードを行い、取得したUnity SceneをBackend内部で管理します。変換関数でBuild Settingsのフルパスを指定できます。

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

同名Sceneが複数ある場合、短いScene名での指定はエラーになります。フルパスを指定してください。既にロード済みのSceneは取り込みません。Backendが所有する物理Sceneを、外部から重ねてロード／アンロードしないでください。

## Scene指定と現在状態

1〜5 Sceneには固定引数overloadがあり、最後に省略可能なCancellationTokenを渡せます。

```csharp
await scenes.SetAsync(GameScene.Game);
await scenes.SetAsync(GameScene.Game, GameScene.UI, cancellationToken);

// 重複は最初の出現だけ採用します。
await scenes.SetAsync(GameScene.Game, GameScene.UI, GameScene.Game);

// 空Target：管理Sceneをすべて解放します。起動Sceneは残ります。
await scenes.SetAsync(cancellationToken);

bool hasUI = scenes.Contains(GameScene.UI);
int count = scenes.Count;
GameScene[] snapshot = scenes.GetCurrentScenes();
```

`GetCurrentScenes()`は、ロード成功の完了順に並んだ独立した配列を返します。内部Listやlive viewは公開しません。

大きなTargetや事前定義したTargetには`ReadOnlyMemory<TScene>`を使えます。

```csharp
GameScene[] target = { GameScene.Game, GameScene.UI };
await scenes.SetAsync(target, cancellationToken);
```

固定引数版は入力をstruct内へ直接保持し、呼び出しごとのparams配列やHashSetを作りません。Memory版も5件までは直接保持します。6件以上は待機に入る前に専用配列へコピーするため、呼び出し後に元配列を変更しても処理中のTargetは変わりません。遷移やBackend全体のゼロallocationを保証するものではありません。

## Sequential / Parallel

生成時に一度だけ設定します。

```csharp
var scenes = AddressablesSceneCollection.Create<GameScene>(
    loadingMode: SceneLoadingMode.Parallel);
```

- `Sequential`（規定）：不足SceneをTargetの指定順にロードします。先にロードしたSceneへの依存がある場合に使います。
- `Parallel`：一回のSet内の不足Sceneを並列ロードします。一件が失敗／キャンセルされても、開始済み操作をすべて待ちます。
- 同じCollectionへのSet／Reset呼び出し自体は常に直列化します。
- アンロードはロード成功の完了順を逆にした順番で、一件ずつ実行します。
- Load段階で失敗／キャンセルされた場合、Unload段階には進みません。Unload失敗時も、それ以降のUnloadを止めます。

ParallelではSceneの初期化順を保証しません。複数の失敗は`AggregateException`、キャンセルだけの場合は`OperationCanceledException`として返します。

## 失敗とCancellation

成功済みの処理を保持し、遷移全体のrollbackはしません。

例えば現在が`Title + UI`、Targetが`Game + UI + Audio`のとき、Gameのロード成功後にAudioが失敗すると`Title + UI + Game`が残ります。同じTargetを再指定すれば、Audioだけ再ロードしてからTitleをアンロードします。

Cancellationは、まだ開始していない操作の開始を止めます。開始済み操作はBackendが安定状態に到達するまで待ちます。標準Backendでは、開始済みの物理操作が完了した場合、キャンセル要求が届いていても成功として返します。Collectionはその成功を状態へ反映してからキャンセルを通知します。

ParallelでGameとAudioが成功、UIがキャンセルなら、GameとAudioを管理状態へ残します。古いSceneをアンロードせず、Setはキャンセルとして終了します。例外をcatchした後も、`Contains`・`Count`・`GetCurrentScenes()`で現在状態を確認できます。

## Reset

`ResetAsync`はSetと同じ排他を使います。最初に初期SceneがBuild Settingsにあるかを確認し、Backend経由で管理Sceneをアンロードしてから、Collectionが直接`SceneManager.LoadSceneAsync(..., LoadSceneMode.Single)`を呼びます。

```csharp
var scenes = SceneCollection.Create<GameScene>(initialSceneName: "Boot");

await scenes.ResetAsync(cancellationToken);
```

成功後、Collectionの管理状態は空です。Boot／Systemは管理対象外として残り、同じCollection／Backendを再利用できます。

Singleロード開始前まではCancellationを尊重します。開始後は完了まで待ち、その間にTokenがキャンセルされてもResetは成功します。それ以前のUnloadで失敗／キャンセルされた場合、まだ所有しているSceneを再試行のために残します。

SingleロードはCollection外のSceneを含めてUnity全体へ影響します。Resetはアプリ全体の再起動として使い、別Collectionや外部Scene操作と同時実行しないでください。

Unityは最後のSceneをアンロードできません。空TargetやResetを使うため、起動Sceneを管理対象外でロードしたままにしてください。[UnityのUnloadSceneAsync API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/SceneManagement.SceneManager.UnloadSceneAsync.html)

## Custom Backend

FactoryはCollectionごとに専用の標準Backendを生成します。独自Backendを渡す場合は、`new SceneCollection<GameScene>(backend)`のようにコンストラクタを直接使ってください。

次の2メソッドを実装します。

```csharp
public interface ISceneBackend<TScene> where TScene : struct, System.Enum
{
    UniTask LoadAsync(TScene scene, CancellationToken cancellationToken = default);
    UniTask UnloadAsync(TScene scene, CancellationToken cancellationToken = default);
}
```

Backendは一回の呼び出しで一つのSceneだけを扱い、その物理Scene／resourceの所有権を保持します。Target、重複除去、順序、排他、ResetはCollectionが担当します。

契約は次のとおりです。

| 結果 | 保証する状態 |
|------|-------------|
| Load成功 | Sceneが完全にロード済みで、Backendが所有している。 |
| Load失敗／キャンセル | その操作による新しい所有権が残っておらず、同じIDを再試行できる。 |
| Unload成功 | Sceneが完全にアンロード済みで、所有権も解放されている。 |
| Unload失敗／キャンセル | ロード済みSceneと所有権が維持され、再試行できる。 |

安全に止められる区間ではTokenを使用して構いません。キャンセルできない地点を越えたら完了まで待って成功を返します。awaitだけをキャンセルし、裏で物理操作を継続させてはいけません。既にUnloadが成功した後でCancellationをthrowすることもしません。

途中停止できないエンジンAPIでは、次の流れになります。

```csharp
cancellationToken.ThrowIfCancellationRequested();
var operation = StartPhysicalLoad(scene);
// ここからは安全にキャンセルできない。
await operation.ToUniTask(cancellationToken: CancellationToken.None);
// Backend内部の所有情報へ記録してから成功を返す。
```

`StartPhysicalLoad`は独自エンジン／resource APIを表す仮の名前です。必要な所有情報の格納領域は物理操作前に確保し、Load失敗時の後始末を終えてからエラーを返してください。

Parallelでは異なるIDのLoadが同時に呼ばれます。Backendから自分のCollectionのSet／Resetを呼んでawaitすると、取得中の排他を再度待つためデッドロックします。Unityに関わる所有情報の操作はメインスレッド上で完了させてください。

## API

| メンバー | 説明 |
|----------|------|
| `SceneCollection.Create<TScene>(getSceneName = null, initialSceneName = "System", loadingMode = Sequential)` | SceneManagerを使用するCollectionを生成する。 |
| `AddressablesSceneCollection.Create<TScene>(getSceneName = null, initialSceneName = "System", loadingMode = Sequential)` | Addressablesを使用するCollectionを生成する。 |
| `new SceneCollection<TScene>(backend, initialSceneName = "System", loadingMode = Sequential)` | Collectionを生成する。 |
| `SetAsync(scene1, ..., scene5, cancellationToken = default)` | 1〜5件の固定引数overload。 |
| `SetAsync(ReadOnlyMemory<TScene>, cancellationToken = default)` | 任意サイズのTarget。 |
| `SetAsync(cancellationToken = default)` | 空Targetへ遷移する。 |
| `ResetAsync(cancellationToken = default)` | 所有Sceneを解放し、初期Sceneを再ロードする。 |
| `Count` / `Contains(scene)` | 部分成功を含む現在の所有状態を確認する。 |
| `GetCurrentScenes()` | 独立した配列snapshotを取得する。 |

## ライセンス

このライブラリはMITライセンスで公開されています。
