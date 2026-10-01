#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Transition
{
    /// <summary>Creates collections backed by Unity's SceneManager.</summary>
    public static class SceneCollection
    {
        /// <summary>Creates a collection with its own SceneManager backend.</summary>
        /// <param name="getSceneName">Maps scene identifiers to Build Settings names or paths. Defaults to enum names.</param>
        /// <param name="initialSceneName">Build Settings scene name or path loaded by Reset.</param>
        /// <param name="loadingMode">Order of missing-scene loads within each Set call.</param>
        public static SceneCollection<TScene> Create<TScene>(Func<TScene, string>? getSceneName = null,
            string initialSceneName = "System", SceneLoadingMode loadingMode = SceneLoadingMode.Sequential)
            where TScene : struct, Enum =>
            new(new SceneManagerBackend<TScene>(getSceneName), initialSceneName, loadingMode);
    }

    /// <summary>
    /// Converges the scenes it owns to a requested target, retaining successful work after a failure.
    /// </summary>
    /// <remarks>
    /// Use on Unity's main thread, with an exclusively owned backend. Set and Reset are serialized.
    /// Scenes loaded outside this collection, including the initial bootstrap scene, are not adopted.
    /// Keep a bootstrap scene loaded: Unity cannot unload its last scene.
    /// </remarks>
    /// <typeparam name="TScene">An ordinary enum identifying scenes. Zero is a scene, not an empty target.</typeparam>
    public sealed class SceneCollection<TScene> where TScene : struct, Enum
    {
        private readonly ISceneBackend<TScene> _backend;
        private readonly string _initialSceneName;
        private readonly SceneLoadingMode _loadingMode;
        private readonly List<TScene> _currentScenes = new(5);
        private readonly List<UniTask<Exception?>> _pendingLoads = new(5);
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        /// <summary>Gets the number of successfully loaded, still-owned scenes.</summary>
        public int Count => _currentScenes.Count;

        /// <summary>Creates a collection with its own backend and fixed loading mode.</summary>
        /// <param name="backend">An empty backend used exclusively by this collection.</param>
        /// <param name="initialSceneName">Build Settings scene name or path loaded by Reset.</param>
        /// <param name="loadingMode">Order of missing-scene loads within each Set call.</param>
        public SceneCollection(ISceneBackend<TScene> backend, string initialSceneName = "System",
            SceneLoadingMode loadingMode = SceneLoadingMode.Sequential)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _initialSceneName = !string.IsNullOrWhiteSpace(initialSceneName)
                ? initialSceneName
                : throw new ArgumentException("An initial scene name is required.", nameof(initialSceneName));
            _loadingMode = loadingMode is SceneLoadingMode.Sequential or SceneLoadingMode.Parallel
                ? loadingMode
                : throw new ArgumentOutOfRangeException(nameof(loadingMode));
        }

        /// <summary>Determines whether a scene is currently owned.</summary>
        public bool Contains(TScene scene) => _currentScenes.Contains(scene);

        /// <summary>Returns an independent snapshot in successful-load completion order.</summary>
        public TScene[] GetCurrentScenes() => _currentScenes.ToArray();

        /// <summary>Sets an empty target, unloading every managed scene.</summary>
        public UniTask SetAsync(CancellationToken cancellationToken = default) =>
            SetCoreAsync(default, cancellationToken);

        /// <summary>Sets a target of one scene.</summary>
        public UniTask SetAsync(TScene scene1, CancellationToken cancellationToken = default) =>
            SetCoreAsync(new SceneTarget<TScene>(1, scene1), cancellationToken);

        /// <summary>Sets a target of two scenes, ignoring duplicates.</summary>
        public UniTask SetAsync(TScene scene1, TScene scene2, CancellationToken cancellationToken = default) =>
            SetCoreAsync(new SceneTarget<TScene>(2, scene1, scene2), cancellationToken);

        /// <summary>Sets a target of three scenes, ignoring duplicates.</summary>
        public UniTask SetAsync(TScene scene1, TScene scene2, TScene scene3,
            CancellationToken cancellationToken = default) =>
            SetCoreAsync(new SceneTarget<TScene>(3, scene1, scene2, scene3), cancellationToken);

        /// <summary>Sets a target of four scenes, ignoring duplicates.</summary>
        public UniTask SetAsync(TScene scene1, TScene scene2, TScene scene3, TScene scene4,
            CancellationToken cancellationToken = default) =>
            SetCoreAsync(new SceneTarget<TScene>(4, scene1, scene2, scene3, scene4), cancellationToken);

        /// <summary>Sets a target of five scenes, ignoring duplicates.</summary>
        public UniTask SetAsync(TScene scene1, TScene scene2, TScene scene3, TScene scene4, TScene scene5,
            CancellationToken cancellationToken = default) =>
            SetCoreAsync(new SceneTarget<TScene>(5, scene1, scene2, scene3, scene4, scene5), cancellationToken);

        /// <summary>
        /// Sets a target of any size. Copies the input before queuing; targets over five scenes allocate an array.
        /// </summary>
        public UniTask SetAsync(ReadOnlyMemory<TScene> scenes, CancellationToken cancellationToken = default) =>
            SetCoreAsync(new SceneTarget<TScene>(scenes), cancellationToken);

        /// <summary>
        /// Unloads managed scenes, then loads the initial scene in Single mode. The initial scene is unmanaged.
        /// </summary>
        /// <remarks>
        /// Cancellation before Single loading preserves completed work. Once Single loading starts it is
        /// observed without cancellation and Reset returns success. Reset also unloads other Unity scenes.
        /// </remarks>
        public async UniTask ResetAsync(CancellationToken cancellationToken = default)
        {
            EnsureMainThread();
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await UniTask.SwitchToMainThread();
                cancellationToken.ThrowIfCancellationRequested();
                // Reject a bad reset destination before releasing any ownership.
                if (!Application.CanStreamedLevelBeLoaded(_initialSceneName))
                {
                    throw new InvalidOperationException($"Initial scene '{_initialSceneName}' is not in Build Settings.");
                }

                await UnloadExceptAsync(default, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var operation = SceneManager.LoadSceneAsync(_initialSceneName, LoadSceneMode.Single)
                    ?? throw new InvalidOperationException($"Could not load initial scene '{_initialSceneName}'.");

                // Single loading cannot be undone safely. Keep the gate until Unity has finished.
                await operation.ToUniTask(cancellationToken: CancellationToken.None);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async UniTask SetCoreAsync(SceneTarget<TScene> target, CancellationToken cancellationToken)
        {
            EnsureMainThread();
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await UniTask.SwitchToMainThread();
                cancellationToken.ThrowIfCancellationRequested();
                // Reserve before acquiring resources so successful loads need no List growth to be recorded.
                var capacity = checked(_currentScenes.Count + target.Count);
                if (_currentScenes.Capacity < capacity)
                {
                    _currentScenes.Capacity = capacity;
                }

                if (_loadingMode == SceneLoadingMode.Sequential)
                {
                    for (var i = 0; i < target.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var scene = target[i];
                        if (!_currentScenes.Contains(scene))
                        {
                            var error = await LoadAndRecordAsync(scene, cancellationToken);
                            if (error is not null)
                            {
                                ExceptionDispatchInfo.Capture(error).Throw();
                            }
                        }
                    }
                }
                else
                {
                    await LoadParallelAsync(target, cancellationToken);
                }

                // No obsolete scene is removed unless every missing scene loaded successfully.
                cancellationToken.ThrowIfCancellationRequested();
                await UnloadExceptAsync(target, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async UniTask<Exception?> LoadAndRecordAsync(TScene scene, CancellationToken cancellationToken)
        {
            try
            {
                await _backend.LoadAsync(scene, cancellationToken);
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                return exception;
            }

            await UniTask.SwitchToMainThread();
            // Record success even when cancellation arrived after the backend's point of no cancellation.
            _currentScenes.Add(scene);
            return null;
        }

        private async UniTask LoadParallelAsync(SceneTarget<TScene> target, CancellationToken cancellationToken)
        {
            if (_pendingLoads.Capacity < target.Count)
            {
                _pendingLoads.Capacity = target.Count;
            }

            for (var i = 0; i < target.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var scene = target[i];
                if (target.IsFirstOccurrence(i) && !_currentScenes.Contains(scene))
                {
                    _pendingLoads.Add(LoadAndRecordAsync(scene, cancellationToken));
                }
            }

            List<Exception>? errors = null;
            try
            {
                // Every task is already running. Await each exactly once; each returns its error as data.
                // A failed/canceled task therefore cannot release the gate before the others settle.
                foreach (var pending in _pendingLoads)
                {
                    var error = await pending;
                    if (error is not null)
                    {
                        (errors ??= new List<Exception>()).Add(error);
                    }
                }
            }
            finally
            {
                _pendingLoads.Clear();
            }

            if (errors is not null)
            {
                var onlyCancellation = true;
                foreach (var error in errors)
                {
                    if (error is not OperationCanceledException)
                    {
                        onlyCancellation = false;
                        break;
                    }
                }

                if (errors.Count == 1 || onlyCancellation)
                {
                    ExceptionDispatchInfo.Capture(errors[0]).Throw();
                }

                throw new AggregateException("Multiple scene loads failed.", errors);
            }
        }

        private async UniTask UnloadExceptAsync(SceneTarget<TScene> target, CancellationToken cancellationToken)
        {
            for (var i = _currentScenes.Count - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scene = _currentScenes[i];
                if (target.Contains(scene))
                {
                    continue;
                }

                try
                {
                    await _backend.UnloadAsync(scene, cancellationToken);
                }
                finally
                {
                    await UniTask.SwitchToMainThread();
                }

                // On failure/cancellation the backend still owns the scene, so leave this entry for retry.
                _currentScenes.RemoveAt(i);
            }
        }

        private static void EnsureMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException("SceneCollection must be used on Unity's main thread.");
            }
        }
    }
}