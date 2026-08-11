namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// The catalogue entry for one VEX part.
    ///
    /// This is *document* data in the sense of ARCHITECTURE.md section 6: it
    /// describes what a part IS, independent of any instance sitting on the
    /// table. A saved robot references parts by <see cref="partId"/>, never by
    /// mesh or object reference, which is what lets a save file stay small and
    /// survive both serialisation and the network.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Part_",
        menuName = "VexDesigner/Part Definition",
        order = 0)]
    public sealed class PartDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("VEX SKU, e.g. 276-2289. This is the stable ID that save " +
                 "files and network messages refer to. Changing it breaks " +
                 "existing saves, so treat it as permanent.")]
        public string partId = "";

        [Tooltip("Human-readable name shown in the UI.")]
        public string displayName = "";

        [Header("Geometry")]
        [Tooltip("Full-detail mesh, already scaled to metres by the part " +
                 "import postprocessor.")]
        public Mesh mesh;

        [Header("Physical")]
        [Tooltip("Mass in grams. VEX publishes part weights; using real values " +
                 "means a built robot has a believable centre of mass rather " +
                 "than behaving like a balloon.")]
        public float massGrams = 100f;

        [Header("Appearance")]
        public Color colour = new Color(0.68f, 0.70f, 0.74f);

        [Tooltip("How reflective the surface is. Aluminium is fairly smooth.")]
        [Range(0f, 1f)] public float smoothness = 0.55f;

        [Tooltip("Metallic response. VEX structural parts are bare aluminium.")]
        [Range(0f, 1f)] public float metallic = 0.85f;

        /// <summary>Mass in the kilograms Unity's physics expects.</summary>
        public float MassKilograms => Mathf.Max(0.001f, massGrams / 1000f);

        /// <summary>
        /// Longest dimension of the mesh in inches. Useful for sanity checks:
        /// VEX parts are near-exact multiples of the 0.5" hole pitch, so an
        /// implausible number here means the import scale went wrong.
        /// </summary>
        public float LongestDimensionInches =>
            mesh == null ? 0f : Mathf.Max(
                mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z) / 0.0254f;
    }
}
