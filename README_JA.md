# Transition
[![unity-meta-check](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Transition.svg)](https://github.com/AndanteTribe/Transition/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Transition.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.transition?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.transition/)

[English](README.md) | 日本語

## 概要
**Transition** は、AddressablesまたはUnityの`SceneManager`を使用できるシーン遷移システムです。

利用者が最終的に必要なシーン集合を宣言すると、`SceneControllerCore<TScene>`が現在管理しているシーンとの差分を求め、不足しているシーンをロードし、不要になったシーンをアンロードします。これらを1回の`LoadAsync`で実行します。

シーン識別子には、通常の列挙型、ビットフラグ列挙型、文字列、独自の値型を使用できます。通常の目標集合への遷移では、AddressablesやSceneManager固有のロード・アンロード処理をそれぞれのLoaderが担当し、Coreの差分処理から分離しています。

## 要件
- Unity 2021.3 以上
- [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.19.19 以上
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 以上
- [UniTaskPlus](https://github.com/AndanteTribe/UniTaskPlus) 0.1.0 以上

## インストール
`Window > Package Manager`からPackage Managerウィンドウを開き、`[+] > Add package from git URL`を選択して以下のURLを入力します。

```
https://github.com/AndanteTribe/Transition.git?path=src/Transition.Unity/Packages/jp.andantetribe.transition
```

## セットアップ

`RestartAsync`を使用する場合は、**`"System"`という名前のシーン**を`File > Build Settings`へ追加してください。`RestartAsync`は、このシーンをアプリケーションの初期シーンとしてSingleモードでロードします。

- Addressablesを使用する場合は、対象シーンをAddressablesへ登録します。
- `SceneManager`を使用する場合は、対象シーンを`File > Build Settings`へ追加します。
- 独自asmdefで参照先を明示している場合は、`Transition`を参照します。`AddressablesSceneController`を使用する場合は、`Transition.Addressables`も参照します。

## クイックスタート

次の例では、ビットフラグ形式の識別子とAddressablesを使用します。

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Transition;

// Flags用Controllerでは、[Flags]とintの基底型が必要です。
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
        // GameとHUDを加算ロードし、この集合に含まれない管理中Sceneをアンロードします。
        await _controller.LoadAsync(
            progress: null,
            cancellationToken,
            SceneName.Game | SceneName.HUD);

        // GameとHUDをアンロードし、Titleへ遷移します。
        await _controller.LoadAsync(
            progress: null,
            cancellationToken,
            SceneName.Title);

        // Flags用Controllerでも、現在状態はSceneSetとして公開されます。
        var currentFlags = _controller.CurrentScenes.ToFlags();

        await _controller.UnloadAllAsync(progress: null, cancellationToken);

        // 管理中Sceneをアンロードし、SystemをSingleモードでリロードします。
        await _controller.RestartAsync(
            progress: null,
            forceImmediate: false,
            cancellationToken);
    }
}
```

## Addressables

### 通常のシーン識別子を使用する

各列挙値がビットの集合ではなく、1つのシーンを表す場合は`CreateScenes`を使用します。`SceneSet`を手動で作らなくても、複数の遷移先をそのまま指定できます。

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

既定では`ToString()`の結果をAddressablesのAddressとして使用します。論理的なシーン識別子とAddressablesのAddressが異なる場合は、変換関数を渡します。

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

Build Settingsに登録されたシーンをAddressables APIなしでロードする場合は、`SceneManagerController`を使用します。

```csharp
private readonly SceneControllerCore<GameScene> _controller =
    SceneManagerController.CreateScenes<GameScene>();

await _controller.LoadAsync(
    cancellationToken,
    GameScene.Stage100,
    GameScene.UserInterface);
```

既定では`ToString()`の結果をシーン名として使用します。変換関数では、Build Settingsに登録されたシーン名またはパスを返せます。

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

Build Settingsのシーンでもビットフラグ形式を使いたい場合は、`SceneManagerController.CreateFlags<TEnum>()`を使用できます。

## ビットフラグ

ビットフラグを使用すると、複数シーンを簡潔に指定できます。

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

Flags用列挙型は、次の条件を満たす必要があります。

- `[Flags]`を付ける
- 基底型を`int`にする
- 空の状態には`0`を使用する
- 個別のシーンごとに1bitを割り当てる

`int`は32bitであるため、ビットフラグ形式で表現できる個別値は最大32個です。この制限があるのはFlags形式だけであり、`CreateScenes`と`SceneSet<TScene>`では、より大きなシーン集合を扱えます。

明示的に相互変換する場合は、`SceneSet.FromFlags`と`ToFlags`を使用します。

```csharp
SceneSet<SceneName> target =
    SceneSet.FromFlags(SceneName.Game | SceneName.HUD);

SceneName flags = target.ToFlags();
```

`SceneSet.Of(SceneName.Game | SceneName.HUD)`は、複合値を1つの識別子として格納し、個別のフラグへ分解しません。フラグを分解するときは`SceneSet.FromFlags`を使用してください。

## SceneSet

`SceneSet<TScene>`は、最初に指定されたロード順を維持しながら、重複した識別子を取り除くイミュータブルなコレクションです。

```csharp
SceneSet<GameScene> target = SceneSet.Of(
    GameScene.Stage100,
    GameScene.UserInterface);

await _controller.LoadAsync(
    target,
    progress: null,
    cancellationToken);
```

再利用するシーン集合へ識別子を追加するときは、`With`を使用します。元の集合は変更されません。

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

空の遷移先を明示する場合は、`SceneSet<TScene>.Empty`を使用します。空の集合を`LoadAsync`へ渡すと、そのControllerが管理しているすべてのシーンをアンロードします。

```csharp
await _controller.LoadAsync(
    SceneSet<GameScene>.Empty,
    progress: null,
    cancellationToken);
```

`CurrentScenes`に含まれ、差分処理の対象になるのは、そのControllerを通してロードしたシーンだけです。外部でロードまたはアンロードされたシーンとは自動的に同期しません。

## 独自Scene Loader

別のロード方式を追加する場合は、`ISceneLoader<TScene>`を実装します。ロードに成功すると、ロード済みシーンの所有権を表す`ISceneHandle`を返します。Controllerは後でこのHandleを使用して、対応するシーンをアンロードします。次のコードは実装の骨組みであり、実際のロード処理とHandleは使用するBackendに合わせて実装します。

```csharp
public sealed class CustomSceneLoader : ISceneLoader<GameScene>
{
    public UniTask<ISceneHandle> LoadAsync(
        GameScene scene,
        IProgress<float>? progress,
        CancellationToken cancellationToken)
    {
        // 1つのシーンをロードし、そのライフタイムを返します。
        throw new NotImplementedException();
    }
}

var controller = new SceneControllerCore<GameScene>(new CustomSceneLoader());
```

Core APIはAddressablesのAddress、`AsyncOperationHandle`、`SceneInstance`、解放用Keyを要求しません。ロード方式固有の所有権は、返された`ISceneHandle`の内部に保持します。

## Cancellation

組み込みLoaderは、シーンのロードを開始する前にCancellationを確認します。UnityまたはAddressablesのロード開始後は、物理的な処理と必要なロールバックを完了してからCancellationを通知します。アンロード開始後についても、物理的な処理の完了を待ってからCancellationを通知します。

## 利用上の注意

- 通常のController操作は直列化されます。`RestartAsync(forceImmediate: true)`はこの直列化を省略するため、同じControllerの別操作と同時に実行しないでください。通常の遷移では`false`を使用します。
- `IProgress<float>`は、個別のシーン操作ごとの進捗を通知します。複数シーンを扱う場合、遷移全体を表す単調増加の進捗値にはなりません。
- 異なる論理識別子が同じ物理シーンへ変換されても、自動的に重複とは判断されません。
- SceneManager用Loaderでは、Unityの`AsyncOperation`から生成された`Scene`を直接取得できないため、別のLoaderから同じシーンを同時にロードする使い方には対応していません。

## API

### Controller Factory

| メンバー | 説明 |
|---------|------|
| `AddressablesSceneController.CreateFlags<TEnum>(getSceneName?)` | ビットフラグを分解するAddressables用Controllerを作成します。 |
| `AddressablesSceneController.CreateScenes<TScene>(getSceneName)` | 通常のシーン識別子を扱うAddressables用Controllerを作成します。列挙型用Overloadでは`ToString()`を使用します。 |
| `SceneManagerController.CreateFlags<TEnum>(getSceneName?)` | ビットフラグを分解するSceneManager用Controllerを作成します。 |
| `SceneManagerController.CreateScenes<TScene>(getSceneName)` | 通常のシーン識別子を扱うSceneManager用Controllerを作成します。列挙型用Overloadでは`ToString()`を使用します。 |

### `SceneControllerCore<TScene>`

| メンバー | 説明 |
|---------|------|
| `CurrentScenes` | このControllerが現在管理しているシーンを取得します。 |
| `LoadAsync(params TScene[] targetScenes)` | 管理中のシーンを指定された識別子の集合へ遷移させます。 |
| `LoadAsync(CancellationToken cancellationToken, params TScene[] targetScenes)` | Cancellationを受け取り、管理中のシーンを指定された識別子の集合へ遷移させます。 |
| `LoadAsync(IProgress<float>? progress, CancellationToken cancellationToken, params TScene[] targetScenes)` | 個別のシーン操作の進捗を通知しながら、複数の識別子へ遷移させます。 |
| `LoadAsync(SceneSet<TScene> targetScenes, IProgress<float>? progress, CancellationToken cancellationToken)` | 明示的に作成したシーン集合へ遷移させます。 |
| `UnloadAllAsync(IProgress<float>? progress, CancellationToken cancellationToken)` | このControllerが管理しているすべてのシーンをアンロードします。 |
| `RestartAsync(IProgress<float>? progress, bool forceImmediate, CancellationToken cancellationToken)` | 管理中のシーンをアンロードし、`System`をSingleモードでリロードします。`forceImmediate: true`では処理の直列化を省略します。 |

### `SceneSet`

| メンバー | 説明 |
|---------|------|
| `SceneSet.Of(params TScene[] scenes)` | 順序を維持し、重複を取り除いた識別子の集合を作成します。 |
| `sceneSet.With(params TScene[] scenes)` | 元の集合を変更せず、識別子を追加した新しい集合を作成します。 |
| `SceneSet.FromFlags(params TEnum[] flags)` | `int`を基底型とする列挙フラグを個別の識別子へ分解します。 |
| `sceneSet.ToFlags()` | Flags用SceneSetを1つの列挙値へ集約します。 |
| `SceneSet<TScene>.Empty` | 空の遷移先を取得します。 |

## ライセンス
このライブラリは、MITライセンスで公開しています。
