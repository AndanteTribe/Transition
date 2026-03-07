using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Transition
{
    /// <summary>
    /// Additional extension methods for <see cref="System.Collections.Generic"/>, <see cref="System.Linq"/>, and <see cref="System.Buffers.ArrayPool{T}"/>.
    /// </summary>
    internal static class CollectionExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> AsSpan<T>(this List<T> list)
        {
#if NET5_0_OR_GREATER
            return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
#else
            return list == null ? default : UnsafeUtility.As<List<T>, ListDummy<T>>(ref list).Items.AsSpan(0, list.Count);
#endif
        }

#if !NET5_0_OR_GREATER
        private sealed class ListDummy<T>
        {
            public T[] Items = null!;
        }
#endif
    }
}