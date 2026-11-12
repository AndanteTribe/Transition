#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace Transition
{
    /// <summary>An immutable group of scene identifiers from any enum type.</summary>
    /// <remarks>Scenes are distinct and retain their insertion order. The group can be reused by a loader.
    /// Use on Unity's main thread.</remarks>
    public sealed class SceneGroup : IReadOnlyList<Enum>
    {
        private static readonly SceneGroup s_empty = new(new List<Enum>());
        private readonly List<Enum> _scenes;

        /// <summary>Gets an empty scene group.</summary>
        public static SceneGroup Empty => s_empty;

        /// <summary>Gets the number of distinct scenes.</summary>
        public int Count => _scenes.Count;

        /// <summary>Gets a scene in insertion order.</summary>
        public Enum this[int index] => _scenes[index];

        private SceneGroup(List<Enum> scenes) => _scenes = scenes;

        // Copy the builder's list so later changes cannot affect the group.
        internal static SceneGroup CreateFromBuilder(List<Enum> scenes) =>
            scenes.Count == 0 ? Empty : new SceneGroup(new List<Enum>(scenes));

        /// <summary>Determines whether the group contains a scene.</summary>
        public bool Contains<TEnum>(TEnum scene) where TEnum : Enum =>
            _scenes.Contains(EnumBoxCache.Get(scene));

        /// <summary>Returns a value-type enumerator for direct, allocation-free iteration.</summary>
        public List<Enum>.Enumerator GetEnumerator() => _scenes.GetEnumerator();

        IEnumerator<Enum> IEnumerable<Enum>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
