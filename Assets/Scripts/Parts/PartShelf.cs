namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using TMPro;
    using UnityEngine;

    /// <summary>
    /// Lays every catalogued part out on a patch of table, one of each, paged.
    ///
    /// Built at runtime from whatever is in the library rather than baked into
    /// the scene, so converting a new STEP file makes the part appear on the
    /// shelf with no scene rebuild and no manual placement.
    /// </summary>
    public sealed class PartShelf : MonoBehaviour
    {
        private const float InchesToMetres = 0.0254f;

        [Header("Region (inches, measured on the table surface)")]
        [SerializeField] private float regionWidthIn = 16f;
        [SerializeField] private float regionDepthIn = 30f;
        [SerializeField] private float paddingIn = 0.6f;

        [Header("Catalogue")]
        [Tooltip("Resources path the part definitions are loaded from.")]
        [SerializeField] private string resourcePath = "PartLibrary";

        [Tooltip("Populated in the editor as a fallback if the Resources load " +
                 "finds nothing.")]
        [SerializeField] private List<PartDefinition> explicitCatalogue = new List<PartDefinition>();

        [Header("Presentation")]
        [SerializeField] private Material partMaterial;

        [Tooltip("Smallest clickable box, in inches. Screws are only a couple " +
                 "of tenths across and would be near-impossible to click at " +
                 "their true size.")]
        [SerializeField] private float minimumHitSizeIn = 0.45f;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private List<ShelfPlacement> placements;
        private int pageCount = 1;
        private int currentPage;

        // Serialized: the scene builder wires this at edit time, and an
        // unserialized reference would be null again by the time Play starts.
        [SerializeField] private TextMeshPro pageLabel;

        public int CurrentPage => currentPage;
        public int PageCount => pageCount;

        private void Start()
        {
            BuildLayout();
            ShowPage(0);
        }

        private void BuildLayout()
        {
            List<PartDefinition> catalogue = LoadCatalogue();
            if (catalogue.Count == 0)
            {
                Debug.LogWarning(
                    $"[Shelf] No part definitions found under Resources/{resourcePath}. " +
                    "Convert a STEP file and run VexDesigner > Rebuild Part Library.");
                placements = new List<ShelfPlacement>();
                return;
            }

            // Stable order so the shelf does not reshuffle between runs, which
            // would make parts hard to find by memory.
            catalogue.Sort((a, b) => string.CompareOrdinal(a.partId, b.partId));

            placements = ShelfLayout.Arrange(
                catalogue,
                regionDepthIn * InchesToMetres,
                regionWidthIn * InchesToMetres,
                paddingIn * InchesToMetres,
                out pageCount);
        }

        private List<PartDefinition> LoadCatalogue()
        {
            var loaded = Resources.LoadAll<PartDefinition>(resourcePath);
            if (loaded != null && loaded.Length > 0)
            {
                return new List<PartDefinition>(loaded);
            }

            return new List<PartDefinition>(explicitCatalogue);
        }

        public void ChangePage(int delta)
        {
            if (pageCount <= 1)
            {
                return;
            }

            // Wraps, so paging never dead-ends on a disabled arrow.
            int next = ((currentPage + delta) % pageCount + pageCount) % pageCount;
            ShowPage(next);
        }

        public void ShowPage(int page)
        {
            currentPage = Mathf.Clamp(page, 0, Mathf.Max(0, pageCount - 1));

            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null)
                {
                    Destroy(spawned[i]);
                }
            }
            spawned.Clear();

            if (placements == null)
            {
                return;
            }

            foreach (ShelfPlacement placement in placements)
            {
                if (placement.Page == currentPage)
                {
                    spawned.Add(CreateItem(placement));
                }
            }

            UpdateLabel();
        }

        private GameObject CreateItem(ShelfPlacement placement)
        {
            PartDefinition definition = placement.Definition;

            var go = new GameObject($"Shelf_{definition.partId}");
            go.transform.SetParent(transform, false);
            go.transform.localRotation = placement.Rotation;

            go.AddComponent<MeshFilter>().sharedMesh = definition.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = partMaterial != null
                ? partMaterial
                : PartFactory.GetSharedMaterial(definition);

            // Position from rendered bounds, not the mesh origin: an imported
            // CAD mesh has its origin wherever the modeller left it.
            Vector3 corner = RegionCornerLocal();
            go.transform.localPosition = corner + placement.LocalPosition;

            Bounds worldBounds = renderer.bounds;
            float lift = transform.position.y - worldBounds.min.y;
            go.transform.position += new Vector3(0f, lift, 0f);

            // A box rather than the mesh: cheaper, and crucially it gives tiny
            // screws a hit target big enough to actually click.
            var box = go.AddComponent<BoxCollider>();
            Bounds local = definition.mesh.bounds;
            box.center = local.center;
            float minimum = minimumHitSizeIn * InchesToMetres;
            box.size = new Vector3(
                Mathf.Max(local.size.x, minimum),
                Mathf.Max(local.size.y, minimum),
                Mathf.Max(local.size.z, minimum));

            go.AddComponent<Highlightable>();
            go.AddComponent<ShelfItem>().Configure(definition);

            return go;
        }

        /// <summary>
        /// Near-left corner of the region in local space. The shelf transform
        /// marks the region's centre, which is easier to position in the
        /// scene than a corner.
        /// </summary>
        private Vector3 RegionCornerLocal()
        {
            return new Vector3(
                -regionWidthIn * InchesToMetres * 0.5f,
                0f,
                -regionDepthIn * InchesToMetres * 0.5f);
        }

        // ------------------------------------------------------------------
        // Page indicator
        // ------------------------------------------------------------------

        public void AttachLabel(TextMeshPro label)
        {
            pageLabel = label;
            UpdateLabel();
        }

        private void UpdateLabel()
        {
            if (pageLabel != null)
            {
                // Just "2/5". The word "Page" is obvious from the arrows either
                // side of it and only makes the label bigger.
                pageLabel.text = $"{currentPage + 1}/{Mathf.Max(1, pageCount)}";
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(
                new Vector3(0f, 0.01f, 0f),
                new Vector3(regionWidthIn * InchesToMetres, 0.02f, regionDepthIn * InchesToMetres));
        }
    }
}
