#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UniTaskPlus;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Transition
{
    /// <summary>
    /// Converges the scenes it owns to a requested target, retaining successful work after a failure.
    /// </summary>
    /// <remarks>
    /// Use on Unity's main thread, with an exclusively owned scene loader. Load and Reset are serialized.
    /// Scenes loaded outside this loader, including the initial bootstrap scene, are not adopted.
    /// Keep a bootstrap scene loaded: Unity cannot unload its last scene.
    /// </remarks>
    public sealed class SceneGroupLoader
    {
        /// <summary>Creates group loaders for the supported scene loading methods.</summary>
        public static class Create
        {
            /// <summary>Creates a group loader with its own SceneManager scene loader.</summary>
            /// <param name="getSceneName">Maps scene identifiers to Build Settings names or paths. Defaults to enum names.</param>
            /// <param name="initialSceneName">Build Settings scene name or path loaded by Reset.</param>
            /// <param name="loadingMode">Order of missing-scene loads within each Load call.</param>
            public static SceneGroupLoader SceneManager(Func<Enum, string>? getSceneName = null,
                string initialSceneName = "System", SceneLoadingMode loadingMode = SceneLoadingMode.Sequential) =>
                new(new SceneManagerSceneLoader(getSceneName), initialSceneName, loadingMode);

            /// <summary>Creates a group loader with its own Addressables scene loader.</summary>
            /// <param name="getSceneAddress">Maps scene identifiers to Addressables addresses. Defaults to enum names.</param>
            /// <param name="initialSceneName">Build Settings scene name or path loaded by Reset.</param>
            /// <param name="loadingMode">Order of missing-scene loads within each Load call.</param>
            public static SceneGroupLoader Addressables(Func<Enum, string>? getSceneAddress = null,
                string initialSceneName = "System", SceneLoadingMode loadingMode = SceneLoadingMode.Sequential) =>
                new(new AddressablesSceneLoader(getSceneAddress), initialSceneName, loadingMode);
        }

        private readonly ISceneLoader _sceneLoader;
        private readonly string _initialSceneName;
        private readonly SceneLoadingMode _loadingMode;
        private readonly List<Enum> _currentScenes = new();
        private readonly IReadOnlyList<Enum> _currentScenesView;
        private readonly List<UniTask<Exception?>> _pendingLoads = new();
        private readonly UniTaskSemaphore _semaphore = new(1, 1);

        /// <summary>Gets the number of successfully loaded, still-owned scenes.</summary>
        public int Count => _currentScenes.Count;

        /// <summary>Gets the read-only live view in successful-load completion order without allocating.</summary>
        public IReadOnlyList<Enum> CurrentScenes => _currentScenesView;

        /// <summary>Creates a group loader with its own scene loader and fixed loading mode.</summary>
        /// <param name="sceneLoader">An empty scene loader used exclusively by this group loader.</param>
        /// <param name="initialSceneName">Build Settings scene name or path loaded by Reset.</param>
        /// <param name="loadingMode">Order of missing-scene loads within each Load call.</param>
        public SceneGroupLoader(ISceneLoader sceneLoader, string initialSceneName = "System",
            SceneLoadingMode loadingMode = SceneLoadingMode.Sequential)
        {
            _sceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
            _initialSceneName = !string.IsNullOrWhiteSpace(initialSceneName)
                ? initialSceneName
                : throw new ArgumentException("An initial scene name is required.", nameof(initialSceneName));
            _loadingMode = loadingMode is SceneLoadingMode.Sequential or SceneLoadingMode.Parallel
                ? loadingMode
                : throw new ArgumentOutOfRangeException(nameof(loadingMode));
            _currentScenesView = _currentScenes.AsReadOnly();
        }

        /// <summary>Determines whether a scene is currently owned.</summary>
        public bool Contains<TEnum>(TEnum scene) where TEnum : Enum =>
            _currentScenes.Contains(EnumBoxCache.Get(scene));

        /// <summary>
        /// Loads missing scenes in the group, then unloads managed scenes absent from the group.
        /// Pass an empty group or null to unload every managed scene. Reusing a group requires no input copy.
        /// </summary>
        /// <remarks>
        /// Parallel load failures are reported as an AggregateException. If every error is a cancellation,
        /// a cancellation exception is reported instead.
        /// </remarks>
        public async UniTask LoadAsync(SceneGroup? group, CancellationToken cancellationToken = default)
        {
            using (await _semaphore.WaitScopeAsync(cancellationToken))
            {
                group ??= SceneGroup.Empty;
                if (_loadingMode == SceneLoadingMode.Sequential)
                {
                    for (var i = 0; i < group.Count; i++)
                    {
                        var scene = group[i];
                        if (!_currentScenes.Contains(scene))
                        {
                            await _sceneLoader.LoadAsync(scene, cancellationToken);
                            _currentScenes.Add(scene);
                        }
                    }
                }
                else
                {
                    await LoadParallelAsync(group, cancellationToken);
                }

                // No obsolete scene is removed unless every missing scene loaded successfully.
                await UnloadScenesExceptAsync(group, cancellationToken);
                // Report cancellation that arrived while an irreversible physical operation completed.
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        /// <summary>
        /// Unloads managed scenes, then loads the initial scene in Single mode. The initial scene is unmanaged.
        /// </summary>
        /// <remarks>
        /// Cancellation before Single loading preserves completed work. Once Single loading starts it is
        /// observed without cancellation and Reset returns success. Reset also unloads other Unity scenes.
        /// </remarks>
        public async UniTask ResetAsync(CancellationToken cancellationToken = default)
        {
            using (await _semaphore.WaitScopeAsync(cancellationToken))
            {
                // Reject a bad reset destination before releasing any ownership.
                if (!Application.CanStreamedLevelBeLoaded(_initialSceneName))
                {
                    throw new InvalidOperationException($"Initial scene '{_initialSceneName}' is not in Build Settings.");
                }

                await UnloadScenesExceptAsync(SceneGroup.Empty, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var operation = SceneManager.LoadSceneAsync(_initialSceneName, LoadSceneMode.Single)
                    ?? throw new InvalidOperationException($"Could not load initial scene '{_initialSceneName}'.");

                // Single loading cannot be undone safely. Keep the semaphore until Unity has finished.
                await operation.ToUniTask();
            }
        }

        // Parallel loads retain errors as data to preserve cancellation exception details until all loads settle.
        private async UniTask<Exception?> LoadAndRecordAsync(Enum scene, CancellationToken cancellationToken)
        {
            try
            {
                await _sceneLoader.LoadAsync(scene, cancellationToken);
            }
            catch (Exception exception)
            {
                return exception;
            }

            // Record success even when cancellation arrived after the scene loader's point of no cancellation.
            _currentScenes.Add(scene);
            return null;
        }

        private async UniTask LoadParallelAsync(SceneGroup group, CancellationToken cancellationToken)
        {
            for (var i = 0; i < group.Count; i++)
            {
                var scene = group[i];
                if (_currentScenes.Contains(scene))
                {
                    continue;
                }

                // Stop scheduling new work without creating canceled tasks for the remaining scenes.
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _pendingLoads.Add(LoadAndRecordAsync(scene, cancellationToken));
            }

            List<Exception>? errors = null;
            try
            {
                // Every task is already running. Await each exactly once; each returns its error as data.
                // A failed/canceled task therefore cannot release the semaphore before the others settle.
                foreach (var pending in _pendingLoads)
                {
                    var error = await pending;
                    if (error is not null)
                    {
                        if (errors is null)
                        {
                            errors = new List<Exception>();
                        }

                        errors.Add(error);
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

                if (onlyCancellation)
                {
                    ExceptionDispatchInfo.Capture(errors[0]).Throw();
                }

                throw new AggregateException("One or more scene loads failed.", errors);
            }
        }

        private async UniTask UnloadScenesExceptAsync(SceneGroup group, CancellationToken cancellationToken)
        {
            for (var i = _currentScenes.Count - 1; i >= 0; i--)
            {
                var scene = _currentScenes[i];
                if (group.Contains(scene))
                {
                    continue;
                }

                await _sceneLoader.UnloadAsync(scene, cancellationToken);

                // On failure/cancellation the scene loader still owns the scene, so leave this entry for retry.
                _currentScenes.RemoveAt(i);
            }
        }
    }
}
