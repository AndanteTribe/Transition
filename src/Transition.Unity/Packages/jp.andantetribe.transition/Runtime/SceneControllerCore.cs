#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UniTaskPlus;
using UnityEngine.SceneManagement;

namespace Transition
{
    /// <summary>
    /// Loads a requested collection of scenes while unloading managed scenes that are no longer requested.
    /// </summary>
    /// <typeparam name="TScene">The type used to identify scenes.</typeparam>
    public class SceneControllerCore<TScene> where TScene : notnull
    {
        // Define "restart" as loading the System scene in Single mode.
        private const string DefaultSceneName = "System";

        private readonly ISceneLoader<TScene> _sceneLoader;
        private readonly Func<TScene[], SceneSet<TScene>> _createSceneSet;
        private readonly List<SceneInfo> _activeScenes = new(4);
        private readonly UniTaskSemaphore _semaphore = new(1, 1);

        /// <summary>
        /// Gets the current set of managed scenes.
        /// </summary>
        public SceneSet<TScene> CurrentScenes { get; private set; } = SceneSet<TScene>.Empty;

        /// <summary>
        /// Initialize a new instance of <see cref="SceneControllerCore{TScene}"/>.
        /// </summary>
        /// <param name="sceneLoader">Scene loader.</param>
        public SceneControllerCore(ISceneLoader<TScene> sceneLoader)
            : this(sceneLoader, static scenes => SceneSet.Of(scenes))
        {
        }

        /// <summary>
        /// Initialize a new instance of <see cref="SceneControllerCore{TScene}"/>.
        /// </summary>
        /// <param name="sceneLoader">Scene loader.</param>
        /// <param name="createSceneSet">Function to convert requested values into individual scene identifiers.</param>
        public SceneControllerCore(
            ISceneLoader<TScene> sceneLoader,
            Func<TScene[], SceneSet<TScene>> createSceneSet)
        {
            _sceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
            _createSceneSet = createSceneSet ?? throw new ArgumentNullException(nameof(createSceneSet));
        }

        /// <summary>
        /// Loads the requested scenes and unloads managed scenes that are not requested.
        /// </summary>
        /// <param name="targetScenes">Target scenes in their preferred load order.</param>
        public UniTask LoadAsync(params TScene[] targetScenes) =>
            LoadAsync(
                CreateSceneSet(targetScenes),
                progress: null,
                cancellationToken: CancellationToken.None);

        /// <summary>
        /// Loads the requested scenes and unloads managed scenes that are not requested.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="targetScenes">Target scenes in their preferred load order.</param>
        public UniTask LoadAsync(CancellationToken cancellationToken, params TScene[] targetScenes) =>
            LoadAsync(
                CreateSceneSet(targetScenes),
                progress: null,
                cancellationToken: cancellationToken);

        /// <summary>
        /// Loads the requested scenes and unloads managed scenes that are not requested.
        /// </summary>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="targetScenes">Target scenes in their preferred load order.</param>
        public UniTask LoadAsync(
            IProgress<float>? progress,
            CancellationToken cancellationToken,
            params TScene[] targetScenes) =>
            LoadAsync(CreateSceneSet(targetScenes), progress, cancellationToken);

        /// <summary>
        /// Loads the requested scene set and unloads managed scenes that are not requested.
        /// </summary>
        /// <param name="targetScenes">Target scene set.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async UniTask LoadAsync(
            SceneSet<TScene> targetScenes,
            IProgress<float>? progress,
            CancellationToken cancellationToken)
        {
            if (targetScenes is null)
            {
                throw new ArgumentNullException(nameof(targetScenes));
            }

            using var _ = await _semaphore.WaitScopeAsync(cancellationToken);
            using var __ = ArrayPool<SceneInfo>.Shared.Rent(_activeScenes.Count, out var array);
            _activeScenes.CopyTo(array);
            var activeScenes = new ArraySegment<SceneInfo>(array, 0, _activeScenes.Count);

            try
            {
                foreach (var scene in targetScenes)
                {
                    if (!CurrentScenes.Contains(scene))
                    {
                        var sceneHandle = await _sceneLoader.LoadAsync(scene, progress, cancellationToken);
                        var info = new SceneInfo(scene, sceneHandle);
                        _activeScenes.Add(info);
                    }
                }

                await using (var bag = new UniTaskBag())
                {
                    foreach (var info in activeScenes)
                    {
                        if (!targetScenes.Contains(info.SceneName))
                        {
                            bag.Add(info.SceneHandle.UnloadAsync(progress, cancellationToken));
                            _activeScenes.Remove(info);
                        }
                    }
                }

                CurrentScenes = targetScenes;
            }
            catch (Exception)
            {
                // If loading fails, recompute the currently managed scenes.
                CurrentScenes = CreateCurrentSceneSet();
                throw;
            }
        }

        /// <summary>
        /// Unloads all scenes.
        /// </summary>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async UniTask UnloadAllAsync(IProgress<float>? progress, CancellationToken cancellationToken)
        {
            using var _ = await _semaphore.WaitScopeAsync(cancellationToken);
            await UnloadAllCoreAsync(progress, cancellationToken);
        }

        /// <summary>
        /// Restarts the application.
        /// </summary>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="forceImmediate">Whether to skip semaphore synchronization.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async UniTask RestartAsync(
            IProgress<float>? progress,
            bool forceImmediate,
            CancellationToken cancellationToken)
        {
            if (forceImmediate)
            {
                await UnloadAllCoreAsync(progress, cancellationToken);
                await SceneManager.LoadSceneAsync(DefaultSceneName)!.ToUniTask(progress!, cancellationToken: cancellationToken);
            }
            else
            {
                using var _ = await _semaphore.WaitScopeAsync(cancellationToken);
                await UnloadAllCoreAsync(progress, cancellationToken);
                await SceneManager.LoadSceneAsync(DefaultSceneName)!.ToUniTask(progress!, cancellationToken: cancellationToken);
            }
        }

        private SceneSet<TScene> CreateSceneSet(TScene[] targetScenes)
        {
            if (targetScenes is null)
            {
                throw new ArgumentNullException(nameof(targetScenes));
            }

            return _createSceneSet(targetScenes) ??
                   throw new InvalidOperationException("The scene set converter returned null.");
        }

        private async UniTask UnloadAllCoreAsync(IProgress<float>? progress, CancellationToken cancellationToken)
        {
            try
            {
                await using var bag = new UniTaskBag();
                foreach (var info in _activeScenes)
                {
                    bag.Add(info.SceneHandle.UnloadAsync(progress, cancellationToken));
                }
                _activeScenes.Clear();
            }
            finally
            {
                CurrentScenes = SceneSet<TScene>.Empty;
            }
        }

        private SceneSet<TScene> CreateCurrentSceneSet()
        {
            var scenes = new TScene[_activeScenes.Count];
            for (var i = 0; i < _activeScenes.Count; i++)
            {
                scenes[i] = _activeScenes[i].SceneName;
            }

            return SceneSet<TScene>.Create(scenes);
        }

        private readonly struct SceneInfo : IEquatable<SceneInfo>
        {
            public readonly TScene SceneName;

            public readonly ISceneHandle SceneHandle;

            public SceneInfo(TScene sceneName, ISceneHandle sceneHandle)
            {
                SceneName = sceneName;
                SceneHandle = sceneHandle;
            }

            /// <inheritdoc />
            public bool Equals(SceneInfo other) => EqualityComparer<TScene>.Default.Equals(SceneName, other.SceneName);

            /// <inheritdoc />
            public override int GetHashCode() => EqualityComparer<TScene>.Default.GetHashCode(SceneName);
        }
    }
}
