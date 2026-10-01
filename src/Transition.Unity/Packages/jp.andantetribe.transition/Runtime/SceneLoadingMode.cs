namespace Transition
{
    /// <summary>Controls missing-scene loads within one transition.</summary>
    public enum SceneLoadingMode
    {
        /// <summary>Loads missing scenes in target order.</summary>
        Sequential,

        /// <summary>Starts missing-scene loads concurrently and observes every started operation.</summary>
        Parallel,
    }
}