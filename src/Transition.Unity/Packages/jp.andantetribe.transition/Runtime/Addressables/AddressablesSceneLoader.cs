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
    /// <summary>Loads additive Addressables scenes and retains their handles until unloaded.</summary>
    /// <remarks>
    /// Use exclusively with one SceneGroupLoader on the main thread. Each enum must identify a distinct scene.
    /// Do not externally unload owned scenes. Cancellation is checked before starting Addressables work.
    /// </remarks>
    public sealed class AddressablesSceneLoader : ISceneLoader
    {
        private readonly Func<Enum, string> _getSceneAddress;
        private readonly Dictionary<Enum, AsyncOperationHandle<SceneInstance>> _scenes = new();

        /// <summary>Creates a scene loader. By default, enum names are Addressables addresses.</summary>
        /// <param name="getSceneAddress">Maps scene identifiers to addresses. Defaults to enum names.</param>
        public AddressablesSceneLoader(Func<Enum, string>? getSceneAddress = null) =>
            _getSceneAddress = getSceneAddress ?? (static scene => scene.ToString());

        /// <inheritdoc />
        public async UniTask LoadAsync<TEnum>(TEnum scene, CancellationToken cancellationToken = default)
            where TEnum : Enum
        {
            cancellationToken.ThrowIfCancellationRequested();
            var boxedScene = EnumBoxCache.Get(scene);

            var address = _getSceneAddress(boxedScene);
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("The scene address resolver returned an empty address.", nameof(scene));
            }

            // Reserve before starting: completion only updates an existing dictionary entry.
            _scenes.Add(boxedScene, default);
            AsyncOperationHandle<SceneInstance> handle = default;
            try
            {
                handle = Addressables.LoadSceneAsync(address, LoadSceneMode.Additive);
                await handle.ToUniTask();
                _scenes[boxedScene] = handle;
            }
            catch
            {
                _scenes.Remove(boxedScene);
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
        public async UniTask UnloadAsync<TEnum>(TEnum scene, CancellationToken cancellationToken = default)
            where TEnum : Enum
        {
            cancellationToken.ThrowIfCancellationRequested();
            var boxedScene = EnumBoxCache.Get(scene);

            if (!_scenes.TryGetValue(boxedScene, out var loadHandle) || !loadHandle.IsValid())
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
                await unloadHandle.ToUniTask();

                if (loadedScene.isLoaded)
                {
                    throw new InvalidOperationException($"Scene '{scene}' is still loaded; its ownership is retained for retry.");
                }

                _scenes.Remove(boxedScene);
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
    }
}