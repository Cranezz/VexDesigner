namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Drives a screw through metal and puts a nut on it, without a person.
    ///
    /// Fastening is geometry three layers deep - a screw's pose comes from a
    /// hole, what it passes through comes from its pose, and where a nut goes
    /// comes from what it passes through - so an error anywhere in the chain
    /// surfaces as a nut floating an eighth of an inch off a plate, which is
    /// both hard to see and hard to attribute. Checking the numbers directly
    /// says which link broke.
    /// </summary>
    public static class FastenerTests
    {
        private const float InchesToMetres = 0.0254f;

        /// <summary>A thousandth of an inch. Tighter than anything visible.</summary>
        private const float Tolerance = 0.001f * InchesToMetres;

        private static int failures;
        private static int checks;

        [MenuItem("VexDesigner/Run Fastener Tests")]
        public static void Run()
        {
            failures = 0;
            checks = 0;

            // Every case starts in an empty workshop. Sharing one scene left
            // each test's parts sitting in the next test's way - three
            // C-channels at the origin, so a screw through "one wall" reported
            // the same wall three times and every distance after that was
            // measured from the wrong place.
            Case(ScrewThroughOneWall);
            Case(NutOnTheEnd);
            Case(NutInAGap);
            Case(ScrewTooShortForANut);

            if (failures == 0)
            {
                Debug.Log($"[FastenerTests] All {checks} checks passed.");
            }
            else
            {
                Debug.LogError($"[FastenerTests] {failures} of {checks} checks FAILED.");
            }
        }

        private static void Case(System.Action<List<GameObject>> test)
        {
            var rubbish = new List<GameObject>();

            try
            {
                test(rubbish);
            }
            finally
            {
                foreach (GameObject go in rubbish)
                {
                    if (go != null)
                    {
                        Object.DestroyImmediate(go);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Cases
        // ------------------------------------------------------------------

        /// <summary>
        /// A screw driven into a C-channel should sit with its head exactly on
        /// the surface, and should report the wall it went through.
        /// </summary>
        private static void ScrewThroughOneWall(List<GameObject> rubbish)
        {
            PartDefinition channelDef = Load("CCHL-2");
            PartDefinition screwDef = Load("276-4996");   // 1 inch

            if (channelDef == null || screwDef == null)
            {
                return;
            }

            GameObject channel = Spawn(channelDef, Vector3.zero, Quaternion.identity, rubbish);
            var holes = channel.GetComponent<PartHoles>();

            HoleHit hole = holes.FaceAt(0, false);

            GameObject screw = Spawn(screwDef, Vector3.zero, Quaternion.identity, rubbish);

            FastenerFitting.ScrewPose(
                screwDef, Quaternion.identity, hole, Vector3.one,
                out Vector3 position, out Quaternion rotation);

            screw.transform.SetPositionAndRotation(position, rotation);

            var placed = screw.AddComponent<PlacedScrew>();
            placed.RecomputePasses();

            // The head has to land on the metal, not near it.
            Near("screw head seats on the hole face",
                Vector3.Distance(placed.Seat, hole.WorldPosition), 0f);

            // And point into the material rather than out of it.
            Near("screw points into the material",
                Vector3.Dot(placed.Direction, -hole.WorldNormal), 1f, 0.001f);

            Dump("one wall", placed);
            True("screw finds the wall it went through", placed.Passes.Count >= 1);

            if (placed.Passes.Count >= 1)
            {
                ScrewPass first = placed.Passes[0];

                Near("first wall starts at the head", first.Entry, 0f);

                // VEX structural aluminium is 1/16 in. The first version of
                // this check said an eighth and was simply wrong about the
                // stock, which is worth knowing since screw lengths are chosen
                // against how many walls they have to cross.
                Near("wall is 1/16 in thick", first.Thickness / InchesToMetres, 0.0625f, 0.01f);

                True("a plain hole does not grip", !first.Grips);
            }

            True("nothing is fastened without a nut", placed.GripDepth() < 0f);
        }

        /// <summary>
        /// A nut offered to that screw should seat against the far face of the
        /// last wall, and should join the stack.
        /// </summary>
        private static void NutOnTheEnd(List<GameObject> rubbish)
        {
            PartDefinition channelDef = Load("CCHL-2");
            PartDefinition screwDef = Load("276-4996");
            PartDefinition nutDef = Load("275-1028");     // hex

            if (channelDef == null || screwDef == null || nutDef == null)
            {
                return;
            }

            GameObject channel = Spawn(channelDef, Vector3.zero, Quaternion.identity, rubbish);
            HoleHit hole = channel.GetComponent<PartHoles>().FaceAt(0, false);

            PlacedScrew placed = DriveScrew(screwDef, hole, rubbish);
            if (placed == null)
            {
                return;
            }

            float lastExit = 0f;
            foreach (ScrewPass pass in placed.Passes)
            {
                lastExit = Mathf.Max(lastExit, pass.Exit);
            }

            // Aimed at the end of the shank, past everything.
            Ray aim = AimAt(placed, (lastExit + placed.Length) * 0.5f);

            NutSeating seating = FastenerFitting.FindNutSeating(placed, nutDef, aim);

            True("a seat is offered", seating.IsValid);
            True("the nut fits on a 1 in screw through one wall", seating.Fits);
            False("it is not a gap fitting", seating.InGap);

            Near("nut seats on the last face", seating.Distance, lastExit);

            FastenerFitting.NutPose(
                nutDef, Quaternion.identity, seating, Vector3.one,
                out Vector3 position, out Quaternion rotation);

            GameObject nut = Spawn(nutDef, position, rotation, rubbish);

            // The nut's own near face has to end up on the seating point, or it
            // hangs off the metal it is supposed to be tightened against.
            var nutHoles = nut.GetComponent<PartHoles>();
            HoleHit nearFace = NearestFace(nutHoles, seating.WorldPosition);

            Near("nut face is flush with the metal",
                Vector3.Distance(nearFace.WorldPosition, seating.WorldPosition), 0f);

            placed.AttachNut(nut.GetComponent<PartInstance>(), seating.Distance);

            True("the nut makes the screw grip", placed.GripDepth() >= 0f);

            PartGroup group = placed.GetComponent<PartInstance>().Group;
            True("channel joins the screw's assembly",
                group == channel.GetComponent<PartInstance>().Group);
            True("nut joins the screw's assembly",
                group == nut.GetComponent<PartInstance>().Group);
        }

        /// <summary>
        /// Two walls with air between them: pointing at the air should offer a
        /// seat there rather than at the end.
        /// </summary>
        private static void NutInAGap(List<GameObject> rubbish)
        {
            PartDefinition channelDef = Load("CCHL-2");
            PartDefinition screwDef = Load("276-8016");   // 2.5 inch
            PartDefinition nutDef = Load("275-1028");

            if (channelDef == null || screwDef == null || nutDef == null)
            {
                return;
            }

            GameObject first = Spawn(channelDef, Vector3.zero, Quaternion.identity, rubbish);
            HoleHit hole = first.GetComponent<PartHoles>().FaceAt(0, false);

            // A second channel a clear inch below the first, lined up on the
            // same hole, so the screw passes through both with a gap between.
            Vector3 down = -hole.WorldNormal * (1f * InchesToMetres);
            GameObject second = Spawn(channelDef, down, Quaternion.identity, rubbish);

            PlacedScrew placed = DriveScrew(screwDef, hole, rubbish);
            if (placed == null)
            {
                return;
            }

            Dump("two channels an inch apart", placed);
            True("screw reaches both channels", placed.Passes.Count >= 2);

            if (placed.Passes.Count < 2)
            {
                return;
            }

            float gapStart = placed.Passes[0].Exit;
            float gapEnd = placed.Passes[1].Entry;

            True("there is a gap between them", gapEnd > gapStart);

            // Pointed at the middle of the bare shank between the two walls.
            Ray aim = AimAt(placed, (gapStart + gapEnd) * 0.5f);
            NutSeating seating = FastenerFitting.FindNutSeating(placed, nutDef, aim);

            True("the gap offers a seat", seating.IsValid);
            True("it is recognised as a gap fitting", seating.InGap);

            Near("nut tightens against the wall above the gap",
                seating.Distance, gapStart);

            True("it fits", seating.Fits);

            // And pointing past everything still gives the end, not the gap.
            Ray endAim = AimAt(placed, (placed.Passes[1].Exit + placed.Length) * 0.5f);
            NutSeating endSeating = FastenerFitting.FindNutSeating(placed, nutDef, endAim);

            False("pointing past the stack is not a gap fitting", endSeating.InGap);
            Near("and seats on the last face", endSeating.Distance, placed.Passes[1].Exit);

            Object.DestroyImmediate(second);
        }

        /// <summary>
        /// The shortest screw through two walls has no thread left for a nut,
        /// and has to say so.
        /// </summary>
        private static void ScrewTooShortForANut(List<GameObject> rubbish)
        {
            PartDefinition channelDef = Load("CCHL-2");
            PartDefinition screwDef = Load("276-4990");   // 1/4 inch
            PartDefinition nutDef = Load("275-1028");

            if (channelDef == null || screwDef == null || nutDef == null)
            {
                return;
            }

            GameObject channel = Spawn(channelDef, Vector3.zero, Quaternion.identity, rubbish);
            HoleHit hole = channel.GetComponent<PartHoles>().FaceAt(0, false);

            PlacedScrew placed = DriveScrew(screwDef, hole, rubbish);
            if (placed == null)
            {
                return;
            }

            // A hex nut is 0.122 in thick and a quarter-inch screw has about
            // 0.125 in of thread past a 1/8 in wall, so this is genuinely
            // marginal - which is the interesting case.
            float shank = placed.Length / InchesToMetres;
            float used = (placed.Passes.Count > 0 ? placed.Passes[0].Exit : 0f) / InchesToMetres;
            float spare = shank - used;

            Debug.Log(
                $"[FastenerTests] 1/4 in screw: {spare:0.000} in of thread spare, " +
                $"hex nut needs {nutDef.fastener.thicknessInches:0.000} in.");

            bool shouldFit = spare >= nutDef.fastener.thicknessInches - 0.0001f;

            Ray aim = AimAt(placed, placed.Length * 0.95f);
            NutSeating seating = FastenerFitting.FindNutSeating(placed, nutDef, aim);

            True("fit reported matches the arithmetic", seating.Fits == shouldFit);

            // And a nut thicker than the whole screw must always be refused.
            True("an impossible nut is refused",
                !placed.NutFits(used * InchesToMetres, placed.Length * 2f));
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>Prints what a screw runs through, head first.</summary>
        private static void Dump(string what, PlacedScrew screw)
        {
            var text = new System.Text.StringBuilder();
            text.Append($"[FastenerTests] {what}: shank ")
                .Append($"{screw.Length / InchesToMetres:0.000} in, ")
                .Append($"{screw.Passes.Count} pass(es)");

            foreach (ScrewPass pass in screw.Passes)
            {
                text.AppendLine()
                    .Append($"    {pass.Part.name}[{pass.HoleIndex}] ")
                    .Append($"{pass.Entry / InchesToMetres:0.000} to ")
                    .Append($"{pass.Exit / InchesToMetres:0.000} in")
                    .Append(pass.Grips ? " (grips)" : string.Empty);
            }

            Debug.Log(text.ToString());
        }

        private static PlacedScrew DriveScrew(
            PartDefinition screwDef, HoleHit hole, List<GameObject> rubbish)
        {
            FastenerFitting.ScrewPose(
                screwDef, Quaternion.identity, hole, Vector3.one,
                out Vector3 position, out Quaternion rotation);

            GameObject screw = Spawn(screwDef, position, rotation, rubbish);

            var placed = screw.AddComponent<PlacedScrew>();
            placed.RecomputePasses();
            return placed;
        }

        /// <summary>A ray pointing at a spot on the screw, from the side.</summary>
        private static Ray AimAt(PlacedScrew screw, float distanceAlong)
        {
            Vector3 point = screw.Seat + (screw.Direction * distanceAlong);

            // Any direction across the screw will do; the seating code only
            // uses where the ray passes closest to the shank.
            Vector3 across = Vector3.Cross(screw.Direction, Vector3.up);

            if (across.sqrMagnitude < 1e-6f)
            {
                across = Vector3.Cross(screw.Direction, Vector3.right);
            }

            across.Normalize();
            return new Ray(point + (across * 0.3f), -across);
        }

        private static HoleHit NearestFace(PartHoles holes, Vector3 point)
        {
            HoleHit best = default;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < holes.Holes.Count; i++)
            {
                for (int side = 0; side < 2; side++)
                {
                    HoleHit hit = holes.FaceAt(i, side == 1);
                    float distance = Vector3.Distance(hit.WorldPosition, point);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = hit;
                    }
                }
            }

            return best;
        }

        private static PartDefinition Load(string id)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:PartDefinition"))
            {
                var definition = AssetDatabase.LoadAssetAtPath<PartDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (definition != null && definition.Matches(id))
                {
                    return definition;
                }
            }

            Debug.LogError($"[FastenerTests] No part with ID '{id}'. Test skipped.");
            failures++;
            return null;
        }

        private static GameObject Spawn(
            PartDefinition definition, Vector3 position, Quaternion rotation,
            List<GameObject> rubbish)
        {
            GameObject go = PartFactory.Create(definition, withPhysics: false);
            go.transform.SetPositionAndRotation(position, rotation);
            rubbish.Add(go);
            return go;
        }

        // ------------------------------------------------------------------
        // Assertions
        // ------------------------------------------------------------------

        private static void Near(string what, float actual, float expected, float tolerance = -1f)
        {
            checks++;
            float limit = tolerance < 0f ? Tolerance : tolerance;

            if (Mathf.Abs(actual - expected) > limit)
            {
                failures++;
                Debug.LogError(
                    $"[FastenerTests] FAILED: {what}. " +
                    $"Expected {expected:0.00000}, got {actual:0.00000} " +
                    $"(off by {Mathf.Abs(actual - expected):0.00000}).");
            }
        }

        private static void True(string what, bool condition)
        {
            checks++;

            if (!condition)
            {
                failures++;
                Debug.LogError($"[FastenerTests] FAILED: {what}.");
            }
        }

        private static void False(string what, bool condition) => True(what, !condition);
    }
}
