#nullable enable

using System;
using System.Collections.Generic;

namespace Transition
{
    /// <summary>Reuses boxed enum values for scene identifiers.</summary>
    /// <remarks>Use on Unity's main thread.</remarks>
    public static class EnumBoxCache
    {
        /// <summary>Returns a cached box, or the original reference when the value is already boxed.</summary>
        public static Enum Get<TEnum>(TEnum scene) where TEnum : Enum
        {
            if (typeof(TEnum) == typeof(Enum))
            {
                return scene ?? throw new ArgumentNullException(nameof(scene));
            }

            return EnumBoxCache<TEnum>.Get(scene);
        }
    }

    // Reuse boxes for enum values within the lifetime of each closed generic type.
    internal static class EnumBoxCache<TEnum> where TEnum : Enum
    {
        private static readonly Dictionary<TEnum, Enum> s_entries = new();

        public static Enum Get(TEnum value)
        {
            if (s_entries.TryGetValue(value, out var boxed))
            {
                return boxed;
            }

            boxed = value;
            s_entries.Add(value, boxed);

            return boxed;
        }
    }
}
