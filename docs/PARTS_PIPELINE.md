# Parts Pipeline

How a VEX part gets from vexrobotics.com into the app at the correct size.

---

## The problem

**Unity cannot import STEP files.** Not with a setting, not with a package you
have. STEP is a boundary representation — exact mathematical surfaces, arcs and
NURBS. Unity renders triangles. Something has to tessellate the surfaces into a
mesh first, and that something is FreeCAD.

## The unit chain

This is the part that has to be right, because a scale error is invisible until
someone measures a robot.

```
VEX STEP file        declares its own unit  (inches, for VEX parts)
  -> FreeCAD         normalises to millimetres on read
  -> OBJ             always millimetres
  -> Unity           x 0.001  ->  metres
  -> world space     1 unit = 1 metre,  1 inch = 0.0254 units
```

Because the OBJ leg is *always* millimetres regardless of what the source file
declared, Unity's scale factor is the same constant for every part forever.
That constant is applied automatically — see below.

### Why world space is metric when the parts are imperial

Authoring and display are in inches. World space is metres. Both, deliberately.

Unity's physics solver and every OpenXR runtime assume 1 unit = 1 metre. A
project built at 1 unit = 1 inch has hands roughly forty times the wrong size
the moment a headset is attached, and there is no clean fix short of rescaling
the entire project. Inches live in the UI and in the source constants; metres
live in the transform.

---

## Converting a part

```bash
STEP_IN="PartSources/my-part.step" OBJ_OUT="Assets/Parts/my-part.obj" "C:/Program Files/FreeCAD 1.0/bin/freecadcmd.exe" tools/step_to_obj.py
```

Paths go through the environment because `freecadcmd` treats trailing
command-line arguments as documents to open — it will try to open the
not-yet-existing output file and fail.

The script prints the bounding box in both millimetres and inches. **Read it.**
VEX dimensions are near-exact multiples of the 0.5" hole pitch, so a wrong
scale shows up instantly as a number that makes no sense.

Verified example — the 35-hole C-channel:

```
X  444.500 mm  =  17.5000 in     35 holes x 0.5" pitch
Y   25.400 mm  =   1.0000 in
Z   13.970 mm  =   0.5500 in
```

### Tessellation quality

`DEFLECTION_MM` (default `0.05`) sets the maximum distance the triangle mesh
may deviate from the true surface. VEX screw holes are about 5 mm across, so
this has to stay well under a millimetre or the holes become visible polygons.

That default produced **31,422 triangles for one C-channel**. A fifty-part
robot would be around 1.5 million. This is the concrete reason the project
needs an adjustable display-quality setting; note that hole detection and
physics must always run against the full-detail mesh regardless of what is
displayed.

---

## Import settings are automatic

`Assets/Editor/PartImportPostprocessor.cs` forces the correct settings on
anything landing in `Assets/Parts/`. **Do not set these by hand** — the whole
point is that getting it wrong on one part out of two hundred is impossible.

| Setting | Value | Why |
|---|---|---|
| Use File Scale | off | OBJ carries no units; leaving this on makes Unity guess |
| Scale Factor | 0.001 | millimetres to metres |
| Read/Write Enabled | on | required to slice the mesh at runtime |
| Mesh Compression | off | quantisation would move holes located to thousandths of an inch |
| Index Format | UInt32 | parts exceed 65k vertices once holes are tessellated |
| Normals | Calculate, 30° | keeps machined edges crisp instead of rounding them |
| Materials | none | assigned in-app, not from the CAD file |

On import it logs the part's real dimensions in inches. That log line is the
verification step — check it rather than trusting the pipeline silently.

---

## Where files live

| Path | Contents | Notes |
|---|---|---|
| `PartSources/` | Original `.step` files | Outside `Assets/` so Unity ignores them. Git LFS. |
| `Assets/Parts/` | Converted `.obj` meshes | Git LFS. |
| `tools/step_to_obj.py` | The converter | |

Source STEP files are kept because tessellation is lossy and one-way. If a
finer mesh is ever needed, or the deflection default changes, every part can be
regenerated from source. Discarding the STEP would make that impossible.

---

## Checking scale in-app

The cutting mat in the workshop scene carries a **one-inch grid**, generated at
an exact pixel-per-inch ratio. It is not decoration; it is the scene's ruler.

Drop the 17.5" C-channel onto the mat. It must span exactly 17.5 squares. If it
does not, the import scale is wrong, and you have found out in seconds rather
than after building half a robot.
