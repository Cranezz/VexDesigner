# Art Direction

## Target look

**Realistic.** A working home garage: concrete slab, block and drywall walls,
sectional door, timber benches, fluorescent tube lighting.

An earlier draft of this document specified a cel-shaded look, based on a
stylised reference. That was superseded — the app is a measuring tool, and
realism serves it better. A stylised render flattens the visual cues that tell
you a part is seated, aligned, or resting on a surface, and those cues are the
whole job here.

## What actually makes it read as real

In rough order of how much they matter:

1. **Surface relief.** A uniform colour reads as plastic no matter how good the
   lighting is, because real materials are legible almost entirely through how
   light catches their texture. Every surface is therefore built as a **height
   field**, with the albedo *and* the normal map derived from the same field so
   the bumps and the shading agree.
2. **Correct scale.** The garage is a real 20 ft square with a 9 ft ceiling;
   the bench is 36 in high. This matters more now that the player walks around,
   and it will matter more again in VR, where a room built to look right from
   one fixed camera angle feels immediately wrong.
3. **Believable light.** Fluorescent tubes, slightly cool and green-leaning.
   Neutral white reads as studio lighting rather than a garage.
4. **Weak ambient fill.** Enough that the undersides of parts stay readable —
   necessary, not decorative, when the user is lining up screw holes.

## How the surfaces are made

All textures are **generated procedurally** (`SurfaceTextureGenerator`) rather
than downloaded. That keeps the repository self-contained with no licensing
questions, guarantees seamless tiling, and lets the cutting mat's grid be
generated at an exact pixel-per-inch ratio.

| Surface | Notes |
|---|---|
| Concrete | Multi-scale blotching, fine grain, sparse trowel pits, aggregate flecks |
| Drywall | Shallow orange-peel roller texture |
| Cinder block | 16x8 in blocks in running bond with recessed mortar joints |
| Bench wood | Plank grain with warped rings and grooved seams |
| Painted metal | Very shallow — overdoing this makes metal look like hammered tin |
| Pegboard | Quarter-inch holes on one-inch centres, as depth rather than geometry |
| Cutting mat | Exact one-inch grid. Not decoration — see below |

Two implementation notes that are easy to get wrong and hard to diagnose:

- **Normal maps must import as `NormalMap`, not `Default`.** Unity packs them
  differently and treats them as linear data. Imported as a colour texture they
  are gamma-corrected and decoded wrongly, and the resulting lighting error is
  subtle enough to be mistaken for a lighting problem.
- **Importer settings cannot be applied inside a `StartAssetEditing` block**, or
  before a `Refresh`. `AssetImporter.GetAtPath` returns null for a file Unity
  has not yet imported, and the settings are then skipped in silence.

## Tiling

Specified as **inches per texture repeat**, never as a raw repeat count, and
applied per object through a `MaterialPropertyBlock`.

A fixed repeat count would stretch the texture across a 20 ft wall and cram it
on a cabinet door. Per-object property blocks mean one material asset serves
every size, instead of needing dozens of variants differing only in two numbers.

## The cutting mat is a ruler

Its one-inch grid is generated at an exact pixel-per-inch ratio, and it is not
decoration. Drop the 17.5 in C-channel on it and it must span 17.5 squares. If
it does not, the import scale is wrong, and you find out in seconds rather than
after building half a robot.

## Sequencing

The room is greybox-quality geometry with real materials — simple shapes,
correct sizes, believable surfaces. That is deliberate: shading carries far more
of the realism than modelling detail does, so it is worth getting right first.

Detailed props can be added later without disturbing anything, since they land
into a lighting and material setup that already works.

## Asset sources

- **VEX parts: official CAD only.** Never AI-generated, never hand-approximated.
  Hole detection and mesh cutting both depend on real dimensions and clean
  manifold geometry. See ARCHITECTURE.md sections 2 and 3.
- **Environment:** procedural, or CC0/CC-licensed packs if real props are wanted
  later.
