#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace Transition
{
    /// <summary>
    /// Creates sets of scenes while inferring their identifier type.
    /// </summary>
    public static class SceneSet
    {
        /// <summary>
        /// Creates an immutable set containing the specified scenes.
        /// </summary>
        /// <typeparam name="TScene">The type used to identify scenes.</typeparam>
        /// <param name="scenes">Scenes in their preferred load order.</param>
        /// <returns>A set containing each scene once, in first-occurrence order.</returns>
        public static SceneSet<TScene> Of<TScene>(params TScene[] scenes) where TScene : notnull
        {
            if (scenes is null)
            {
                throw new ArgumentNullException(nameof(scenes));
            }

            return SceneSet<TScene>.Create(scenes);
        }

        /// <summary>
        /// Creates a scene set by expanding enum flags into individual scene identifiers.
        /// </summary>
        /// <typeparam name="TEnum">The enum-flags type used to identify scenes.</typeparam>
        /// <param name="flags">Scene flags to expand.</param>
        /// <returns>A set containing each individual flag once.</returns>
        public static SceneSet<TEnum> FromFlags<TEnum>(params TEnum[] flags) where TEnum : unmanaged, Enum
        {
            if (flags is null)
            {
                throw new ArgumentNullException(nameof(flags));
            }

            ValidateFlagsType<TEnum>();

            var scenes = new List<TEnum>(flags.Length);
            foreach (var value in flags)
            {
                foreach (var flag in value)
                {
                    scenes.Add(flag);
                }
            }

            return SceneSet<TEnum>.Create(scenes.ToArray());
        }

        /// <summary>
        /// Aggregates individual scene identifiers into a flags value.
        /// </summary>
        /// <typeparam name="TEnum">The enum-flags type used to identify scenes.</typeparam>
        /// <param name="scenes">Scene set to aggregate.</param>
        /// <returns>The flags value representing the scene set.</returns>
        public static TEnum ToFlags<TEnum>(this SceneSet<TEnum> scenes) where TEnum : unmanaged, Enum
        {
            if (scenes is null)
            {
                throw new ArgumentNullException(nameof(scenes));
            }

            ValidateFlagsType<TEnum>();
            return scenes.AsSpan().AggregateFlags();
        }

        private static void ValidateFlagsType<TEnum>() where TEnum : unmanaged, Enum
        {
            if (Enum.GetUnderlyingType(typeof(TEnum)) != typeof(int))
            {
                throw new ArgumentException($"The underlying type of {typeof(TEnum).FullName} must be int.");
            }

            if (!typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false))
            {
                throw new ArgumentException($"{typeof(TEnum).FullName} must be decorated with FlagsAttribute.");
            }
        }
    }

    /// <summary>
    /// An immutable set of scenes that preserves their preferred load order.
    /// </summary>
    /// <typeparam name="TScene">The type used to identify scenes.</typeparam>
    public sealed class SceneSet<TScene> : IReadOnlyList<TScene>
        where TScene : notnull
    {
        private readonly TScene[] _scenes;
        private readonly HashSet<TScene> _lookup;

        private SceneSet(TScene[] scenes, HashSet<TScene> lookup)
        {
            _scenes = scenes;
            _lookup = lookup;
        }

        /// <summary>
        /// Gets an empty scene set.
        /// </summary>
        public static SceneSet<TScene> Empty { get; } =
            new(Array.Empty<TScene>(), new HashSet<TScene>());

        /// <inheritdoc />
        public int Count => _scenes.Length;

        /// <inheritdoc />
        public TScene this[int index] => _scenes[index];

        internal static SceneSet<TScene> Create(TScene[] scenes)
        {
            if (scenes.Length == 0)
            {
                return Empty;
            }

            var lookup = new HashSet<TScene>();
            var distinctScenes = new TScene[scenes.Length];
            var count = 0;
            foreach (var scene in scenes)
            {
                if (scene is null)
                {
                    throw new ArgumentException("A scene identifier cannot be null.", nameof(scenes));
                }

                if (lookup.Add(scene))
                {
                    distinctScenes[count++] = scene;
                }
            }

            if (count == 0)
            {
                return Empty;
            }

            if (count != distinctScenes.Length)
            {
                Array.Resize(ref distinctScenes, count);
            }

            return new SceneSet<TScene>(distinctScenes, lookup);
        }

        internal ReadOnlySpan<TScene> AsSpan() => _scenes;

        /// <summary>
        /// Determines whether the set contains the specified scene.
        /// </summary>
        /// <param name="scene">Scene to locate.</param>
        /// <returns><see langword="true"/> when the scene is contained in the set.</returns>
        public bool Contains(TScene scene) => _lookup.Contains(scene);

        /// <summary>
        /// Creates a new set by appending the specified scenes.
        /// </summary>
        /// <param name="scenes">Scenes to append in their preferred load order.</param>
        /// <returns>A new set containing each scene once, in first-occurrence order.</returns>
        public SceneSet<TScene> With(params TScene[] scenes)
        {
            if (scenes is null)
            {
                throw new ArgumentNullException(nameof(scenes));
            }

            if (scenes.Length == 0)
            {
                return this;
            }

            var combinedScenes = new TScene[_scenes.Length + scenes.Length];
            Array.Copy(_scenes, combinedScenes, _scenes.Length);
            Array.Copy(scenes, 0, combinedScenes, _scenes.Length, scenes.Length);
            return Create(combinedScenes);
        }

        /// <inheritdoc />
        public IEnumerator<TScene> GetEnumerator() => ((IEnumerable<TScene>)_scenes).GetEnumerator();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => _scenes.GetEnumerator();
    }
}
