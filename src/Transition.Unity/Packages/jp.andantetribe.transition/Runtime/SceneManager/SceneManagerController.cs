#nullable enable

using System;

namespace Transition
{
    /// <summary>
    /// Creates scene controllers backed by Unity's SceneManager.
    /// </summary>
    public static class SceneManagerController
    {
        /// <summary>
        /// Creates a new SceneManager-backed enum-flags scene controller.
        /// </summary>
        /// <typeparam name="TEnum">The enum-flags type used to identify scenes.</typeparam>
        /// <param name="getSceneName">Function to get the scene name or path in Build Settings.</param>
        public static SceneControllerCore<TEnum> CreateFlags<TEnum>(Func<TEnum, string>? getSceneName = null)
            where TEnum : unmanaged, Enum
        {
            SceneSet.FromFlags<TEnum>();
            return new SceneControllerCore<TEnum>(
                new SceneManagerSceneLoader<TEnum>(getSceneName),
                static flags => SceneSet.FromFlags(flags));
        }

        /// <summary>
        /// Creates a new SceneManager-backed scene collection controller.
        /// </summary>
        /// <typeparam name="TEnum">The enum type used to identify scenes.</typeparam>
        public static SceneControllerCore<TEnum> CreateScenes<TEnum>() where TEnum : unmanaged, Enum =>
            new(new SceneManagerSceneLoader<TEnum>());

        /// <summary>
        /// Creates a new SceneManager-backed scene collection controller.
        /// </summary>
        /// <typeparam name="TScene">The type used to identify scenes.</typeparam>
        /// <param name="getSceneName">Function to get the scene name or path in Build Settings.</param>
        public static SceneControllerCore<TScene> CreateScenes<TScene>(Func<TScene, string> getSceneName)
            where TScene : notnull =>
            new(new SceneManagerSceneLoader<TScene>(
                getSceneName ?? throw new ArgumentNullException(nameof(getSceneName))));
    }
}
