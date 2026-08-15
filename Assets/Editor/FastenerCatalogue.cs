namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// The catalogue figures for VEX fasteners, and the command that writes
    /// them onto the part definitions.
    ///
    /// Typed out rather than derived. Names, part numbers and pack weights come
    /// from the vendor and cannot be recovered from a mesh, and the classifier
    /// that fills the rest in on import can only guess from a filename - which
    /// is how a hex nut ended up called "8 32 hex nut 275 1028" weighing
    /// whatever its volume implied.
    ///
    /// Geometry is a different matter and is measured, not typed: which end of
    /// a screw is the head, and how far the head stands above the surface, are
    /// read off the mesh so they cannot drift from the model.
    /// </summary>
    public static class FastenerCatalogue
    {
        private const float InchesToMetres = 0.0254f;

        /// <summary>
        /// Across the flats for a standard nut, in inches - the spanner size.
        ///
        /// Not the height, which was the first reading of it and was wrong. The
        /// models settled it: every nut here measures a corner-to-flat ratio of
        /// 1.152 against a hexagon's 1.1547, so the figure that lands on 11/32
        /// is the width across the flats. The heights are quite different from
        /// each other - a hex nut is an eighth of an inch thick, a nylock half
        /// again as much - which is exactly why height has to be measured per
        /// part rather than declared once for the whole class.
        /// </summary>
        private const float StandardWrenchInches = 11f / 32f;

        /// <summary>Across the flats for a low-profile nut, in inches.</summary>
        private const float LowProfileWrenchInches = 0.25f;

        private struct Entry
        {
            public string sku;
            public string name;
            public string data1;
            public string data2;

            /// <summary>Grams for one piece, from the pack weight divided out.</summary>
            public float grams;

            /// <summary>Catalogue length under the head, in inches. Screws only.</summary>
            public float lengthInches;

            /// <summary>Across the flats, in inches. Nuts only.</summary>
            public float wrenchInches;
        }

        /// <summary>
        /// Nuts. Weights are the 100-pack figure divided by a hundred, which is
        /// the only per-piece number the vendor publishes; it carries the bag
        /// with it, so treat it as within a tenth of a gram rather than exact.
        /// </summary>
        private static readonly Entry[] Nuts =
        {
            new Entry { sku = "275-1026", name = "#8-32 Keps Nut",
                        data1 = "Keps", data2 = "#8-32",
                        grams = 1.3f, wrenchInches = StandardWrenchInches },

            new Entry { sku = "275-1027", name = "#8-32 Nylock Nut",
                        data1 = "Nylock", data2 = "#8-32",
                        grams = 1.8f, wrenchInches = StandardWrenchInches },

            new Entry { sku = "275-1028", name = "#8-32 Hex Nut",
                        data1 = "Hex", data2 = "#8-32",
                        grams = 1.2f, wrenchInches = StandardWrenchInches },

            new Entry { sku = "276-7767", name = "#8-32 Low Profile Nut",
                        data1 = "Low Profile", data2 = "#8-32",
                        grams = 1.3f, wrenchInches = LowProfileWrenchInches },
        };

        /// <summary>
        /// Screws. Lengths are measured under the head, which is what the
        /// catalogue name means and what decides how much material the screw
        /// can pass through.
        /// </summary>
        private static readonly Entry[] Screws =
        {
            Screw("276-4990", "1/4\"",  0.250f, 0.5f),
            Screw("276-4991", "3/8\"",  0.375f, 0.6f),
            Screw("276-4992", "1/2\"",  0.500f, 0.7f),
            Screw("276-4993", "5/8\"",  0.625f, 0.8f),
            Screw("276-4994", "3/4\"",  0.750f, 0.9f),
            Screw("276-4995", "7/8\"",  0.875f, 1.0f),
            Screw("276-4996", "1\"",    1.000f, 1.2f),
            Screw("276-4997", "1-1/4\"", 1.250f, 1.4f),
            Screw("276-4998", "1-1/2\"", 1.500f, 1.6f),
            Screw("276-4999", "1-3/4\"", 1.750f, 1.8f),
            Screw("276-5004", "2\"",    2.000f, 2.0f),
            Screw("276-8015", "2-1/4\"", 2.250f, 2.2f),
            Screw("276-8016", "2-1/2\"", 2.500f, 2.4f),
        };

        private static Entry Screw(string sku, string size, float inches, float grams)
        {
            return new Entry
            {
                sku = sku,
                name = $"#8-32 x {size} Star Drive Screw",
                data1 = size,
                data2 = "Star Drive",
                grams = grams,
                lengthInches = inches,
            };
        }

        [MenuItem("VexDesigner/Apply Fastener Catalogue")]
        public static void Apply()
        {
            var definitions = new List<PartDefinition>();

            foreach (string guid in AssetDatabase.FindAssets("t:PartDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<PartDefinition>(path);
                if (definition != null)
                {
                    definitions.Add(definition);
                }
            }

            int applied = 0;

            foreach (Entry entry in Nuts)
            {
                applied += ApplyTo(definitions, entry, isScrew: false) ? 1 : 0;
            }

            foreach (Entry entry in Screws)
            {
                applied += ApplyTo(definitions, entry, isScrew: true) ? 1 : 0;
            }

            foreach (Entry entry in Nuts)
            {
                PartDefinition definition = definitions.Find(d => d.Matches(entry.sku));
                if (definition != null)
                {
                    BakeNutHole(definition, entry);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Fasteners] Applied catalogue data to {applied} parts.");
        }

        /// <summary>
        /// Gives a nut its one hole, measured rather than detected.
        ///
        /// The general detector samples with rays and lands a face wherever a
        /// sample happened to cross the surface, which is close enough on a
        /// C-channel with a hundred and seventy holes and wrong here: a nut has
        /// exactly one hole, it is on the axis, and its faces are the faces of
        /// the nut. Anything approximate would leave a screw standing a
        /// thousandth of an inch proud of the metal, on every nut in the build.
        /// </summary>
        private static void BakeNutHole(PartDefinition definition, Entry entry)
        {
            Mesh mesh = definition.mesh;
            if (mesh == null)
            {
                return;
            }

            Vector3 axis = definition.fastener.localAxis.normalized;
            Vector3 centre = mesh.bounds.center;

            Vector3[] vertices = mesh.vertices;

            // Flush by construction: the faces sit exactly at the extremes of
            // the mesh along the axis, so the opening is the surface rather
            // than a plane floating near it.
            float minAlong = float.MaxValue;
            float maxAlong = float.MinValue;

            // The bore is whatever lies closest to the axis. On a nut nothing
            // is nearer the centre line than the thread, so the minimum radius
            // is the bore radius without needing to know the thread size.
            float bore = float.MaxValue;

            foreach (Vector3 vertex in vertices)
            {
                Vector3 offset = vertex - centre;
                float along = Vector3.Dot(offset, axis);

                minAlong = Mathf.Min(minAlong, along);
                maxAlong = Mathf.Max(maxAlong, along);

                bore = Mathf.Min(bore, Vector3.ProjectOnPlane(offset, axis).magnitude);
            }

            float measuredInches = (maxAlong - minAlong) / InchesToMetres;

            // How much shank the nut takes up is its own height, and every nut
            // has a different one. Measured rather than declared, so the nut
            // seats exactly against the metal instead of a fraction of an inch
            // off it.
            definition.fastener.thicknessInches = measuredInches;

            // Scale is checked against the bore rather than against the
            // spanner size. Measuring across the flats needs geometry at
            // mid-height and these models have none - a nut is drawn as two end
            // faces and a skin between them, so there is nothing in the middle
            // to measure. The bore is unambiguous, is present in every model,
            // and is the dimension that actually has to be right, since it is
            // what a screw is threaded through.
            float boreInches = bore * 2f / InchesToMetres;

            if (boreInches < 0.10f || boreInches > 0.22f)
            {
                Debug.LogWarning(
                    $"[Fasteners] {entry.sku} has a {boreInches:0.000} in bore, " +
                    "which is not a #8-32 hole (0.136 in tapped, 0.164 in major). " +
                    "Check the model's scale.");
            }

            // Radius doubled to a width, then eased outward a touch. The hole
            // is what a screw is aimed at, and a target measured to the exact
            // wall of the bore is harder to hit than the real hole looks.
            float width = bore * 2f * 1.06f;

            var hole = new Hole
            {
                front = new HoleFace
                {
                    localPosition = centre + (axis * maxAlong),
                    localNormal = axis,
                    width = width,
                },
                back = new HoleFace
                {
                    localPosition = centre + (axis * minAlong),
                    localNormal = -axis,
                    width = width,
                },
                depth = maxAlong - minAlong,

                // The whole purpose of a nut. A screw that reaches one clamps
                // everything between its head and this into one assembly.
                type = HoleType.Threaded,

                // Drilled and tapped, not broached square like VEX structure.
                shape = HoleShape.Round,
            };

            definition.holeSet = new HoleSet
            {
                holes = new[] { hole },
                measuredPitchInches = 0f,
                generatedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            };

            Debug.Log(
                $"[Fasteners] {entry.sku} {entry.name}: bore " +
                $"{width / InchesToMetres:0.000} in, height {measuredInches:0.000} in, " +
                $"threaded, seats on {Fraction(entry.wrenchInches)} flats.");
        }

        /// <summary>Renders a measurement the way a catalogue writes it.</summary>
        private static string Fraction(float inches)
        {
            foreach (int denominator in new[] { 32, 16, 8, 4, 2 })
            {
                float numerator = inches * denominator;

                if (Mathf.Abs(numerator - Mathf.Round(numerator)) < 0.001f)
                {
                    int n = Mathf.RoundToInt(numerator);
                    int d = denominator;

                    // Reduce, so 8/32 reads as the quarter inch it is.
                    while (n % 2 == 0 && d % 2 == 0 && d > 1)
                    {
                        n /= 2;
                        d /= 2;
                    }

                    return d == 1 ? $"{n}\"" : $"{n}/{d}\"";
                }
            }

            return $"{inches:0.000}\"";
        }

        private static PartDefinition Find(List<PartDefinition> definitions, string sku)
        {
            PartDefinition byId = definitions.Find(d => d.Matches(sku));
            if (byId != null)
            {
                return byId;
            }

            // Fall back to the asset name, which carries the part number
            // because the importer put it there. Parts brought in before the
            // library settled on part numbers were given hand-written IDs, and
            // those should be corrected rather than left to sit alongside a
            // catalogue keyed on the vendor's numbering.
            return definitions.Find(d => d.name.Contains(sku));
        }

        private static bool ApplyTo(List<PartDefinition> definitions, Entry entry, bool isScrew)
        {
            PartDefinition definition = Find(definitions, entry.sku);

            if (definition == null)
            {
                Debug.LogWarning($"[Fasteners] No part definition found for {entry.sku}.");
                return false;
            }

            Undo.RecordObject(definition, "Apply fastener catalogue");

            if (definition.saving.id != entry.sku)
            {
                // The old ID is kept rather than dropped. That is the whole
                // point of the legacy list: anything already saved under it
                // still resolves, so correcting an ID costs nothing.
                var legacy = new List<string>(definition.saving.legacyIds);

                if (!string.IsNullOrEmpty(definition.saving.id) &&
                    !legacy.Contains(definition.saving.id))
                {
                    legacy.Add(definition.saving.id);
                }

                Debug.Log(
                    $"[Fasteners] {definition.name}: ID '{definition.saving.id}' " +
                    $"-> '{entry.sku}', old ID kept as legacy.");

                definition.saving.legacyIds = legacy.ToArray();
                definition.saving.id = entry.sku;
            }

            definition.data.partName = entry.name;
            definition.data.data1 = entry.data1;
            definition.data.data2 = isScrew
                ? entry.data2
                : $"{entry.data2}, {Fraction(entry.wrenchInches)} across flats";
            definition.data.weightGrams = entry.grams;
            definition.data.material = PartMaterial.Steel;
            definition.data.partClass = PartClass.Structure;
            definition.data.cuttable = false;

            // A screw is a thing that goes *into* holes and has none of its own;
            // a nut has exactly one, and it is the whole point of the part.
            definition.data.subClass = isScrew ? PartSubClass.Screw : PartSubClass.Nut;
            definition.data.hasHoles = !isScrew;

            MeasureGeometry(definition, entry, isScrew);

            EditorUtility.SetDirty(definition);
            return true;
        }

        /// <summary>
        /// Reads the shank axis and head height off the mesh.
        ///
        /// The axis is the longest side of the bounding box, which for a screw
        /// or a nut is unambiguous. Its *sign* is not, and that matters - it
        /// decides which end of a screw goes into the metal. That is settled by
        /// weighing each half: the head end carries more of the mesh than the
        /// thread does.
        /// </summary>
        private static void MeasureGeometry(PartDefinition definition, Entry entry, bool isScrew)
        {
            var fastener = definition.fastener ?? new FastenerData();
            definition.fastener = fastener;

            fastener.shankLengthInches = isScrew ? entry.lengthInches : 0f;

            Mesh mesh = definition.mesh;
            if (mesh == null)
            {
                Debug.LogWarning($"[Fasteners] {entry.sku} has no mesh; axis not measured.");
                return;
            }

            Bounds bounds = mesh.bounds;
            Vector3 size = bounds.size;

            int longest;

            if (isScrew)
            {
                // A screw is far longer than it is wide, so its bounding box
                // names the shank without ambiguity.
                longest = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
            }
            else if (!TryFindBoreAxis(mesh, out longest))
            {
                Debug.LogError(
                    $"[Fasteners] {entry.sku}: no through-hole found on any axis. " +
                    "The mesh does not look like a nut; hole not baked.");
                return;
            }

            Vector3 axis = longest == 0 ? Vector3.right : (longest == 1 ? Vector3.up : Vector3.forward);

            float half = size[longest] * 0.5f;
            float centre = bounds.center[longest];

            if (isScrew)
            {
                // Which half holds more vertices. A pan head is a wide disc and
                // a thread is a thin cylinder, so the count is decisive rather
                // than marginal.
                int positive = 0;
                int negative = 0;

                foreach (Vector3 vertex in mesh.vertices)
                {
                    if (vertex[longest] > centre)
                    {
                        positive++;
                    }
                    else
                    {
                        negative++;
                    }
                }

                // Axis points head -> tip, so it runs away from the heavy end.
                if (positive > negative)
                {
                    axis = -axis;
                }

                float meshLengthInches = size[longest] / InchesToMetres;

                // Whatever the mesh has that the catalogue length does not is
                // the head. Clamped at zero because a model slightly shorter
                // than its nominal size should read as "no head", not as a
                // negative one that would push the screw out of the hole.
                fastener.headHeightInches =
                    Mathf.Max(0f, meshLengthInches - entry.lengthInches);

                // The underside of the head: where the screw meets the surface
                // it is driven into.
                float headFace = (positive > negative ? 1f : -1f) *
                    (half - (fastener.headHeightInches * InchesToMetres));

                Vector3 seat = bounds.center;
                seat[longest] = centre + headFace;
                fastener.localSeatPoint = seat;

                Debug.Log(
                    $"[Fasteners] {entry.sku} {entry.name}: shank " +
                    $"{entry.lengthInches:0.000} in, head " +
                    $"{fastener.headHeightInches:0.000} in, mesh " +
                    $"{meshLengthInches:0.000} in along {axis}.");

                if (fastener.headHeightInches > 0.25f)
                {
                    Debug.LogWarning(
                        $"[Fasteners] {entry.sku} head measures " +
                        $"{fastener.headHeightInches:0.000} in, which is too tall " +
                        "for a pan head. Check the catalogue length against the mesh.");
                }
            }
            else
            {
                // A nut has a right way up, and the model says which. The
                // flange of a keps nut and the flat face of a nylock are the
                // ends that meet the metal, and both are the *wider* end -
                // which is measurable, unlike the part number.
                //
                // Before this the nut simply used whichever end was already
                // nearer the screw, so a keps nut went on upside down half the
                // time, sitting on its washer instead of clamping with it.
                float band = half * 0.5f;

                float positiveRadius = 0f;
                float negativeRadius = 0f;

                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 offset = vertex - bounds.center;
                    float along = offset[longest];

                    if (Mathf.Abs(along) < band)
                    {
                        continue;
                    }

                    float radius = Vector3.ProjectOnPlane(offset, axis).magnitude;

                    if (along > 0f)
                    {
                        positiveRadius = Mathf.Max(positiveRadius, radius);
                    }
                    else
                    {
                        negativeRadius = Mathf.Max(negativeRadius, radius);
                    }
                }

                // The axis runs from the seating face toward the free end, so
                // a nut threaded on always points its wide end at the metal.
                bool seatOnPositive = positiveRadius >= negativeRadius;

                if (seatOnPositive)
                {
                    axis = -axis;
                }

                Vector3 seat = bounds.center;
                seat[longest] = bounds.center[longest] + (seatOnPositive ? half : -half);
                fastener.localSeatPoint = seat;

                Debug.Log(
                    $"[Fasteners] {entry.sku} seats on its " +
                    $"{(seatOnPositive ? "positive" : "negative")} face " +
                    $"({Mathf.Max(positiveRadius, negativeRadius) / InchesToMetres:0.000} in " +
                    $"across, against " +
                    $"{Mathf.Min(positiveRadius, negativeRadius) / InchesToMetres:0.000} in).");
            }

            fastener.localAxis = axis;
        }

        /// <summary>
        /// Finds which way a nut's bore runs, by looking for the way through it.
        ///
        /// The bounding box cannot answer this and the first attempt to make it
        /// try was wrong in an instructive way: a #8-32 hex nut is 11/32 of an
        /// inch tall and 3/8 of an inch across the flats, so its *longest* side
        /// is across the flats and the bore was measured sideways through solid
        /// metal. Every figure that followed - height, bore diameter, seating
        /// face - was then measuring the wrong thing, which is exactly what the
        /// four contradictory bore diameters were saying.
        ///
        /// A hole, though, is defined by being empty: a line down the true axis
        /// leaves the mesh alone, and a line down either of the others does not.
        /// So the test is simply which direction the part can be seen through.
        /// </summary>
        private static bool TryFindBoreAxis(Mesh mesh, out int axisIndex)
        {
            axisIndex = -1;

            MeshRayTester tester = MeshRayTester.For(mesh);
            if (tester == null)
            {
                return false;
            }

            Bounds bounds = mesh.bounds;
            float reach = bounds.size.magnitude * 2f;

            for (int i = 0; i < 3; i++)
            {
                Vector3 direction = i == 0 ? Vector3.right
                    : i == 1 ? Vector3.up : Vector3.forward;

                Vector3 origin = bounds.center - (direction * reach * 0.5f);

                if (!tester.FirstCrossing(origin, direction, reach, out _))
                {
                    axisIndex = i;
                    return true;
                }
            }

            return false;
        }
    }
}
