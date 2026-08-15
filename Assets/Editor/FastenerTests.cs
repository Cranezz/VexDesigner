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
            Case(GroupingFormsAndComesApart);
            Case(TwoScrewsHoldWhenOneComesOff);
            Case(MatedPartsSitSquare);
            Case(FrozenSurvivesUngrouping);
            Case(TightNutStillSeats);
            Case(ChainOfThreeStaysOneAssembly);
            Case(NutSeatsWhereverYouPoint);
            Case(WeldedAssemblyIsOneBody);

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

            Assembly.Rebuild();
            placed.RecomputePasses();

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

            True("a seat is offered exactly when one fits", seating.IsValid == shouldFit);

            // And a nut thicker than the whole screw must always be refused.
            True("an impossible nut is refused",
                !placed.NutFits(used * InchesToMetres, placed.Length * 2f));
        }

        /// <summary>
        /// A screw and nut through two channels joins all four; taking the nut
        /// off puts them back to being four separate parts.
        /// </summary>
        private static void GroupingFormsAndComesApart(List<GameObject> rubbish)
        {
            if (!Stack(rubbish, out GameObject upper, out GameObject lower,
                    out PlacedScrew screw, out GameObject nut, 0f))
            {
                return;
            }

            PartGroup group = screw.GetComponent<PartInstance>().Group;

            True("upper channel is in the assembly",
                group == upper.GetComponent<PartInstance>().Group);
            True("lower channel is in the assembly",
                group == lower.GetComponent<PartInstance>().Group);
            True("nut is in the assembly",
                group == nut.GetComponent<PartInstance>().Group);
            Near("all four parts are one group", group.Members.Count, 4f, 0.1f);

            // Taken off the way grabbing it does: rebuilt as though the nut
            // were already in the user's hand.
            Assembly.Rebuild(nut.GetComponent<PartInstance>());

            True("upper channel comes free",
                upper.GetComponent<PartInstance>().Group !=
                lower.GetComponent<PartInstance>().Group);

            Near("the screw is alone again",
                screw.GetComponent<PartInstance>().Group.Members.Count, 1f, 0.1f);
        }

        /// <summary>
        /// The case that made incremental merging untenable: two screws through
        /// the same pair of parts. Removing one nut must not release them.
        /// </summary>
        private static void TwoScrewsHoldWhenOneComesOff(List<GameObject> rubbish)
        {
            if (!Stack(rubbish, out GameObject upper, out GameObject lower,
                    out PlacedScrew first, out GameObject firstNut, 0f))
            {
                return;
            }

            // A second screw and nut through the next hole along, on the same
            // two channels.
            PartDefinition screwDef = Load("276-4996");
            PartDefinition nutDef = Load("275-1028");

            HoleHit second = upper.GetComponent<PartHoles>().FaceAt(1, false);
            PlacedScrew secondScrew = DriveScrew(screwDef, second, rubbish);

            if (secondScrew == null)
            {
                return;
            }

            if (!FitNut(secondScrew, nutDef, rubbish, out GameObject secondNut))
            {
                return;
            }

            True("both channels are joined",
                upper.GetComponent<PartInstance>().Group ==
                lower.GetComponent<PartInstance>().Group);

            // First nut off. The second screw is still holding everything.
            Assembly.Rebuild(firstNut.GetComponent<PartInstance>());

            True("two screws, one nut removed: still joined",
                upper.GetComponent<PartInstance>().Group ==
                lower.GetComponent<PartInstance>().Group);

            True("the loosened nut is no longer part of it",
                firstNut.GetComponent<PartInstance>().Group !=
                upper.GetComponent<PartInstance>().Group);

            // Second nut off as well. Now nothing holds them, so they part.
            Object.DestroyImmediate(firstNut);
            Assembly.Rebuild(secondNut.GetComponent<PartInstance>());

            True("last nut removed: they come apart",
                upper.GetComponent<PartInstance>().Group !=
                lower.GetComponent<PartInstance>().Group);

        }

        /// <summary>
        /// Two channels a set distance apart, a screw through hole 0 of the
        /// upper one, and a nut on the end of it.
        /// </summary>
        private static bool Stack(
            List<GameObject> rubbish, out GameObject upper, out GameObject lower,
            out PlacedScrew screw, out GameObject nut, float gapInches)
        {
            upper = null;
            lower = null;
            screw = null;
            nut = null;

            PartDefinition channelDef = Load("CCHL-2");
            PartDefinition screwDef = Load("276-4996");
            PartDefinition nutDef = Load("275-1028");

            if (channelDef == null || screwDef == null || nutDef == null)
            {
                return false;
            }

            upper = Spawn(channelDef, Vector3.zero, Quaternion.identity, rubbish);
            HoleHit hole = upper.GetComponent<PartHoles>().FaceAt(0, false);

            Vector3 down = -hole.WorldNormal *
                ((0.0625f + gapInches) * InchesToMetres);

            lower = Spawn(channelDef, down, Quaternion.identity, rubbish);

            screw = DriveScrew(screwDef, hole, rubbish);

            return screw != null && FitNut(screw, nutDef, rubbish, out nut);
        }

        private static bool FitNut(
            PlacedScrew screw, PartDefinition nutDef, List<GameObject> rubbish,
            out GameObject nut)
        {
            nut = null;

            float lastExit = 0f;
            foreach (ScrewPass pass in screw.Passes)
            {
                lastExit = Mathf.Max(lastExit, pass.Exit);
            }

            Ray aim = AimAt(screw, (lastExit + screw.Length) * 0.5f);
            NutSeating seating = FastenerFitting.FindNutSeating(screw, nutDef, aim);

            if (!seating.IsValid || !seating.Fits)
            {
                True("a nut fits on the test stack", false);
                return false;
            }

            FastenerFitting.NutPose(
                nutDef, Quaternion.identity, seating, Vector3.one,
                out Vector3 position, out Quaternion rotation);

            nut = Spawn(nutDef, position, rotation, rubbish);
            Assembly.Rebuild();
            return true;
        }

        /// <summary>
        /// Mating two parts hole to hole must leave them square to each other,
        /// from any starting orientation. If it does not, no amount of snapped
        /// rotation afterwards can reach parallel.
        /// </summary>
        private static void MatedPartsSitSquare(List<GameObject> rubbish)
        {
            PartDefinition channelDef = Load("CCHL-2");

            if (channelDef == null)
            {
                return;
            }

            GameObject fixedPart = Spawn(
                channelDef, Vector3.zero, Quaternion.Euler(11f, 27f, 5f), rubbish);

            HoleHit target = fixedPart.GetComponent<PartHoles>().FaceAt(3, false);

            // Several awkward starting orientations. The case that broke the
            // old code had the part's reference axis nearly along the mating
            // axis, so these include near-degenerate angles rather than tidy
            // ones.
            var starts = new[]
            {
                Quaternion.identity,
                Quaternion.Euler(0f, 43f, 0f),
                Quaternion.Euler(89f, 12f, 3f),
                Quaternion.Euler(0f, 0f, 91f),
                Quaternion.Euler(178f, 4f, 87f),
            };

            GameObject mover = Spawn(channelDef, Vector3.zero, Quaternion.identity, rubbish);
            var moverHoles = mover.GetComponent<PartHoles>();

            for (int i = 0; i < starts.Length; i++)
            {
                mover.transform.rotation = starts[i];
                HoleHit held = moverHoles.FaceAt(0, false);

                HoleMating.ComputePose(
                    held.Face, starts[i], target, 90f, 0f, Vector3.one,
                    out _, out Quaternion mated, out _);

                True("mated square from start " + i,
                    IsSquareWith(fixedPart.transform.rotation, mated));
            }

            // A snapped manual roll must keep it square, since the increment
            // divides a quarter turn.
            HoleHit again = moverHoles.FaceAt(0, false);
            var rolls = new[] { 15f, 90f, 180f, 345f };

            for (int i = 0; i < rolls.Length; i++)
            {
                HoleMating.ComputePose(
                    again.Face, Quaternion.identity, target, 90f, rolls[i], Vector3.one,
                    out _, out Quaternion rolled, out _);

                bool square = IsSquareWith(fixedPart.transform.rotation, rolled);
                bool shouldBeSquare = Mathf.Approximately(rolls[i] % 90f, 0f);

                True("roll of " + rolls[i] + " degrees squares correctly",
                    square == shouldBeSquare);
            }
        }

        /// <summary>
        /// Taking an assembly apart must not un-freeze what is left, or a
        /// pinned build drops to the bench the moment a nut comes off.
        /// </summary>
        private static void FrozenSurvivesUngrouping(List<GameObject> rubbish)
        {
            if (!Stack(rubbish, out GameObject upper, out GameObject lower,
                    out PlacedScrew screw, out GameObject nut, 0f))
            {
                return;
            }

            screw.GetComponent<PartInstance>().Group.SetFrozen(true);

            True("the whole assembly is frozen",
                upper.GetComponent<PartInstance>().IsFrozen &&
                lower.GetComponent<PartInstance>().IsFrozen);

            Assembly.Rebuild(nut.GetComponent<PartInstance>());

            True("upper channel is still frozen after ungrouping",
                upper.GetComponent<PartInstance>().IsFrozen);
            True("lower channel is still frozen after ungrouping",
                lower.GetComponent<PartInstance>().IsFrozen);
            True("the screw is still frozen after ungrouping",
                screw.GetComponent<PartInstance>().IsFrozen);
        }

        /// <summary>
        /// A screw that only just protrudes should still take a nut.
        /// </summary>
        private static void TightNutStillSeats(List<GameObject> rubbish)
        {
            PartDefinition channelDef = Load("CCHL-2");
            PartDefinition screwDef = Load("276-4990");   // 1/4 in
            PartDefinition nutDef = Load("275-1028");     // hex, 0.122 in

            if (channelDef == null || screwDef == null || nutDef == null)
            {
                return;
            }

            GameObject upper = Spawn(channelDef, Vector3.zero, Quaternion.identity, rubbish);
            HoleHit hole = upper.GetComponent<PartHoles>().FaceAt(0, false);

            // A second wall flush against the first: two sixteenths of an inch
            // used, an eighth left, and the nut wants 0.122 of it.
            Vector3 down = -hole.WorldNormal * (0.0625f * InchesToMetres);
            Spawn(channelDef, down, Quaternion.identity, rubbish);

            PlacedScrew screw = DriveScrew(screwDef, hole, rubbish);
            if (screw == null)
            {
                return;
            }

            Dump("tight stack", screw);

            float used = 0f;
            foreach (ScrewPass pass in screw.Passes)
            {
                used = Mathf.Max(used, pass.Exit);
            }

            float spare = (screw.Length - used) / InchesToMetres;

            Debug.Log("[FastenerTests] tight nut: " + spare.ToString("0.0000") +
                      " in spare, nut is " +
                      nutDef.fastener.thicknessInches.ToString("0.0000") + " in.");

            Ray aim = AimAt(screw, screw.Length * 0.99f);
            NutSeating seating = FastenerFitting.FindNutSeating(screw, nutDef, aim);

            True("a tight nut is still offered a seat", seating.IsValid);

            if (!seating.IsValid)
            {
                return;
            }

            FastenerFitting.NutPose(
                nutDef, Quaternion.identity, seating, Vector3.one,
                out Vector3 position, out Quaternion rotation);

            Spawn(nutDef, position, rotation, rubbish);
            Assembly.Rebuild();
            screw.RecomputePasses();

            Dump("tight stack with nut", screw);

            True("a tight nut still registers as gripping", screw.GripDepth() >= 0f);
        }

        /// <summary>
        /// Adding a part to a robot must join it, not move the joint.
        ///
        /// Three channels in a stack: the first screw holds the top two, the
        /// second holds the bottom two. The middle channel is in both, so all
        /// three have to end up in one assembly - a new part must not take its
        /// neighbour out of the group it was already in.
        /// </summary>
        private static void ChainOfThreeStaysOneAssembly(List<GameObject> rubbish)
        {
            PartDefinition channelDef = Load("CCHL-2");
            PartDefinition screwDef = Load("276-4996");
            PartDefinition nutDef = Load("275-1028");

            if (channelDef == null || screwDef == null || nutDef == null)
            {
                return;
            }

            GameObject top = Spawn(channelDef, Vector3.zero, Quaternion.identity, rubbish);
            HoleHit first = top.GetComponent<PartHoles>().FaceAt(0, false);

            float wall = 0.0625f * InchesToMetres;

            GameObject middle = Spawn(
                channelDef, -first.WorldNormal * wall, Quaternion.identity, rubbish);

            // The first screw, through the top two only.
            PlacedScrew screwA = DriveScrew(screwDef, first, rubbish);
            if (screwA == null || !FitNut(screwA, nutDef, rubbish, out GameObject nutA))
            {
                return;
            }

            True("first two channels are joined",
                top.GetComponent<PartInstance>().Group ==
                middle.GetComponent<PartInstance>().Group);

            // Now a third channel, screwed to the middle one through a
            // different hole - the case the user hit.
            GameObject bottom = Spawn(
                channelDef, -first.WorldNormal * (wall * 2f), Quaternion.identity, rubbish);

            HoleHit second = middle.GetComponent<PartHoles>().FaceAt(1, false);
            PlacedScrew screwB = DriveScrew(screwDef, second, rubbish);

            if (screwB == null || !FitNut(screwB, nutDef, rubbish, out GameObject nutB))
            {
                return;
            }

            PartGroup group = middle.GetComponent<PartInstance>().Group;

            True("the top channel is still in the assembly",
                top.GetComponent<PartInstance>().Group == group);
            True("the bottom channel joined it",
                bottom.GetComponent<PartInstance>().Group == group);
            True("the first screw is still in it",
                screwA.GetComponent<PartInstance>().Group == group);
            True("the second screw is in it",
                screwB.GetComponent<PartInstance>().Group == group);
            True("both nuts are in it",
                nutA.GetComponent<PartInstance>().Group == group &&
                nutB.GetComponent<PartInstance>().Group == group);

            Debug.Log("[FastenerTests] chain of three: " + group.Members.Count +
                      " parts in one assembly");

            // Seven: three channels, two screws, two nuts.
            Near("nothing was left behind", group.Members.Count, 7f, 0.1f);
        }

        /// <summary>
        /// A nut must be offered a seat from anywhere on the screw.
        ///
        /// Pointing at the head, at the metal, or past the end are all just
        /// ways of saying "put a nut on this screw", and there is only one
        /// sensible answer to that. Refusing unless the cursor happened to
        /// land on the one workable stretch of thread made short screws
        /// impossible to fit at all.
        /// </summary>
        private static void NutSeatsWhereverYouPoint(List<GameObject> rubbish)
        {
            PartDefinition channelDef = Load("CCHL-2");
            PartDefinition screwDef = Load("276-4992");   // 1/2 in
            PartDefinition nutDef = Load("275-1028");

            if (channelDef == null || screwDef == null || nutDef == null)
            {
                return;
            }

            GameObject channel = Spawn(channelDef, Vector3.zero, Quaternion.identity, rubbish);
            HoleHit hole = channel.GetComponent<PartHoles>().FaceAt(0, false);

            PlacedScrew screw = DriveScrew(screwDef, hole, rubbish);
            if (screw == null)
            {
                return;
            }

            float expected = 0f;
            foreach (ScrewPass pass in screw.Passes)
            {
                expected = Mathf.Max(expected, pass.Exit);
            }

            // Right at the head, in the metal, and past the end. Every one of
            // them has to give the same answer, because there is only one
            // place on this screw a nut can go.
            var wheres = new[] { 0f, 0.5f, 0.99f };

            for (int i = 0; i < wheres.Length; i++)
            {
                Ray aim = AimAt(screw, screw.Length * wheres[i]);
                NutSeating seating = FastenerFitting.FindNutSeating(screw, nutDef, aim);

                True("a seat is offered at " + wheres[i] + " along", seating.IsValid);

                if (seating.IsValid)
                {
                    Near("and it is the only one there is at " + wheres[i],
                        seating.Distance, expected);
                }
            }

            // A bare screw takes a nut right up against the head.
            PartDefinition loneDef = Load("276-4996");
            GameObject lone = Spawn(loneDef, new Vector3(2f, 0f, 0f), Quaternion.identity, rubbish);
            var bare = lone.AddComponent<PlacedScrew>();
            bare.RecomputePasses();

            Ray bareAim = AimAt(bare, bare.Length * 0.5f);
            NutSeating bareSeat = FastenerFitting.FindNutSeating(bare, nutDef, bareAim);

            True("a bare screw takes a nut", bareSeat.IsValid);
            Near("tightened against the head", bareSeat.Distance, 0f);
        }

        /// <summary>
        /// An assembly must be one rigid body, not several that agree about
        /// where they are.
        ///
        /// Anything less falls apart under gravity: each part its own body,
        /// each finding its own way to the floor. One Rigidbody with all the
        /// colliders parented to it cannot come apart at any speed.
        /// </summary>
        private static void WeldedAssemblyIsOneBody(List<GameObject> rubbish)
        {
            if (!Stack(rubbish, out GameObject upper, out GameObject lower,
                    out PlacedScrew screw, out GameObject nut, 0f))
            {
                return;
            }

            PartGroup group = screw.GetComponent<PartInstance>().Group;

            True("the assembly is welded", group.IsWelded);

            if (!group.IsWelded)
            {
                return;
            }

            // The heaviest part carries the body: a robot swinging about its
            // C-channel behaves far better than one swinging about a nut.
            True("the heaviest part leads",
                group.Leader == upper.GetComponent<PartInstance>() ||
                group.Leader == lower.GetComponent<PartInstance>());

            int bodies = 0;

            foreach (PartInstance part in group.Members)
            {
                if (part.GetComponent<Rigidbody>() != null)
                {
                    bodies++;
                }

                if (part != group.Leader)
                {
                    True(part.name + " is parented to the leader",
                        part.transform.parent == group.Leader.transform);
                }
            }

            Near("exactly one body for the whole assembly", bodies, 1f, 0.1f);

            // And taking it apart gives every part its body back, or a
            // dismantled robot would be a pile of things that cannot move.
            Assembly.Rebuild(nut.GetComponent<PartInstance>());

            True("the channels are apart again",
                upper.GetComponent<PartInstance>().Group !=
                lower.GetComponent<PartInstance>().Group);

            True("the freed nut has its own body",
                nut.GetComponent<Rigidbody>() != null);
            True("and is no longer parented to anything",
                nut.transform.parent == null);
        }

        /// <summary>
        /// True when two orientations differ only by whole quarter turns - the
        /// parts are parallel, however they are laid out.
        /// </summary>
        private static bool IsSquareWith(Quaternion a, Quaternion b)
        {
            Quaternion difference = Quaternion.Inverse(a) * b;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };

            for (int i = 0; i < axes.Length; i++)
            {
                Vector3 turned = difference * axes[i];

                // Each axis has to land on a signed unit axis: one component
                // near one, the other two near zero.
                float largest = Mathf.Max(
                    Mathf.Abs(turned.x), Mathf.Max(Mathf.Abs(turned.y), Mathf.Abs(turned.z)));

                if (largest < 0.999f)
                {
                    return false;
                }
            }

            return true;
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
            // With physics, so welding has real bodies to absorb and give back.
            // Nothing moves: the editor does not step the simulation.
            GameObject go = PartFactory.Create(definition, withPhysics: true);
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
