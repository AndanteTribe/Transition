#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Transition
{
    /// <summary>
    /// Loads a scene and returns its loaded lifetime.
    /// </summary>
    /// <typeparam name="TScene">The type used to identify scenes.</typeparam>
    public interface ISceneLoader<TScene>
    {
        /// <summary>
        /// Loads a scene asynchronously.
        /// </summary>
        /// <param name="scene">Scene to load.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A lifetime owned by the caller. A successfully returned lifetime must be unloaded once.
        /// </returns>
        UniTask<ISceneHandle> LoadAsync(
            TScene scene,
            IProgress<float>? progress,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Represents ownership of a loaded scene. Its owner must unload it once.
    /// </summary>
    public interface ISceneHandle
    {
        /// <summary>
        /// Unloads the scene asynchronously.
        /// </summary>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        UniTask UnloadAsync(IProgress<float>? progress, CancellationToken cancellationToken);
    }
}