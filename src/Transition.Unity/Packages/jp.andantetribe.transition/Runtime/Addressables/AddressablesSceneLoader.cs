#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Transition
{
    /// <summary>
    /// Loads scenes with Addressables.
    /// </summary>
    /// <typeparam name="TScene">The type used to identify scenes.</typeparam>
    /// <remarks>
    /// Cancellation requested after an operation starts is reported only after the Addressables operation and
    /// any required rollback have completed.
    /// </remarks>
    public sealed class AddressablesSceneLoader<TScene> : ISceneLoader<TScene> where TScene : notnull
    {
        private readonly Func<TScene, string> _getSceneName;

        /// <summary>
        /// Initialize a new instance of <see cref="AddressablesSceneLoader{TScene}"/>.
        /// </summary>
        /// <param name="getSceneName">Function to get the Addressables scene name.</param>
        public AddressablesSceneLoader(Func<TScene, string>? getSceneName = null)
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

            // UniTask checks whether progress is null internally, so no null check is needed here.
            var handle = Addressables.LoadSceneAsync(_getSceneName(scene), LoadSceneMode.Additive);
            try
            {
                // Addressables cannot cancel a scene load. Observe it to completion so that a loaded scene is
                // never left outside the controller's ownership.
                await handle.ToUniTask(progress!);
            }
            catch
            {
                if (handle.IsValid())
                {
                    // A failed SceneInstance operation is not a loaded scene lifetime. Use the typeless overload
                    // to release only the operation handle and avoid Addressables' scene-specific release path.
                    Addressables.Release((AsyncOperationHandle)handle);
                }

                throw;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await UnloadCoreAsync(handle, progress: null);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new SceneHandle(handle);
        }

        private static async UniTask UnloadCoreAsync(
            AsyncOperationHandle<SceneInstance> loadHandle,
            IProgress<float>? progress)
        {
            var unloadHandle = Addressables.UnloadSceneAsync(loadHandle, autoReleaseHandle: false);
            try
            {
                await unloadHandle.ToUniTask(progress!);
            }
            finally
            {
                if (unloadHandle.IsValid())
                {
                    // The returned handle represents the unload operation, not another loaded scene lifetime.
                    Addressables.Release((AsyncOperationHandle)unloadHandle);
                }
            }
        }

        private sealed class SceneHandle : ISceneHandle
        {
            private readonly AsyncOperationHandle<SceneInstance> _handle;

            public SceneHandle(AsyncOperationHandle<SceneInstance> handle)
            {
                _handle = handle;
            }

            /// <inheritdoc />
            public async UniTask UnloadAsync(IProgress<float>? progress, CancellationToken cancellationToken)
            {
                // Once started, Addressables cannot cancel the underlying unload operation. Wait for the scene
                // state to settle before reporting cancellation to the controller.
                await UnloadCoreAsync(_handle, progress);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}