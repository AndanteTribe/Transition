using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Transition
{
    /// <summary>
    /// Loads or unloads one scene per operation and owns its backend-specific resources.
    /// </summary>
    /// <remarks>
    /// Give each collection its own backend. Calls for different identifiers may overlap in Parallel mode.
    /// Load success means the scene is fully loaded and owned; failure or cancellation must leave no new ownership.
    /// Unload success means the scene and its ownership are gone; failure or cancellation must preserve the
    /// loaded scene and ownership for retry. Never finish an await while an unobserved scene operation continues.
    /// Once cancellation is unsafe, finish the operation and return success even if the token was canceled.
    /// Do not call back into the owning collection and await it: the collection holds its transition gate.
    /// </remarks>
    /// <typeparam name="TScene">An ordinary enum identifying scenes.</typeparam>
    public interface ISceneBackend<TScene> where TScene : struct, Enum
    {
        /// <summary>Loads one scene and acquires its ownership.</summary>
        UniTask LoadAsync(TScene scene, CancellationToken cancellationToken = default);

        /// <summary>Unloads one scene and releases its ownership.</summary>
        UniTask UnloadAsync(TScene scene, CancellationToken cancellationToken = default);
    }
}