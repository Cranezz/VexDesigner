namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Builds runtime GameObjects from <see cref="PartDefinition"/> assets.
    ///
    /// Kept separate from the definition itself because a definition is
    /// document data - it describes the part, it does not know about Unity
    /// scene objects. That split is what lets the same definitions be used by
    /// a headless server later (ARCHITECTURE.md section 6).
    /// </summary>
    public static class PartFactory
    {
        // One material per definition, shared by every instance of that part.
        // Without this, each spawned part would create its own material and
        // break batching - a robot is hundreds of parts, so this matters.
        private static readonly Dictionary<PartDefinition, Material> MaterialCache =
            new Dictionary<PartDefinition, Material>();

        public static GameObject Create(PartDefinition definition, bool withPhysics)
        {
            if (definition == null || definition.mesh == null)
            {
                Debug.LogError("[PartFactory] Definition or its mesh is missing.");
                return null;
            }

            var go = new GameObject(definition.displayName);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = definition.mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetSharedMaterial(definition);

            var instance = go.AddComponent<PartInstance>();
            instance.Initialise(definition);

            // Placed parts are pickable again, so a robot can be rearranged
            // rather than only ever added to.
            go.AddComponent<Highlightable>();
            go.AddComponent<PickupHandle>();
            go.AddComponent<PartImpactAudio>();

            var collider = go.AddComponent<MeshCollider>();

            // A non-convex MeshCollider cannot move under physics in Unity, so
            // a dynamic part has to use the convex hull.
            //
            // KNOWN LIMITATION: for a C-channel the hull fills in the channel,
            // so parts cannot yet nest inside one another. Fine for "parts rest
            // on the table instead of floating", which is what this supports
            // today. Proper interlocking needs compound colliders built from
            // the part profile - a later job, and one that pairs naturally with
            // hole detection since both need the part's real structure.
            collider.convex = true;
            collider.sharedMesh = definition.mesh;

            if (withPhysics)
            {
                AddPhysics(go, definition);
            }

            return go;
        }

        public static void AddPhysics(GameObject go, PartDefinition definition)
        {
            var body = go.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = go.AddComponent<Rigidbody>();
            }

            body.mass = definition.MassKilograms;

            // Speculative rather than ContinuousDynamic.
            //
            // ContinuousDynamic sweeps only against static geometry and other
            // continuous bodies, and it ignores rotation entirely. Speculative
            // contacts widen the collision check by the distance the body will
            // travel this step, catching fast movement against everything -
            // which is what stopped parts being shoved through the bench when
            // dragged quickly.
            //
            // A 1/4 inch screw is about 6 mm across, so at any real speed it
            // covers several times its own size per physics step. Without
            // continuous detection of some kind it does not so much collide as
            // teleport past.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // Physics runs at a fixed rate that rarely matches the frame rate.
            // Interpolation smooths the visual position between steps, which is
            // most of what made a carried part look like it lagged the cursor.
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // Aluminium on a rubber mat does not slide far or bounce.
            body.linearDamping = 0.15f;
            body.angularDamping = 0.6f;

            // Settle sooner. The default threshold leaves a stack of small
            // light parts trembling almost indefinitely, because their
            // residual energy never quite falls below it.
            body.sleepThreshold = 0.012f;
        }

        /// <summary>
        /// Shared material for a part type. Public so shelf display copies use
        /// the identical material and batch with placed parts rather than
        /// doubling the draw calls.
        /// </summary>
        public static Material GetSharedMaterial(PartDefinition definition)
        {
            if (MaterialCache.TryGetValue(definition, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");

            var material = new Material(shader) { name = $"{definition.displayName} (runtime)" };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", definition.colour);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", definition.colour);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", definition.smoothness);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", definition.metallic);
            }

            // Emission on, black by default: invisible until Highlightable
            // drives it. The keyword cannot be enabled from a property block,
            // so it has to be set here or highlighting silently does nothing.
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags =
                MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", Color.black);
            }

            MaterialCache[definition] = material;
            return material;
        }
    }
}
