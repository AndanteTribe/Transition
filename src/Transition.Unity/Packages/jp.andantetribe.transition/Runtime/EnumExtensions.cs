using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Transition
{
    /// <summary>
    /// Extension methods for enumerations.
    /// </summary>
    internal static class EnumExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasBitFlags<T>(this T value, T flag) where T : struct, Enum
        {
            var v = UnsafeUtility.As<T, int>(ref value);
            var f = UnsafeUtility.As<T, int>(ref flag);
            return (v & f) == f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T AggregateFlags<T>(this Span<T> flags) where T : struct, Enum => AggregateFlags((ReadOnlySpan<T>)flags);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T AggregateFlags<T>(this ReadOnlySpan<T> flags) where T : struct, Enum
        {
            var r = 0;
            var e = flags.GetEnumerator();
            while (e.MoveNext())
            {
                var flag = e.Current;
                var f = UnsafeUtility.As<T, int>(ref flag);
                r |= f;
            }
            return UnsafeUtility.As<int, T>(ref r);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Enumerator<T> GetEnumerator<T>(this T value) where T : struct, Enum => new(value);

        /// <summary>
        /// Enumerator for bit flags in an enum value.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        public struct Enumerator<T> where T : struct, Enum
        {
            private int _value;

            /// <see cref="System.Collections.Generic.IEnumerator{T}.Current"/>
            public T Current { get; private set; }

            internal Enumerator(T value)
            {
                _value = UnsafeUtility.As<T, int>(ref value);
                Current = default;
            }

            /// <see cref="System.Collections.IEnumerator.MoveNext"/>
            public bool MoveNext()
            {
                if (_value == 0)
                {
                    return false;
                }

                var f = _value & -_value; // get lowest flag
                Current = UnsafeUtility.As<int, T>(ref f);
                _value &= ~f;
                return true;
            }
        }
    }
}