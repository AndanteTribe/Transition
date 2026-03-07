using System;
using System.Buffers;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Handle<T> Rent<T>(this ArrayPool<T> pool, int minimumLength, out T[] array) =>
            new(pool, array = pool.Rent(minimumLength));

        /// <summary>
        /// A handle for returning an array borrowed from <see cref="ArrayPool{T}"/>.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        public readonly struct Handle<T> : IDisposable
        {
            private readonly ArrayPool<T> _pool;
            private readonly T[] _array;

            internal Handle(ArrayPool<T> pool, T[] array)
            {
                _pool = pool;
                _array = array;
            }

            /// <inheritdoc/>
            void IDisposable.Dispose() => _pool.Return(_array);
        }
    }
}