namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Runtime marker identifying a spawned part and linking it back to its
    /// catalogue entry.
    ///
    /// Everything a save file or a network message needs about this object is
    /// its <see cref="Definition"/> ID plus its transform - never its mesh.
    /// The list of cut operations will live here too once cutting exists.
    /// </summary>
    public sealed class PartInstance : MonoBehaviour
    {
        [SerializeField] private PartDefinition definition;

        public PartDefinition Definition => definition;

        /// <summary>
        /// Stable per-session identity. Becomes the key that cut operations
        /// and part-to-part joins refer to. Assigned rather than derived from
        /// the object name, which users will eventually be able to change.
        /// </summary>
        public int InstanceId { get; private set; }

        private static int nextInstanceId = 1;

        public void Initialise(PartDefinition partDefinition)
        {
            definition = partDefinition;
            InstanceId = nextInstanceId++;
        }
    }
}
