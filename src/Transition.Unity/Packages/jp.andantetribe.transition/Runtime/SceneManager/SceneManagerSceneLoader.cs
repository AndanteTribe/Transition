#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Transition
{
    /// <summary>
    /// Loads scenes from Build Settings with Unity's SceneManager.
    /// </summary>
    /// <typeparam name="TScene">The type used to identify scenes.</typeparam>
    /// <remarks>
    /// Loading the same scene concurrently outside this loader is not supported because Unity's load operation
    /// does not expose the <see cref="Scene"/> it created.
    /// Cancellation requested after an operation starts is reported only after the Unity operation and any
    /// required rollback have completed.
    /// </remarks>
    public sealed class SceneManagerSceneLoader<TScene> : ISceneLoader<TScene> where TScene : notnull
    {
        private readonly Func<TScene, string> _getSceneName;

        /// <summary>
        /// Initialize a new instance of <see cref="SceneManagerSceneLoader{TScene}"/>.
        /// </summary>
        /// <param name="getSceneName">Function to get the scene name or path in Build Settings.</param>
        public SceneManagerSceneLoader(Func<TScene, string>? getSceneName = null)
        {
            _getSceneName = getSceneName ?? (static scene => scene.ToString());
        }

        /// <inheritdoc />
        public async UniTask<ISceneHandle> LoadAsync(
            TScene scene,
            IProgress<float>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sceneName = _getSceneName(scene);
            var loadedSceneHandles = GetLoadedSceneHandles();
            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (operation is null)
            {
                throw new InvalidOperationException($"SceneManager could not start loading scene '{sceneName}'.");
            }

            // SceneManager cannot cancel its AsyncOperation. Always observe it to completion so that a loaded
            // scene is never left outside the controller's ownership.
            await operation.ToUniTask(progress!);

            var loadedScene = FindLoadedScene(sceneName, loadedSceneHandles);
            if (!loadedScene.IsValid())
            {
                throw new InvalidOperationException($"SceneManager loaded scene '{sceneName}', but its Scene could not be identified.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await UnloadCoreAsync(loadedScene, progress: null);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new SceneHandle(loadedScene);
        }

        private static HashSet<int> GetLoadedSceneHandles()
        {
            var handles = new HashSet<int>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                handles.Add(SceneManager.GetSceneAt(i).handle);
            }

            return handles;
        }

        private static Scene FindLoadedScene(string sceneName, HashSet<int> loadedSceneHandles)
        {
            var expectedName = Path.GetFileNameWithoutExtension(sceneName);
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var candidate = SceneManager.GetSceneAt(i);
                if (!loadedSceneHandles.Contains(candidate.handle) &&
                    (string.Equals(candidate.path, sceneName, StringComparison.Ordinal) ||
                     string.Equals(candidate.name, expectedName, StringComparison.Ordinal)))
                {
                    return candidate;
                }
            }

            return default;
        }

        private static async UniTask UnloadCoreAsync(Scene scene, IProgress<float>? progress)
        {
            var operation = SceneManager.UnloadSceneAsync(scene);
            if (operation is null)
            {
                throw new InvalidOperationException($"SceneManager could not start unloading scene '{scene.path}'.");
            }

            await operation.ToUniTask(progress!);
        }

        private sealed class SceneHandle : ISceneHandle
        {
            private readonly Scene _scene;

            public SceneHandle(Scene scene)
            {
                _scene = scene;
            }

            /// <inheritdoc />
            public async UniTask UnloadAsync(IProgress<float>? progress, CancellationToken cancellationToken)
            {
                // Once started, Unity cannot cancel the unload operation. Wait for the actual scene state to
                // settle before reporting cancellation to the controller.
                await UnloadCoreAsync(_scene, progress);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}