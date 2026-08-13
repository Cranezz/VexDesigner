namespace VexDesigner.EditorTools
{
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Runs hole detection across the whole library at once.
    ///
    /// Separate from the Part Inspector because the two answer different
    /// questions: the inspector is for looking closely at one part while
    /// working out whether detection is right, this is for re-running it over
    /// everything once it is.
    /// </summary>
    public static class HoleBatch
    {
        [MenuItem("VexDesigner/Detect Holes (All Parts)")]
        public static void DetectAllMenuItem()
        {
            DetectAll();
        }

        public static void DetectAllFromCommandLine()
        {
            try
            {
                DetectAll();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Holes] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void DetectAll()
        {

            string[] guids = AssetDatabase.FindAssets("t:PartDefinition");
            int processed = 0;
            int withHoles = 0;

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var part = AssetDatabase.LoadAssetAtPath<PartDefinition>(
                        AssetDatabase.GUIDToAssetPath(guids[i]));

                    if (part == null || part.mesh == null)
                    {
                        continue;
                    }

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Detecting holes",
                            $"{part.displayName}  ({i + 1} of {guids.Length})",
                            i / (float)guids.Length))
                    {
                        break;
                    }

                    // Parts flagged as having no hole pattern are skipped, not
                    // searched and found empty. Searching a wheel wastes time
                    // and risks a false positive on a spoke gap.
                    if (!part.hasHolePattern)
                    {
                        Debug.Log($"[Holes] {part.partId}: skipped (Has Holes is off)");
                        continue;
                    }

                    HoleDetector.Result result =
                        HoleDetector.Detect(part.mesh, part.holePitchInches);

                    part.holeSet = result.Holes;
                    EditorUtility.SetDirty(part);

                    processed++;
                    if (result.Holes.Count > 0)
                    {
                        withHoles++;
                    }

                    Debug.Log($"[Holes] {part.partId}: {result.Summary}");

                    // Positions dumped for offline analysis when tuning the
                    // detector. Off unless diagnostics are on.
                    if (!HoleDetector.Verbose)
                    {
                        continue;
                    }

                    var csv = new System.Text.StringBuilder();
                    csv.AppendLine("ax,ay,az,cx,cy,cz,width,depth");
                    foreach (Hole h in result.Holes.holes)
                    {
                        Vector3 c = h.LocalCentre / 0.0254f;
                        Vector3 a = h.LocalAxis;
                        csv.AppendLine(
                            $"{a.x:F3},{a.y:F3},{a.z:F3},{c.x:F4},{c.y:F4},{c.z:F4}," +
                            $"{h.front.width / 0.0254f:F4},{h.depth / 0.0254f:F4}");
                    }

                    System.IO.File.WriteAllText(
                        $"{Application.dataPath}/../HoleDump_{part.partId}.csv", csv.ToString());
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Holes] Searched {processed} part(s); {withHoles} had holes.");
        }
    }
}
