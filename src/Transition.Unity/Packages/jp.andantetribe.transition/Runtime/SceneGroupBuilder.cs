#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace Transition
{
    /// <summary>Configures scenes before building an immutable scene group.</summary>
    /// <remarks>
    /// Add ignores duplicates and preserves insertion order. Build copies the scenes into a group
    /// without changing the builder. Dispose releases the working list for reuse. Use on Unity's main thread.
    /// </remarks>
    public sealed class SceneGroupBuilder : IDisposable
    {
        private List<Enum>? _scenes;

        /// <summary>Creates a builder with a pooled working list.</summary>
        public SceneGroupBuilder() => _scenes = ListPool<Enum>.Get();

        /// <summary>Adds one scene and returns this builder.</summary>
        public SceneGroupBuilder Add<TEnum>(TEnum scene) where TEnum : Enum
        {
            var scenes = GetScenes();
            var boxedScene = EnumBoxCache.Get(scene);
            if (!scenes.Contains(boxedScene))
            {
                scenes.Add(boxedScene);
            }

            return this;
        }

        /// <summary>Returns an immutable copy of the configured scenes.</summary>
        public SceneGroup Build() => SceneGroup.CreateFromBuilder(GetScenes());

        /// <summary>Releases the working list for reuse.</summary>
        public void Dispose()
        {
            var scenes = _scenes;
            if (scenes is null)
            {
                return;
            }

            _scenes = null;
            ListPool<Enum>.Release(scenes);
        }

        private List<Enum> GetScenes() => _scenes ?? throw new ObjectDisposedException(nameof(SceneGroupBuilder));
    }
}
