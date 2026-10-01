#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Transition
{
    /// <summary>Owns additive scenes from Build Settings, identified by ordinary enum values.</summary>
    /// <remarks>
    /// Use exclusively with one collection on the main thread. Do not load or unload the same physical scenes
    /// elsewhere. Duplicate scene names require full paths. Cancellation is checked before starting Unity work.
    /// </remarks>
    public sealed class SceneManagerBackend<TScene> : ISceneBackend<TScene> where TScene : struct, Enum
    {
        private readonly Func<TScene, string> _getSceneName;
        private readonly Dictionary<TScene, SceneEntry> _scenes = new();

        /// <summary>Creates a backend. By default, enum names are Build Settings scene names.</summary>
        public SceneManagerBackend(Func<TScene, string>? getSceneName = null) =>
            _getSceneName = getSceneName ?? (static scene => scene.ToString());

        /// <inheritdoc />
        public async UniTask LoadAsync(TScene scene, CancellationToken cancellationToken = default)
        {
            EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolvePath(_getSceneName(scene));
            foreach (var entry in _scenes.Values)
            {
                if (string.Equals(entry.Path, path, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Scene '{path}' is already owned or loading.");
                }
            }

            if (SceneManager.GetSceneByPath(path).IsValid())
            {
                throw new InvalidOperationException($"Scene '{path}' is already loaded outside this backend.");
            }

            // Reserve both the enum and path before starting. Different IDs cannot race to load one path.
            _scenes.Add(scene, new SceneEntry(path, default));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive)
                    ?? throw new InvalidOperationException($"Could not start loading scene '{path}'.");

                // Point of no cancellation: a completed await must correspond to a stable physical scene.
                await operation.ToUniTask(cancellationToken: CancellationToken.None);
                var loadedScene = SceneManager.GetSceneByPath(path);
                if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                {
                    throw new InvalidOperationException($"Scene '{path}' did not remain loaded. Check for external scene operations.");
                }

                _scenes[scene] = new SceneEntry(path, loadedScene);
            }
            catch
            {
                _scenes.Remove(scene);
                throw;
            }
        }

        /// <inheritdoc />
        public async UniTask UnloadAsync(TScene scene, CancellationToken cancellationToken = default)
        {
            EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_scenes.TryGetValue(scene, out var entry) || !entry.Scene.IsValid())
            {
                throw new InvalidOperationException($"Scene '{scene}' is not owned or is still loading.");
            }

            if (SceneManager.sceneCount <= 1)
            {
                throw new InvalidOperationException("Unity cannot unload its last scene. Keep a bootstrap scene loaded.");
            }

            var operation = SceneManager.UnloadSceneAsync(entry.Scene)
                ?? throw new InvalidOperationException($"Could not start unloading scene '{entry.Path}'.");

            await operation.ToUniTask(cancellationToken: CancellationToken.None);
            if (entry.Scene.isLoaded)
            {
                throw new InvalidOperationException($"Scene '{entry.Path}' is still loaded.");
            }

            _scenes.Remove(scene);
        }

        private static string ResolvePath(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("The scene name resolver returned an empty name.", nameof(sceneName));
            }

            string? match = null;
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.Equals(path, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                if (!string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match is not null)
                {
                    throw new ArgumentException($"Scene name '{sceneName}' is ambiguous. Use its full path.", nameof(sceneName));
                }

                match = path;
            }

            return match ?? throw new ArgumentException($"Scene '{sceneName}' is not in Build Settings.", nameof(sceneName));
        }

        private static void EnsureMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException("SceneManagerBackend must be used on Unity's main thread.");
            }
        }

        private readonly struct SceneEntry
        {
            public readonly string Path;
            public readonly Scene Scene;

            public SceneEntry(string path, Scene scene)
            {
                Path = path;
                Scene = scene;
            }
        }
    }
}