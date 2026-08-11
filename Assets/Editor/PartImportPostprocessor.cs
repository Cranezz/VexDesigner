namespace VexDesigner.EditorTools
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Forces correct import settings on every mesh dropped into Assets/Parts.
    ///
    /// The point of this file is that **part scale is never a manual step**.
    /// Getting it wrong on one part out of two hundred would produce a robot
    /// that looks fine until someone measures it, and hole detection would
    /// silently fail on that part alone. So the rule is enforced in code
    /// rather than written in a document and hoped for.
    ///
    /// The unit chain, end to end:
    ///
    ///     VEX STEP file        declares its own unit (inches, for VEX)
    ///       -> FreeCAD         normalises to millimetres on read
    ///       -> OBJ             always millimetres (see tools/step_to_obj.py)
    ///       -> Unity           x 0.001, giving metres
    ///       -> world space     1 unit = 1 metre, 1 inch = 0.0254 units
    ///
    /// Because the OBJ leg is always millimetres, the Unity scale factor is
    /// the same constant for every part forever.
    /// </summary>
    public sealed class PartImportPostprocessor : AssetPostprocessor
    {
        private const string PartsFolder = "Assets/Parts/";
        private const float MillimetresToMetres = 0.001f;

        private void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(PartsFolder))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;

            // useFileScale must be off. OBJ carries no unit information, so
            // leaving it on makes Unity guess, and the guess is not stable
            // across formats.
            importer.useFileScale = false;
            importer.globalScale = MillimetresToMetres;

            // Required to read vertex data at runtime. Without it the mesh
            // lives only on the GPU and cannot be sliced, which is the entire
            // premise of the cutting tool.
            importer.isReadable = true;

            // Compression quantises vertex positions. On a part whose holes
            // are located to thousandths of an inch, that is not acceptable.
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshVertices = false;
            importer.weldVertices = false;

            // Parts can exceed 65k vertices once holes are tessellated finely,
            // and the 16-bit default silently splits the mesh when they do.
            importer.indexFormat = ModelImporterIndexFormat.UInt32;

            // CAD parts are hard-surface. Calculating normals with a tight
            // smoothing angle keeps machined edges crisp instead of rounding
            // them into a soft blob.
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalSmoothingAngle = 30f;

            // Materials, rigs, animation and cameras are all meaningless for a
            // CAD part and only create clutter.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.Replace('\\', '/').StartsWith(PartsFolder))
            {
                return;
            }

            // Report real dimensions in inches at import time. VEX parts are
            // specified in inches and holes sit on a 0.5" pitch, so a wrong
            // scale shows up immediately as a number that is not a sensible
            // multiple - far easier to catch here than after assembly.
            var filter = root.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return;
            }

            Vector3 sizeUnits = filter.sharedMesh.bounds.size;
            Vector3 sizeInches = sizeUnits / 0.0254f;

            Debug.Log(
                $"[Parts] Imported {System.IO.Path.GetFileName(assetPath)}  " +
                $"{sizeInches.x:F3} x {sizeInches.y:F3} x {sizeInches.z:F3} in  " +
                $"({filter.sharedMesh.triangles.Length / 3} tris)");
        }
    }
}
