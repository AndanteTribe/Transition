#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Transition
{
    /// <summary>Owns additive Unity scenes, identified by ordinary enum values.</summary>
    /// <remarks>
    /// Use exclusively with one SceneGroupLoader on the main thread. Do not load or unload the same physical scenes
    /// elsewhere. Each enum must identify a distinct scene. Names and paths are passed unchanged to Unity.
    /// Cancellation is checked before starting Unity work.
    /// </remarks>
    public sealed class SceneManagerSceneLoader : ISceneLoader
    {
        private readonly Func<Enum, string> _getSceneName;
        private readonly Dictionary<Enum, SceneEntry> _scenes = new();

        /// <summary>Creates a scene loader. By default, enum names are Unity scene names.</summary>
        /// <param name="getSceneName">Returns the name or path passed directly to SceneManager.LoadSceneAsync.</param>
        public SceneManagerSceneLoader(Func<Enum, string>? getSceneName = null) =>
            _getSceneName = getSceneName ?? (static scene => scene.ToString());

        /// <inheritdoc />
        public async UniTask LoadAsync<TEnum>(TEnum scene, CancellationToken cancellationToken = default)
            where TEnum : Enum
        {
            cancellationToken.ThrowIfCancellationRequested();
            var boxedScene = EnumBoxCache.Get(scene);

            var sceneName = _getSceneName(boxedScene);
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("The scene name resolver returned an empty name.", nameof(scene));
            }

            foreach (var entry in _scenes.Values)
            {
                if (string.Equals(entry.Name, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Scene '{sceneName}' is already owned or loading.");
                }
            }

            if (SceneManager.GetSceneByPath(sceneName).IsValid() || SceneManager.GetSceneByName(sceneName).IsValid())
            {
                throw new InvalidOperationException($"Scene '{sceneName}' is already loaded or loading.");
            }

            // Reserve the identifier and supplied name before starting the physical load.
            _scenes.Add(boxedScene, new SceneEntry(sceneName, default));
            try
            {
                var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive)
                    ?? throw new InvalidOperationException($"Could not start loading scene '{sceneName}'.");

                // Capture this additive load's Scene before another load can append its own Scene.
                var loadedScene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);

                // Point of no cancellation: a completed await must correspond to a stable physical scene.
                await operation.ToUniTask();
                if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                {
                    throw new InvalidOperationException($"Scene '{sceneName}' did not remain loaded. Check for external scene operations.");
                }

                _scenes[boxedScene] = new SceneEntry(sceneName, loadedScene);
            }
            catch
            {
                _scenes.Remove(boxedScene);
                throw;
            }
        }

        /// <inheritdoc />
        public async UniTask UnloadAsync<TEnum>(TEnum scene, CancellationToken cancellationToken = default)
            where TEnum : Enum
        {
            cancellationToken.ThrowIfCancellationRequested();
            var boxedScene = EnumBoxCache.Get(scene);

            if (!_scenes.TryGetValue(boxedScene, out var entry) || !entry.Scene.IsValid())
            {
                throw new InvalidOperationException($"Scene '{scene}' is not owned or is still loading.");
            }

            if (SceneManager.sceneCount <= 1)
            {
                throw new InvalidOperationException("Unity cannot unload its last scene. Keep a bootstrap scene loaded.");
            }

            var operation = SceneManager.UnloadSceneAsync(entry.Scene)
                ?? throw new InvalidOperationException($"Could not start unloading scene '{entry.Name}'.");

            await operation.ToUniTask();
            if (entry.Scene.isLoaded)
            {
                throw new InvalidOperationException($"Scene '{entry.Name}' is still loaded.");
            }

            _scenes.Remove(boxedScene);
        }

        private readonly struct SceneEntry
        {
            public readonly string Name;
            public readonly Scene Scene;

            public SceneEntry(string name, Scene scene)
            {
                Name = name;
                Scene = scene;
            }
        }
    }
}