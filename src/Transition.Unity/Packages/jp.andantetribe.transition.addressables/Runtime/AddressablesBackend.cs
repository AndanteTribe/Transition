#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Transition
{
    /// <summary>Owns additive Addressables scenes without exposing their handles to the collection.</summary>
    /// <remarks>
    /// Use exclusively with one collection on the main thread. Each enum must identify a distinct scene.
    /// Do not externally unload owned scenes. Cancellation is checked before starting Addressables work.
    /// </remarks>
    public sealed class AddressablesBackend<TScene> : ISceneBackend<TScene> where TScene : struct, Enum
    {
        private readonly Func<TScene, string> _getSceneName;
        private readonly Dictionary<TScene, AsyncOperationHandle<SceneInstance>> _scenes = new();

        /// <summary>Creates a backend. By default, enum names are Addressables addresses.</summary>
        public AddressablesBackend(Func<TScene, string>? getSceneName = null) =>
            _getSceneName = getSceneName ?? (static scene => scene.ToString());

        /// <inheritdoc />
        public async UniTask LoadAsync(TScene scene, CancellationToken cancellationToken = default)
        {
            EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            var address = _getSceneName(scene);
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("The scene name resolver returned an empty address.", nameof(scene));
            }

            // Reserve before starting: completion only updates an existing dictionary entry.
            _scenes.Add(scene, default);
            AsyncOperationHandle<SceneInstance> handle = default;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                handle = Addressables.LoadSceneAsync(address, LoadSceneMode.Additive);
                await handle.ToUniTask(cancellationToken: CancellationToken.None);
                _scenes[scene] = handle;
            }
            catch
            {
                _scenes.Remove(scene);
                if (handle.IsValid())
                {
                    // Failed load operations still own a reference. This overload releases the operation;
                    // Release<SceneInstance> would take Addressables' scene-specific unload path instead.
                    Addressables.Release((AsyncOperationHandle)handle);
                }

                throw;
            }
        }

        /// <inheritdoc />
        public async UniTask UnloadAsync(TScene scene, CancellationToken cancellationToken = default)
        {
            EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_scenes.TryGetValue(scene, out var loadHandle) || !loadHandle.IsValid() || !loadHandle.IsDone)
            {
                throw new InvalidOperationException($"Scene '{scene}' is not owned or is still loading.");
            }

            var loadedScene = loadHandle.Result.Scene;
            if (loadedScene.isLoaded && SceneManager.sceneCount <= 1)
            {
                // Addressables 1.19 can report success when Unity refuses to unload its last scene.
                throw new InvalidOperationException("Unity cannot unload its last scene. Keep a bootstrap scene loaded.");
            }

            var unloadHandle = Addressables.UnloadSceneAsync(loadHandle, autoReleaseHandle: false);
            try
            {
                await unloadHandle.ToUniTask(cancellationToken: CancellationToken.None);

                if (loadedScene.isLoaded)
                {
                    throw new InvalidOperationException($"Scene '{scene}' is still loaded; its ownership is retained for retry.");
                }

                _scenes.Remove(scene);
            }
            finally
            {
                if (unloadHandle.IsValid())
                {
                    // Only release the unload operation. Addressables consumes the original scene reference.
                    Addressables.Release((AsyncOperationHandle)unloadHandle);
                }
            }
        }

        private static void EnsureMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException("AddressablesBackend must be used on Unity's main thread.");
            }
        }
    }
}