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

### 3a. Hole types

Every hole carries a type, and it decides what happens when a screw reaches
it:

- **Normal** — a plain opening. Says where something fits; grips nothing.
  Every hole in a C-channel or a plate.
- **Threaded** — bites on the thread. A screw reaching one clamps everything
  between its head and that hole into a single assembly. Nuts are the obvious
  case; threaded standoffs will be the next.
- **Clamp** — grips the shaft rather than the thread (shaft collars, clamps).
  Reserved; currently behaves as Normal.

The type lives on the *hole*, not on the part. That is what lets a nut and a
threaded standoff behave identically without either knowing the other exists,
and it is the same three-way split the original Protobot used.

---

## 3b. Fastening

Mating two holes only puts parts against each other. Nothing is held together
until a screw runs through them and finds something that grips — which is how
a real robot works, and why a screw through four plates with nothing on the
end is just a loose screw.

A screw knows three things about itself, all baked at import:

- the direction of its shank, from the head toward the tip;
- the point on the underside of its head, which is what lands on the metal;
- its catalogue length under the head, which is how much material it can cross.

The first two are measured off the mesh rather than declared, so they cannot
drift from the model. The head height falls out as whatever the mesh is longer
than the catalogue says — 0.087 in on every VEX star drive screw, which is the
same constant the original hard-coded.

From a screw's pose, everything else is derived rather than stored:

- **What it passes through** — every hole whose axis is parallel to the shank
  and whose two openings both sit on it, sorted by distance from under the
  head. Distances are measured from the head because that is the natural zero:
  it is where the screw meets the first piece of metal, and the figure the
  catalogue length is quoted against.
- **Where a nut can go** — the gaps between material along the shank. A nut
  seats at the *top* of a gap, tightened up against whatever is above it,
  because a nut floating in mid-shank holds nothing. The last gap is the run
  past the final plate, which is the ordinary end-of-screw position; the
  others are genuine mid-stack clamps, which is a real thing to do with a real
  screw.
- **What is fastened** — everything between the head and the deepest gripping
  point. Nothing grips, nothing is joined.

None of it is cached. The parts a screw holds can be moved, deleted, and
eventually moved by another player, so a stored list would quietly come to
describe a robot that no longer exists. Recomputing is cheap and cannot go
stale.

A nut that will not fit is refused rather than placed. A nut hanging off the
end of a screw looks fastened and is not, which is a worse outcome than being
told to fetch a longer screw.

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

This same schema is what gets sent over the network when multiplayer arrives
(§6) — a save file and a late-joining client's initial state are the same
thing. Keeping one schema for both is deliberate.

---

## 5. Undo/redo

Required from the start because cuts are destructive.

Implement as a **command stack** — each operation is an object that knows how
to apply and revert itself, rather than snapshotting whole meshes (which would
be enormous). Since a cut is a plane (§2), undo is "drop the last plane and
re-slice from the pristine mesh", which is cheap and exact.

Every mutating action goes through the stack. If some actions bypass it, undo
becomes untrustworthy, and an untrustworthy undo is worse than none.

The same discipline is what makes multiplayer possible later: a command that
can be applied and reverted locally is also a command that can be sent to
another client. See §6 — including the warning that *multi-user* undo
semantics are a genuinely hard problem worth deciding before the undo UI is
built.

---

## 6. Multiplayer readiness

Multiplayer is a long-term goal, not a phase-1 feature. It is called out here
because unlike VR — which is mostly an input and rendering concern and can
genuinely be deferred — **multiplayer constrains how state is represented**,
and that is expensive to retrofit.

The encouraging part: this project is unusually well suited to it, and §2, §4
and §5 have already done most of the work by accident.

### The core rule: document state vs presentation state

Split everything into two categories and never confuse them.

| | Document state | Presentation state |
|---|---|---|
| Examples | Part IDs, transforms, cut op lists, joins | Meshes, colliders, renderers, materials, camera |
| Authoritative? | **Yes** | No — always derived |
| Saved? | Yes | Never |
| Networked? | Yes | Never |
| Lives in | Plain C# classes | MonoBehaviours |

Document state is small, serializable, and the single source of truth.
Presentation state is regenerated from it and is disposable.

This one split satisfies three separate requirements at once — the save format
needs it, undo needs it, and networking needs it. That is why it is worth
holding to strictly even before any networking exists.

### Sync operations, never geometry

A cut is a plane: roughly sixteen bytes. A cut *mesh* is megabytes.

Because §2 makes every cut a plane and §5 makes every mutation a command,
the network payload is already defined:

    saved cut op == a plane == one slice == one undo step == one network message

Clients never exchange mesh data. They exchange the operation, and each client
re-derives its own geometry from the pristine imported mesh. Bandwidth is
trivial and latency tolerance is high, because nothing here is twitch-based.

**On floating-point determinism:** re-slicing on different machines can produce
vertex positions that differ in the last few bits. This does not matter, and it
is important to understand why — the *document* is the operation list, not the
mesh. Two clients with an identical operation list are in agreement even if
their vertex buffers differ microscopically. Only diverge-checking should ever
compare document state, never geometry.

### What must be true from the start

These are cheap now and painful later:

1. **Stable IDs, not object references.** Every part gets an ID that means the
   same thing on every machine and across save/load. Unity object references
   survive neither serialization nor the network.
2. **Every mutation goes through a command object.** Already required by §5.
   Commands must be serializable, and must carry enough information to be
   applied by a machine that did not originate them.
3. **Core logic outside MonoBehaviours.** The document model should be plain C#
   with no `UnityEngine` dependency beyond maths types. This keeps it testable,
   serializable, and portable to a headless server later.
4. **No logic that reads live scene state as truth.** Anything that asks the
   scene "where is this part?" instead of asking the document will desync.

### Open design questions (do not need answering yet)

- **Ownership.** Two users cutting the same part at once needs either locking,
  or per-part ownership, or last-write-wins with a visible conflict.
- **Undo semantics.** This is the genuinely hard one. A single global undo
  stack means your undo can revert someone else's work. Per-user undo requires
  operations to be rebaseable — which is a real distributed-systems problem,
  and the reason collaborative editors are hard. Worth deciding *before*
  building the undo UI, since it changes the stack's shape.
- **Late join.** Sending the full document is fine — it is small by design.

### Library choice (deferred)

Netcode for GameObjects (Unity's own), Mirror, FishNet, or Photon. The decision
can wait: this app sends small messages infrequently with no prediction or
rollback requirements, so it is not demanding on any of them. Avoid choosing
early and coupling the document model to a specific library's types.

---

## 7. Deferred (noted, not built)

| Item | Notes |
|---|---|
| Multiplayer | See §6. Not built, but the state model is shaped for it now. |
| VR / OpenXR | Add `com.unity.xr.openxr` + XR Interaction Toolkit. Target Steam Frame. Architecture already input-abstracted (§1). |
| Paint / spray tool | Likely a vertex-colour or decal approach. Note this is document state, not presentation — it has to save and sync. |
| Direct part manipulation | Phase 1 moves the camera only. |
| Angled-cut side selection UI | Short/mid/long-side measurement input. The *maths* should be built into the cut op early even if the UI is not. |
| Multi-part simultaneous cutting | |
| Parts tree / hierarchy panel | Inventor-style. Needs the join graph from §4. |
