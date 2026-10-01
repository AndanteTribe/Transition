#nullable enable

using System;
using System.Collections.Generic;

namespace Transition
{
    // A request owns its input even while queued. Common calls need neither a params array nor a HashSet.
    // This is an ordinary value type, so retaining it across await is safe.
    internal readonly struct SceneTarget<TScene> where TScene : struct, Enum
    {
        private readonly TScene _scene1;
        private readonly TScene _scene2;
        private readonly TScene _scene3;
        private readonly TScene _scene4;
        private readonly TScene _scene5;
        private readonly TScene[]? _scenes;

        public int Count { get; }

        public SceneTarget(int count, TScene scene1, TScene scene2 = default, TScene scene3 = default,
            TScene scene4 = default, TScene scene5 = default)
        {
            Count = count;
            _scene1 = scene1;
            _scene2 = scene2;
            _scene3 = scene3;
            _scene4 = scene4;
            _scene5 = scene5;
            _scenes = null;
        }

        public SceneTarget(ReadOnlyMemory<TScene> scenes)
        {
            Count = scenes.Length;
            var span = scenes.Span;
            _scene1 = Count > 0 ? span[0] : default;
            _scene2 = Count > 1 ? span[1] : default;
            _scene3 = Count > 2 ? span[2] : default;
            _scene4 = Count > 3 ? span[3] : default;
            _scene5 = Count > 4 ? span[4] : default;
            // ReadOnlyMemory does not prevent its owner from changing the source array during an await.
            _scenes = Count > 5 ? scenes.ToArray() : null;
        }

        public TScene this[int index] => _scenes is not null ? _scenes[index] : index switch
        {
            0 => _scene1,
            1 => _scene2,
            2 => _scene3,
            3 => _scene4,
            4 => _scene5,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public bool Contains(TScene scene) => ContainsBefore(scene, Count);

        public bool IsFirstOccurrence(int index) => !ContainsBefore(this[index], index);

        private bool ContainsBefore(TScene scene, int end)
        {
            for (var i = 0; i < end; i++)
            {
                if (EqualityComparer<TScene>.Default.Equals(this[i], scene))
                {
                    return true;
                }
            }

            return false;
        }
    }
}