#nullable enable

using System;

namespace Transition
{
    /// <summary>Creates collections backed by Addressables.</summary>
    public static class AddressablesSceneCollection
    {
        /// <summary>Creates a collection with its own Addressables backend.</summary>
        /// <param name="getSceneName">Maps scene identifiers to Addressables addresses. Defaults to enum names.</param>
        /// <param name="initialSceneName">Build Settings scene name or path loaded by Reset.</param>
        /// <param name="loadingMode">Order of missing-scene loads within each Set call.</param>
        public static SceneCollection<TScene> Create<TScene>(Func<TScene, string>? getSceneName = null,
            string initialSceneName = "System", SceneLoadingMode loadingMode = SceneLoadingMode.Sequential)
            where TScene : struct, Enum =>
            new(new AddressablesBackend<TScene>(getSceneName), initialSceneName, loadingMode);
    }
}