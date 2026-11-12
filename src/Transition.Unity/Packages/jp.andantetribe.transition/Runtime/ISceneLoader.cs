using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Transition
{
    /// <summary>
    /// Loads and unloads individual scenes, retaining their Unity scenes or Addressables handles until unloaded.
    /// </summary>
    /// <remarks>
    /// Give each SceneGroupLoader its own scene loader. Calls for different identifiers may overlap in Parallel mode.
    /// Call on Unity's main thread and complete ownership changes on that thread; callers are responsible for this.
    /// Check cancellation at the start of each operation before changing ownership or starting Unity work.
    /// Load success means the scene is fully loaded and owned; failure or cancellation must leave no new ownership.
    /// Unload success means the scene and its ownership are gone; failure or cancellation must preserve the
    /// loaded scene and ownership for retry. Never finish an await while an unobserved scene operation continues.
    /// Once cancellation is unsafe, finish the operation and return success even if the token was canceled.
    /// Do not call back into the owning SceneGroupLoader and await it: it holds its transition gate.
    /// </remarks>
    public interface ISceneLoader
    {
        /// <summary>Loads one scene and acquires its ownership.</summary>
        UniTask LoadAsync<TEnum>(TEnum scene, CancellationToken cancellationToken = default)
            where TEnum : Enum;

        /// <summary>Unloads one scene and releases its ownership.</summary>
        UniTask UnloadAsync<TEnum>(TEnum scene, CancellationToken cancellationToken = default)
            where TEnum : Enum;
    }
}