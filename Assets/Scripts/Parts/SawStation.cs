namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// The table saw: where a part is set up and cut to length.
    ///
    /// The machine holds one part at a time, resting on the bed and against the
    /// fence exactly as a real one does, and the settings are the ones a real
    /// saw has - how the stock is turned, how far it is fed past the blade, and
    /// what angle the blade is swung to. Those settings are the whole of the
    /// cut: everything else is derived from them, including the plane that
    /// actually divides the metal and the four numbers written to the save
    /// file.
    ///
    /// The saw's local frame, which the rest of this file assumes:
    ///   +X runs along the fence, to the right, and is the direction stock is
    ///      fed past the blade;
    ///   +Y is up, off the bed;
    ///   +Z is toward the fence at the back.
    /// </summary>
    public sealed class SawStation : MonoBehaviour
    {
        private const float InchesToMetres = 0.0254f;

        [Header("Machine")]
        [Tooltip("Top of the bed the stock rests on, in local space.")]
        [SerializeField] private float bedY;

        [Tooltip("Face of the fence the stock is pushed against, in local space.")]
        [SerializeField] private float fenceZ;

        [Tooltip("Where the blade crosses the bed, along the fence.")]
        [SerializeField] private float bladeX;

        [Header("Where the player stands to use it")]
        [SerializeField] private Transform viewpoint;

        [Header("Cosmetic")]
        [SerializeField] private Transform bladeVisual;

        // --- Settings, which are the cut -----------------------------------

        /// <summary>How the stock is turned on the bed, in degrees per axis.</summary>
        public Vector3 Rotation { get; private set; }

        /// <summary>
        /// How far the stock is fed past the blade, in inches. Zero is the
        /// end of the stock exactly at the blade, so this is also the length
        /// of the piece about to come off.
        /// </summary>
        public float FeedInches { get; private set; }

        /// <summary>Blade swing, in degrees. Zero is square across.</summary>
        public float BladeAngle { get; private set; }

        public PartInstance Docked { get; private set; }

        public bool HasPart => Docked != null;

        /// <summary>Where the camera goes when the saw is being used.</summary>
        public Transform Viewpoint => viewpoint;

        /// <summary>Every saw in the workshop. There is one, but it is found rather than wired.</summary>
        private static readonly List<SawStation> Live = new List<SawStation>();

        public static IReadOnlyList<SawStation> All => Live;

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        // ------------------------------------------------------------------
        // The blade
        // ------------------------------------------------------------------

        /// <summary>
        /// The cutting plane, in world space, with its normal pointing at the
        /// piece being kept.
        ///
        /// Swinging the blade turns it about the vertical, which is a mitre -
        /// the cut a saw head actually makes when it swivels. Tilting it the
        /// other way would be a bevel and needs a second axis the machine does
        /// not have.
        /// </summary>
        public Plane CutPlane
        {
            get
            {
                Vector3 normal = transform.TransformDirection(
                    Quaternion.AngleAxis(BladeAngle, Vector3.up) * Vector3.left);

                Vector3 point = transform.TransformPoint(new Vector3(bladeX, bedY, fenceZ));

                return new Plane(normal.normalized, point);
            }
        }

        // ------------------------------------------------------------------
        // Loading the machine
        // ------------------------------------------------------------------

        /// <summary>
        /// Takes a part and sets it up on the bed in its default position.
        /// </summary>
        public bool Dock(PartInstance part)
        {
            if (part == null || part.Definition == null || !part.Definition.cuttable)
            {
                return false;
            }

            Release();

            Docked = part;
            Rotation = Vector3.zero;
            BladeAngle = 0f;
            FeedInches = 0f;

            var body = part.GetComponent<Rigidbody>();

            if (body != null)
            {
                // Clamped, in the sense a real vice clamps: it does not fall
                // off the bed while it is being set up.
                body.isKinematic = true;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Reseat();
            return true;
        }

        /// <summary>Lets the part go, leaving it where it sits.</summary>
        public void Release()
        {
            if (Docked == null)
            {
                return;
            }

            SawPreview.Restore(Docked);

            var body = Docked.GetComponent<Rigidbody>();

            if (body != null)
            {
                body.isKinematic = Docked.IsFrozen;
                body.useGravity = !Docked.IsFrozen;
            }

            Docked = null;
        }

        // ------------------------------------------------------------------
        // Setting up the cut
        // ------------------------------------------------------------------

        public void SetRotation(int axis, float degrees)
        {
            Vector3 rotation = Rotation;
            rotation[Mathf.Clamp(axis, 0, 2)] = Normalise(degrees);

            Rotation = rotation;
            Reseat();
        }

        public void SetFeed(float inches)
        {
            FeedInches = Mathf.Max(0f, inches);
            Reseat();
        }

        public void SetBladeAngle(float degrees)
        {
            BladeAngle = Mathf.Clamp(degrees, 0f, 90f);

            if (bladeVisual != null)
            {
                bladeVisual.localRotation = Quaternion.AngleAxis(BladeAngle, Vector3.up);
            }

            Reseat();
        }

        /// <summary>
        /// Folds any angle onto 0-360, so a knob that has been spun round
        /// several times and a number typed in as -45 mean the same thing.
        /// </summary>
        public static float Normalise(float degrees)
        {
            float wrapped = degrees % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }

        // ------------------------------------------------------------------
        // Resting on the machine
        // ------------------------------------------------------------------

        /// <summary>
        /// Puts the stock back down on the bed and against the fence.
        ///
        /// Called after every change, because every change can move it. Turning
        /// a part about its own centre swings some corner of it below the bed
        /// or through the fence, and a part buried in the machine is both wrong
        /// and impossible to judge. Reseating from the turned bounding box
        /// means the part rises to sit on whatever corner is now lowest and
        /// slides forward to touch on whatever is now furthest back - it lifts,
        /// as the user put it, but never stops touching.
        /// </summary>
        private void Reseat()
        {
            if (Docked == null)
            {
                return;
            }

            Transform part = Docked.transform;

            part.rotation = transform.rotation *
                Quaternion.Euler(Rotation.x, Rotation.y, Rotation.z);

            // Measured in the saw's frame, since that is where the bed and the
            // fence are flat.
            if (!LocalBounds(out Bounds bounds))
            {
                return;
            }

            // Feed is measured from the blade to the end of the stock, so the
            // amount past the blade is exactly the piece about to come off.
            float wantedMaxX = bladeX + (FeedInches * InchesToMetres);

            var offset = new Vector3(
                wantedMaxX - bounds.max.x,
                bedY - bounds.min.y,
                fenceZ - bounds.max.z);

            part.position += transform.TransformVector(offset);
        }

        /// <summary>
        /// The docked part's bounds expressed in the saw's own frame.
        ///
        /// Not the renderer's world bounds, which are axis-aligned to the world
        /// and would report a turned part as far larger than it is - and would
        /// then seat it floating above the bed by the error.
        /// </summary>
        private bool LocalBounds(out Bounds bounds)
        {
            bounds = default;

            var filter = Docked.GetComponent<MeshFilter>();

            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            Matrix4x4 toSaw = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Bounds local = filter.sharedMesh.bounds;

            Vector3 centre = local.center;
            Vector3 extents = local.extents;

            bool first = true;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    centre.x + ((i & 1) == 0 ? -extents.x : extents.x),
                    centre.y + ((i & 2) == 0 ? -extents.y : extents.y),
                    centre.z + ((i & 4) == 0 ? -extents.z : extents.z));

                Vector3 inSaw = toSaw.MultiplyPoint3x4(corner);

                if (first)
                {
                    bounds = new Bounds(inSaw, Vector3.zero);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(inSaw);
                }
            }

            return true;
        }

        /// <summary>
        /// Length of stock past the blade, in inches - what the cut removes.
        /// </summary>
        public float OffcutInches => FeedInches;

        /// <summary>
        /// How long the stock is along the feed direction, in inches, so the
        /// interface can stop the user feeding the whole part past the blade.
        /// </summary>
        public float StockLengthInches
        {
            get
            {
                if (Docked == null || !LocalBounds(out Bounds bounds))
                {
                    return 0f;
                }

                return bounds.size.x / InchesToMetres;
            }
        }

        // ------------------------------------------------------------------
        // Cutting
        // ------------------------------------------------------------------

        /// <summary>
        /// Takes the cut, and leaves the part on the bed ready for another.
        /// </summary>
        public bool Cut()
        {
            if (Docked == null)
            {
                return false;
            }

            Plane world = CutPlane;
            Transform part = Docked.transform;

            // Into the part's own space, which is where a cut is recorded. The
            // part can then be moved, rotated, saved and reloaded and the cut
            // still means the same thing.
            Vector3 localNormal = part.InverseTransformDirection(world.normal).normalized;
            Vector3 pointOnPlane = part.InverseTransformPoint(
                world.normal * -world.distance);

            var localPlane = new Plane(localNormal, pointOnPlane);

            if (!PartCutting.Cut(Docked, localPlane, FeedInches, BladeAngle))
            {
                return false;
            }

            // The stock is shorter now, so it has to be re-seated - and the
            // feed goes back to zero, because the new end of the stock is the
            // face the blade just made.
            FeedInches = 0f;
            Reseat();
            SawPreview.Apply(Docked, this);

            return true;
        }
    }
}
