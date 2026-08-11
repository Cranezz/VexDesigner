# Protobot VR — Architecture Notes

Living document. Records *decisions and their reasons*, so that a future change
is made deliberately rather than by accident. Phase 1 is PC/desktop only, but
nothing here should make VR harder later.

---

## 1. Foundational decisions

### Unity 6.3 LTS (6000.3.x)
Not the 2021.3 the original Protobot used, and not the 6.5 tech stream.
LTS means bug fixes until Dec 2027 and no breaking changes mid-project.

### Universal Render Pipeline (URP)
Chosen over Built-in (legacy) and HDRP. URP is the only one of the three with
good VR support — it does single-pass instanced stereo rendering, which roughly
halves the cost of drawing a VR frame. HDRP's VR support is poor and its cost
profile is wrong for a headset. This decision is expensive to reverse once
materials exist, which is why it is made on day one.

### 1 Unity unit = 1 metre
VEX parts are specified in inches; **store and display inches, but keep world
space in metres.** Conversion happens at import.

This matters more than it looks. Unity's physics solver, gravity default
(-9.81), and every XR/OpenXR runtime all assume 1 unit = 1 metre. If world
space were 1 unit = 1 inch, then in VR your hands would be ~40x the wrong size
relative to the world and there is no clean fix short of rescaling everything.

    1 inch = 0.0254 world units

### Input System (not legacy `Input.GetAxis`)
The brief calls for VR-friendliness from the start. The concrete meaning of
that: **core systems never call the mouse directly.** They read intent from an
abstraction (`ILookInput`, `IGrabInput`, …). Today a mouse fills it; later a VR
controller fills the same interface with no change to the systems above it.

This is the single highest-leverage "don't paint yourself into a corner"
decision in phase 1, and it costs almost nothing now.

---

## 2. Mesh cutting — the important one

The brief specifies real geometry removal via `pb_CSG` "or an equivalent
runtime mesh-boolean solution". Recommendation: **build the saw on planar
slicing, and keep general CSG in reserve.**

### Why

A table saw blade is a **plane**. Every cut the tool can make is a planar cut.
That is a much easier problem than general boolean geometry:

| | Planar slice | General CSG (pb_CSG / CSG.js) |
|---|---|---|
| Algorithm | one signed-distance test per vertex | build + merge BSP trees of both meshes |
| Cost | linear in triangle count | superlinear, and heavy on allocation |
| Failure modes | few; the cap polygon is the only fiddly part | slivers, T-junctions, coplanar-face artifacts |
| Determinism | high — same input, same output | sensitive to floating-point ordering |

`pb_CSG` is a 2016-era port of CSG.js. It works, but it is not maintained, and
BSP-based booleans get fragile exactly where this project lives: dense
imported CAD geometry with many near-coplanar faces (which is precisely what a
VEX part full of holes looks like).

A planar slice still satisfies the brief's actual requirement — it genuinely
deletes triangles and vertices, it does not hide a renderer. That was the point
of the requirement, and slicing meets it more reliably than CSG does.

### The payoff: cuts and the save format become the same thing

The brief's save format stores each cut as *(distance from a zero point, blade
rotation/angle, which face was cut)*. **That is a plane definition.** So:

    saved cut op  ==  a plane  ==  one slice operation

Replaying a save file is just re-applying N planes to the pristine imported
mesh, in order. No special-case rebuild code, and geometry cannot degrade
across save/load cycles because it is always regenerated from the original mesh
rather than from previously-cut output.

### Where general CSG is still needed
Non-planar operations only — drilling a new hole, custom cutouts. Not phase 1.
Keep the cut pipeline behind an interface (`IMeshOperation`) so a CSG-backed
operation can join the same undo stack later.

---

## 3. Hole detection

Goal: find screw holes procedurally instead of hand-placing colliders.

The key exploitable fact: **VEX holes are not arbitrary.** They sit on a
regular pitch (0.5" on standard VEX EDR structural parts — verify against real
part data before hard-coding). So this is not a general "find holes in an
arbitrary mesh" problem, which is hard; it's a lattice-fitting problem, which
is tractable.

Suggested approach, cheapest-first:

1. Compute the part's oriented bounding box to establish its local axes.
2. Generate the candidate lattice of hole positions on the known pitch.
3. Confirm each candidate against the real mesh (a ray cast through the
   expected hole axis that exits cleanly = a hole; blocked = solid).
4. Cache confirmed holes in an asset keyed by part ID, so detection runs
   **once at import, not at runtime.**

Step 4 matters for performance: the brief's quality slider changes *displayed*
mesh density, but hole detection and physics must always run against
full-detail geometry. Caching at import satisfies both — detection sees full
detail, and the runtime only ever reads the cached result.

---

## 4. Save format

JSON, not the original's proprietary `.pbb`.

Per part: part ID, position, rotation, ordered list of cut operations.
Plus: part-to-part joins (which screws/nuts connect which parts).

Rebuild-on-load, never store geometry. See §2 for why this falls out naturally.

**Use Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`), not Unity's built-in
`JsonUtility`.** `JsonUtility` cannot serialize dictionaries, cannot handle
polymorphism, and silently writes `null` rather than erroring on unsupported
types. The save format needs all three things (an ordered list of *differently
shaped* cut ops is polymorphic by nature), so the built-in one will not do.

Version the format from the first write:

```json
{ "formatVersion": 1, "parts": [...], "joins": [...] }
```

Cheap now; the only thing that lets old save files survive a format change.

---

## 5. Undo/redo

Required from the start because cuts are destructive.

Implement as a **command stack** — each operation is an object that knows how
to apply and revert itself, rather than snapshotting whole meshes (which would
be enormous). Since a cut is a plane (§2), undo is "drop the last plane and
re-slice from the pristine mesh", which is cheap and exact.

Every mutating action goes through the stack. If some actions bypass it, undo
becomes untrustworthy, and an untrustworthy undo is worse than none.

---

## 6. Deferred (noted, not built)

| Item | Notes |
|---|---|
| VR / OpenXR | Add `com.unity.xr.openxr` + XR Interaction Toolkit. Target Steam Frame. Architecture already input-abstracted (§1). |
| Paint / spray tool | Likely a vertex-colour or decal approach. |
| Direct part manipulation | Phase 1 moves the camera only. |
| Angled-cut side selection UI | Short/mid/long-side measurement input. The *maths* should be built into the cut op early even if the UI is not. |
| Multi-part simultaneous cutting | |
| Parts tree / hierarchy panel | Inventor-style. Needs the join graph from §4. |
