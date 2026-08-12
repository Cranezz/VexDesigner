namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// The catalogue entry for one VEX part: everything true of the part type,
    /// independent of any instance sitting on the bench.
    ///
    /// This is *document* data in the sense of ARCHITECTURE.md section 6. A
    /// saved robot references parts by <see cref="partId"/> and never by mesh
    /// or object reference, which is what keeps save files small, portable, and
    /// able to survive both serialisation and the network.
    ///
    /// Every field here is editable in the Inspector so parts can be added and
    /// corrected by hand without touching code.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Part_",
        menuName = "VexDesigner/Part Definition",
        order = 0)]
    public sealed class PartDefinition : ScriptableObject
    {
        // ------------------------------------------------------------------
        [Header("Identity")]
        // ------------------------------------------------------------------

        [Tooltip("VEX SKU, e.g. 276-2289.\n\n" +
                 "This is the stable identifier that save files and network " +
                 "messages refer to. Changing it orphans every existing save " +
                 "that used the part, so treat it as permanent once shipped.")]
        public string partId = "";

        [Tooltip("Name shown in the UI and in the parts list.")]
        public string displayName = "";

        [Tooltip("Vendor's size designation, e.g. 1x2x1x35. Free text - it is " +
                 "for humans reading the parts list, not for logic.")]
        public string sizeDesignation = "";

        [Tooltip("Older IDs this part used to be known by.\n\n" +
                 "A save file referencing a retired ID can still be loaded by " +
                 "matching here. Without it, correcting a typo in a part ID " +
                 "silently breaks every robot built with it.")]
        public string[] legacyIds = new string[0];

        // ------------------------------------------------------------------
        [Header("Classification")]
        // ------------------------------------------------------------------

        public PartClass partClass = PartClass.Structure;

        public PartSubClass subClass = PartSubClass.Unknown;

        // ------------------------------------------------------------------
        [Header("Physical")]
        // ------------------------------------------------------------------

        [Tooltip("Weight in pounds, as VEX publishes it.\n\n" +
                 "Pounds rather than grams because that is what the vendor's " +
                 "spec sheets use, so values can be copied across without a " +
                 "conversion step to get wrong.")]
        public float weightPounds = 0.01f;

        [Tooltip("What the part is made of. Drives impact sound now; will drive " +
                 "surface grip and mass estimation for unweighed parts.")]
        public PartMaterial material = PartMaterial.Aluminium;

        [Tooltip("Sliding grip. 0 is ice, 1 is rubber on concrete. Leave at -1 " +
                 "to use the default for the material.")]
        public float frictionOverride = -1f;

        [Tooltip("Centre of mass in local space. Leave at zero to let physics " +
                 "derive it from the collider, which is right for the great " +
                 "majority of parts. Override for anything with concentrated " +
                 "mass, such as a motor.")]
        public Vector3 centreOfMassOverride = Vector3.zero;

        // ------------------------------------------------------------------
        [Header("Geometry")]
        // ------------------------------------------------------------------

        [Tooltip("Full-detail mesh, already scaled to metres by the part " +
                 "import postprocessor.")]
        public Mesh mesh;

        [Tooltip("Whether this part can be cut on the saw. Structure can; " +
                 "screws, motors and wheels cannot.")]
        public bool cuttable;

        [Tooltip("Whether the part carries the standard VEX hole grid.\n\n" +
                 "Hole detection uses this to decide whether to look at all - " +
                 "searching a wheel for a hole lattice wastes time and can " +
                 "produce false positives.")]
        public bool hasHolePattern;

        [Tooltip("Spacing between hole centres, in inches. 0.5 for standard " +
                 "VEX structure.")]
        public float holePitchInches = 0.5f;

        // ------------------------------------------------------------------
        [Header("Appearance")]
        // ------------------------------------------------------------------

        public Color colour = new Color(0.68f, 0.70f, 0.74f);

        [Range(0f, 1f)] public float smoothness = 0.55f;

        [Range(0f, 1f)] public float metallic = 0.85f;

        // ------------------------------------------------------------------
        [Header("Notes")]
        // ------------------------------------------------------------------

        [TextArea(2, 5)]
        [Tooltip("Anything worth recording: where the model came from, quirks, " +
                 "measurements that were checked by hand.")]
        public string notes = "";

        // ------------------------------------------------------------------
        // Derived
        // ------------------------------------------------------------------

        private const float PoundsToKilograms = 0.45359237f;

        /// <summary>Mass in the kilograms Unity's physics expects.</summary>
        public float MassKilograms => Mathf.Max(0.0005f, weightPounds * PoundsToKilograms);

        /// <summary>Convenience for display; VEX small parts are easier in grams.</summary>
        public float MassGrams => weightPounds * PoundsToKilograms * 1000f;

        /// <summary>
        /// Longest dimension in inches. A sanity check: VEX parts are near-exact
        /// multiples of the hole pitch, so an implausible number here means the
        /// import scale went wrong.
        /// </summary>
        public float LongestDimensionInches =>
            mesh == null ? 0f : Mathf.Max(
                mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z) / 0.0254f;

        /// <summary>
        /// True when <paramref name="id"/> names this part, whether by its
        /// current ID or one it used to have.
        /// </summary>
        public bool Matches(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            if (string.Equals(partId, id, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (string legacy in legacyIds)
            {
                if (string.Equals(legacy, id, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
