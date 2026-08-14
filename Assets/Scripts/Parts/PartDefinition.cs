namespace VexDesigner.Parts
{
    using System;
    using UnityEngine;

    /// <summary>
    /// The identity a save file writes down.
    ///
    /// Separated from the rest of the part's data on purpose: everything else
    /// describes what a part *is* and can be corrected at any time, whereas
    /// this is a promise. Change an ID and every robot ever saved with it stops
    /// resolving.
    /// </summary>
    [Serializable]
    public sealed class SavingObject
    {
        [Tooltip("Identifier written to save files, e.g. CCNL-1x2 or SCRW-1.25.\n\n" +
                 "Yours to choose, but permanent once anything has been saved " +
                 "with it. Short and readable beats descriptive.")]
        public string id = "";

        [Tooltip("IDs this part used to be known by.\n\n" +
                 "A save referencing a retired ID still resolves through this " +
                 "list. Without it, correcting a typo in an ID silently breaks " +
                 "every robot built with the part.")]
        public string[] legacyIds = new string[0];
    }

    /// <summary>
    /// What the part is: its specification, and the properties that drive how
    /// it behaves in the workshop.
    /// </summary>
    [Serializable]
    public sealed class PartData
    {
        [Tooltip("Name shown in the UI and the parts list.")]
        public string partName = "";

        [Tooltip("Free specification field - length, size, hole count. Read by " +
                 "people, not by logic, so anything useful can go here.")]
        public string data1 = "";

        [Tooltip("Second free specification field.")]
        public string data2 = "";

        [Tooltip("Weight in grams. Small VEX parts are fractions of a pound, " +
                 "so grams keep the numbers legible - a screw is 0.5 g rather " +
                 "than 0.0011 lb.")]
        public float weightGrams = 10f;

        [Tooltip("Whether the part carries the VEX hole grid.\n\n" +
                 "Hole detection uses this to decide whether to look at all. " +
                 "Searching a wheel for a hole lattice wastes time and invites " +
                 "false positives.")]
        public bool hasHoles;

        [Tooltip("Whether this part can be cut on the saw. Structure can; " +
                 "screws, motors and wheels cannot.")]
        public bool cuttable;

        [Tooltip("Drives impact sound, surface grip, and density-based weight " +
                 "estimation for parts with no measured figure.")]
        public PartMaterial material = PartMaterial.Aluminium;

        public PartClass partClass = PartClass.Structure;

        public PartSubClass subClass = PartSubClass.Unknown;
    }

    /// <summary>
    /// The measurements a fastener needs to be fitted, rather than merely drawn.
    ///
    /// Kept apart from the mesh because none of it can be read off the mesh
    /// reliably. Which end of a screw is the head, how far the thread reaches,
    /// how much shank a nut consumes - a bounding box knows none of that, and
    /// guessing it per frame from geometry would be both slow and wrong.
    ///
    /// Empty for everything that is not a screw or a nut.
    /// </summary>
    [Serializable]
    public sealed class FastenerData
    {
        [Tooltip("Direction the shank runs, in local space, pointing from the " +
                 "head toward the tip. Baked from the mesh.")]
        public Vector3 localAxis = Vector3.forward;

        [Tooltip("Point on the outer face of the head, in local space. This is " +
                 "what lands on the surface a screw is driven into.\n\n" +
                 "For a nut, the face that seats against material.")]
        public Vector3 localSeatPoint = Vector3.zero;

        [Tooltip("Usable length under the head, in inches - the VEX catalogue " +
                 "figure. How much material a screw can pass through.")]
        public float shankLengthInches;

        [Tooltip("Height of the head above the surface, in inches. Baked as " +
                 "whatever the mesh is longer than the catalogue length.")]
        public float headHeightInches;

        [Tooltip("How much shank a nut takes up, in inches. 11/32 for standard " +
                 "nuts, 1/4 for low profile.")]
        public float thicknessInches;

        public bool IsScrew => shankLengthInches > 0f;

        public bool IsNut => thicknessInches > 0f;
    }

    /// <summary>
    /// The catalogue entry for one VEX part: everything true of the part type,
    /// independent of any copy of it sitting on the bench.
    ///
    /// This is *document* data in the sense of ARCHITECTURE.md section 6. A
    /// saved robot references parts by ID and never by mesh or object
    /// reference, which is what keeps save files small, portable, and able to
    /// survive both serialisation and the network.
    ///
    /// Per-instance state lives elsewhere: cuts on <see cref="Modifications"/>,
    /// position and rotation on the transform. Nothing here is ever changed by
    /// something happening to one part on the bench, because every copy of that
    /// part shares it.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Part_",
        menuName = "VexDesigner/Part Definition",
        order = 0)]
    public sealed class PartDefinition : ScriptableObject
    {
        [Header("Saving Object")]
        public SavingObject saving = new SavingObject();

        [Header("Part Data")]
        public PartData data = new PartData();

        [Header("Geometry")]
        [Tooltip("Full-detail mesh, already scaled to metres by the part " +
                 "import postprocessor.")]
        public Mesh mesh;

        [Tooltip("Spacing between hole centres in inches. 0.5 for standard " +
                 "VEX structure.")]
        public float holePitchInches = 0.5f;

        [Header("Fastener (generated — do not hand-edit)")]
        [Tooltip("Screw and nut measurements. Empty for everything else.")]
        public FastenerData fastener = new FastenerData();

        [Header("Holes (generated — do not hand-edit)")]
        [Tooltip("Detected screw holes, computed in the editor and saved.\n\n" +
                 "Never recomputed at runtime: holes are what screws snap to " +
                 "and what save files refer to, so they must be identical in " +
                 "every session and on every machine.")]
        public HoleSet holeSet = new HoleSet();

        [Header("Appearance")]
        public Color colour = new Color(0.68f, 0.70f, 0.74f);
        [Range(0f, 1f)] public float smoothness = 0.55f;
        [Range(0f, 1f)] public float metallic = 0.85f;

        [Header("Advanced physics")]
        [Tooltip("Sliding grip. 0 is ice, 1 is rubber on concrete. Leave at -1 " +
                 "to use the default for the material.")]
        public float frictionOverride = -1f;

        [Tooltip("Centre of mass in local space. Leave at zero to let physics " +
                 "derive it from the collider, which is right for almost every " +
                 "VEX part. Override for concentrated mass, such as a motor.")]
        public Vector3 centreOfMassOverride = Vector3.zero;

        [Header("Notes")]
        [TextArea(2, 5)]
        public string notes = "";

        // ------------------------------------------------------------------
        // Read-only views, so consumers do not reach through two levels of
        // nesting for the values they use constantly.
        // ------------------------------------------------------------------

        public string partId => saving.id;

        public string displayName => data.partName;

        public PartMaterial material => data.material;

        public PartClass partClass => data.partClass;

        public PartSubClass subClass => data.subClass;

        public bool cuttable => data.cuttable;

        public bool hasHolePattern => data.hasHoles;

        public float MassGrams => data.weightGrams;

        public bool IsScrew => data.subClass == PartSubClass.Screw;

        public bool IsNut => data.subClass == PartSubClass.Nut;

        /// <summary>Usable shank length in metres, for screws.</summary>
        public float ShankLengthMetres => fastener.shankLengthInches * 0.0254f;

        /// <summary>How much shank a nut consumes, in metres.</summary>
        public float NutThicknessMetres => fastener.thicknessInches * 0.0254f;

        /// <summary>Mass in the kilograms Unity's physics expects.</summary>
        public float MassKilograms => Mathf.Max(0.0005f, data.weightGrams / 1000f);

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

            if (string.Equals(saving.id, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (string legacy in saving.legacyIds)
            {
                if (string.Equals(legacy, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
