#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UniTaskPlus;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Transition
{
    /// <summary>
    /// Generic scene transition implementation.
    /// </summary>
    public class SceneControllerCore<TEnum> where TEnum : unmanaged, Enum
    {
        // Define "restart" as loading the System scene in Single mode.
        private const string DefaultSceneName = "System";

        private readonly Func<TEnum, string> _getSceneName;
        private readonly List<SceneInfo> _activeScenes = new(4);
        private readonly UniTaskSemaphore _semaphore = new(1, 1);

        /// <summary>
        /// Current scene name.
        /// </summary>
        public TEnum CurrentScene { get; private set; }

        /// <summary>
        /// Initialize a new instance of <see cref="SceneControllerCore{TEnum}"/>.
        /// </summary>
        /// <param name="getSceneName">Function to get the scene name.</param>
        public SceneControllerCore(Func<TEnum, string>? getSceneName = null)
        {
            _getSceneName = getSceneName ?? (static enumValue => enumValue.ToString());
        }

        static SceneControllerCore()
        {
            if (Enum.GetUnderlyingType(typeof(TEnum)) != typeof(int))
            {
                throw new ArgumentException($"The underlying type of {typeof(TEnum).FullName} must be int.");
            }
        }

        /// <summary>
        /// Loads scenes asynchronously.
        /// </summary>
        /// <param name="sceneName">Target scene flags.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async UniTask LoadAsync(TEnum sceneName, IProgress<float>? progress, CancellationToken cancellationToken)
        {
            var array = ArrayPool<SceneInfo>.Shared.Rent(_activeScenes.Count);
            _activeScenes.CopyTo(array);
            var activeScenes = new ArraySegment<SceneInfo>(array, 0, _activeScenes.Count);

            try
            {
                await _semaphore.WaitAsync(cancellationToken);

                foreach (var flag in sceneName)
                {
                    if (!CurrentScene.HasBitFlags(flag))
                    {
                        // HACK: UniTask checks whether progress is null internally, so no null check is needed here.
                        var scene = await Addressables.LoadSceneAsync(_getSceneName(flag), LoadSceneMode.Additive)
                            .ToUniTask(progress!, cancellationToken: cancellationToken, autoReleaseWhenCanceled: true);
                        var info = new SceneInfo(flag, scene);
                        _activeScenes.Add(info);
                    }
                }

                await using (var bag = new UniTaskBag())
                {
                    foreach (var info in activeScenes)
                    {
                        if (!sceneName.HasBitFlags(info.SceneName))
                        {
                            bag.Add(Addressables.UnloadSceneAsync(info.SceneInstance)
                                .ToUniTask(progress!, cancellationToken: cancellationToken, autoReleaseWhenCanceled: true));
                            _activeScenes.Remove(info);
                        }
                    }
                }

                CurrentScene = sceneName;
            }
            catch (Exception)
            {
                // If loading fails, recompute the currently loaded scenes.
                CurrentScene = AggregateFlags(_activeScenes.AsSpan());
                throw;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                static TEnum AggregateFlags(ReadOnlySpan<SceneInfo> infos)
                {
                    var activeScenes = (Span<TEnum>)stackalloc TEnum[infos.Length];
                    for (var i = 0; i < infos.Length; i++)
                    {
                        activeScenes[i] = infos[i].SceneName;
                    }

                    return activeScenes.AggregateFlags();
                }
            }
            finally
            {
                ArrayPool<SceneInfo>.Shared.Return(array);
                _semaphore.Release();
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
        public async UniTask RestartAsync(IProgress<float>? progress, bool forceImmediate, CancellationToken cancellationToken)
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

        private async UniTask UnloadAllCoreAsync(IProgress<float>? progress, CancellationToken cancellationToken)
        {
            try
            {
                await using var bag = new UniTaskBag();
                foreach (var info in _activeScenes)
                {
                    bag.Add(Addressables.UnloadSceneAsync(info.SceneInstance)
                        .ToUniTask(progress!, cancellationToken: cancellationToken, autoReleaseWhenCanceled: true));
                }
                _activeScenes.Clear();
            }
            finally
            {
                CurrentScene = default;
            }
        }

        private readonly struct SceneInfo : IEquatable<SceneInfo>
        {
            public readonly TEnum SceneName;

            public readonly SceneInstance SceneInstance;

            public SceneInfo(TEnum sceneName, SceneInstance sceneInstance)
            {
                SceneName = sceneName;
                SceneInstance = sceneInstance;
            }

            /// <inheritdoc />
            public bool Equals(SceneInfo other) => EqualityComparer<TEnum>.Default.Equals(SceneName, other.SceneName);

            /// <inheritdoc />
            public override int GetHashCode() => EqualityComparer<TEnum>.Default.GetHashCode(SceneName);
        }
    }
}