# Art Direction

## Target look

Cel-shaded / "inked illustration" garage workshop. Reference: a home garage
with a central work table, pegboard tool wall, side workbenches, fluorescent
strip lights, and a roller door.

Defining characteristics, in order of how much they matter:

1. **Black outlines.** Every silhouette and major crease is inked. This is the
   single strongest signal of the style.
2. **Flat or banded shading.** Lighting steps between a small number of values
   rather than blending smoothly. No specular highlights to speak of.
3. **Tight warm palette.** Roughly eight colours: warm tan wood, desaturated
   blue-grey metal, off-white walls, near-black accents.
4. **Soft baked shadows.** Large, soft contact shadows rather than sharp
   realtime ones.

## The important note about this style

**The look lives in the shading, not the geometry.** Everything in the
reference is boxes — table, benches, shelves, door panels, wall. There is
almost no complex modelling in it.

The practical consequence: getting the render pipeline right is worth far more
than modelling effort. Simple geometry with correct toon shading and outlines
reads as the reference. Carefully modelled geometry with default shading does
not.

So: **build the shading first, then the props.** Props built against a correct
shading setup can be crude and still look deliberate.

## Implementation approach

| Element | Approach |
|---|---|
| Outlines | Full-screen edge detection on depth + normals, via URP's Full Screen Pass Renderer Feature. Catches interior creases, not just silhouettes — which the cheaper inverted-hull method misses. |
| Toon shading | Shader Graph with a stepped lighting ramp. Two or three bands is plenty. |
| Shadows | Bake to lightmaps where possible. The environment is static, so there is no reason to pay realtime cost for it. |
| Props | ProBuilder in-editor. This is box modelling; it does not need Blender. |

## Asset sources

- **VEX parts: official CAD only.** VEX publishes models per product; GrabCAD
  also hosts them. Never AI-generated, and never hand-approximated — hole
  detection and mesh cutting both depend on real dimensions and clean manifold
  geometry. See ARCHITECTURE.md §2 and §3.
- **Environment props:** ProBuilder, or CC0/CC-licensed packs (Kenney.nl, Poly
  Pizza, Sketchfab, Unity Asset Store). AI generation (Meshy, Tripo, Rodin) is
  acceptable here but rarely better than an existing pack, and its topology is
  poor.

## Sequencing

Environment art is **deferred**. It touches none of the phase-1 goals, and it
is the cheapest thing in the project to change later. Greybox stays until the
core mechanics work, because the mechanics will impose requirements that cannot
be predicted yet — sightlines to the saw, contrast between parts and the work
surface, legibility of the parts drawers.

The exception is the shading setup, which is worth doing early: it is a
pipeline decision, it makes greybox look intentional, and it means later art
lands into a style that already works.
