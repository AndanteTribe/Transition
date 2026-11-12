# Transition

[![unity-meta-check](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/Transition/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Transition.svg)](https://github.com/AndanteTribe/Transition/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Transition.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.transition?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.transition/)

[English](README.md) | 日本語

## 概要

Transition は、SceneManager または Addressables を使って Unity のシーングループを管理するライブラリです。enum 値から `SceneGroup` を作ってロードすると、不足しているシーンを追加し、不要になったシーンをアンロードします。異なる enum 型の値も同じグループに含められます。

## 必要環境

- Unity 2021.3 以降（C# 9 / .NET Standard 2.1）
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 以降
- [UniTaskPlus](https://github.com/AndanteTribe/UniTaskPlus) 1.1.1 以降
- [Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@1.19/manual/index.html) 1.19.19 以降（SceneManager 使用時も必要）

## インストール

`Window > Package Manager` を開き、`[+] > Add package from git URL` から次の順に追加します。

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

Addressables は Transition の依存パッケージとしてインストールされます。独自の asmdef を使う場合は `Transition` を参照してください。

## Quick Start

起動用のシーンは Transition の管理対象に含めず、ロードしたままにします。ここでは `System` シーンから起動し、`System`・`Game`・`GameUI` を Build Settings に登録します。

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

ゲーム側で `GameFlow` を生成し、`LoadGameAsync` を呼び出してください。既定では enum のメンバー名をシーン名として使います。構築したグループは不変で、繰り返し利用できます。`Build` 後は Builder を破棄して作業用リストをプールに返してください。別のグループをロードすると、共通シーンを維持し、不足分をロードしてから不要な管理シーンをアンロードします。

型付きの `Add`・`Contains` と標準 SceneLoader は、enum 値を初回にボックス化し、以後は共有キャッシュを再利用します。キャッシュに固定の保持数上限はなく、異なる値を渡すほど保持メモリが増えます。Builder・グループの生成やシーン操作では別途アロケーションが発生し得ます。

## Addressables とシーン名の指定

ゲーム用シーンを Addressables に登録し、enum 名と同じ Address を設定します。名前が異なる場合は変換関数を指定できます。

```csharp
var scenes = SceneGroupLoader.Create.Addressables(
    getSceneAddress: scene => $"Scenes/{scene}",
    loadingMode: SceneLoadingMode.Parallel);
```

`SceneGroupLoader.Create.SceneManager` でも `getSceneName` で名前やパスを変換できます。各識別子は異なる物理シーンに対応させてください。異なる enum 型に同名のメンバーがある場合、別シーンを指定するには変換関数で区別します。Addressables のキーには、複数シーンを指すラベルではなく個別のシーンを指定してください。

ロード順は `Sequential`（既定）と `Parallel` を選べます。Parallel ではシーンの初期化順は保証されません。

## 失敗とキャンセル

失敗・キャンセル時も、完了済みの変更は維持されます。遷移全体のロールバックは行いません。ロードに失敗した場合、古いシーンはアンロードされません。同じグループを再指定すると、残りの処理を再試行できます。現在の管理状態は `Contains`・`Count`・`CurrentScenes` で確認できます。

キャンセルは未開始の処理を止めます。標準 Loader は開始済みの物理操作の完了を待ち、結果を管理状態に反映してからキャンセルを通知します。Parallel では開始済みのロードをすべて待ちます。

Parallel のロード失敗は、件数に関係なく `AggregateException` で通知します。キャンセルと失敗が混在した場合は両方が `InnerExceptions` に含まれ、キャンセルのみの場合は `OperationCanceledException` が通知されます。逐次ロードやアンロードの失敗は元の例外を通知します。

## API

| メンバー | 説明 |
|----------|------|
| `SceneGroupLoader.Create.SceneManager(...)` / `Create.Addressables(...)` | 使用する方式に応じた Loader を作成する。 |
| `new SceneGroupBuilder()` | Builder を作成する。使用後は破棄する。 |
| `builder.Add(scene)` / `builder.Build()` | シーンを追加する／不変のグループを作成する。 |
| `LoadAsync(group, cancellationToken)` | 指定グループへ遷移する。`SceneGroup.Empty` または `null` は管理シーンをすべてアンロードする。 |
| `Count` / `Contains(scene)` / `CurrentScenes` | 現在の管理状態を取得する。`CurrentScenes` は読み取り専用の live view。 |
| `ResetAsync(cancellationToken)` | 管理シーンをアンロードし、初期シーンを Single モードでロードする。 |

初期シーン名の既定値は `System` です。変更する場合は Loader の作成時に `initialSceneName` へ Build Settings のシーン名またはパスを指定してください。`ResetAsync` は Loader 外のシーンを含む Unity 全体に影響します。Single ロード開始後はキャンセル要求があっても完了を待ち、成功を返します。

Transition の API は Unity のメインスレッドから呼び出してください。`SceneGroupLoader` ごとに専用の SceneLoader を使用し、管理シーンを外部からロード／アンロードしないでください。Unity は最後のシーンをアンロードできないため、起動シーンは管理対象外でロードしたままにします。

## 独自 SceneLoader

独自の `ISceneLoader` を使う場合は `new SceneGroupLoader(sceneLoader)` に渡します。`where TEnum : System.Enum` を付けた `LoadAsync<TEnum>` と `UnloadAsync<TEnum>` を実装してください。`EnumBoxCache.Get(scene)` でボックス化済みの識別子を再利用できます。

操作開始前にキャンセルを確認し、ロード成功時の所有情報はアンロード成功まで保持します。失敗・キャンセル時は再試行できる所有状態を維持し、開始済みの物理操作は完了まで監視してください。SceneLoader から自身の GroupLoader を呼び出して await することはできません。Parallel では異なる識別子のロードが重なる場合があります。

## ライセンス

このライブラリは MIT ライセンスの下で公開されています。
