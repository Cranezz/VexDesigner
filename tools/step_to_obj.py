"""
Convert a CAD STEP file into an OBJ mesh that Unity can import.

Unity cannot read STEP. STEP is a boundary representation - exact
mathematical surfaces - whereas Unity only handles triangle meshes. So every
VEX part has to be tessellated once, up front, and the OBJ is what ships.

Run with FreeCAD's headless interpreter, not plain python. Paths are passed
through the environment, because freecadcmd treats trailing command-line
arguments as documents to open rather than as script parameters - it will try
to open the not-yet-existing output file and fail:

    STEP_IN=in.step OBJ_OUT=out.obj \
      "C:/Program Files/FreeCAD 1.0/bin/freecadcmd.exe" tools/step_to_obj.py

Optional: DEFLECTION_MM overrides tessellation accuracy.

FreeCAD works internally in millimetres regardless of the source file's units,
and its STEP reader applies the file's declared unit for us. So the output OBJ
is always in millimetres, which is why Unity's import scale factor is a
constant 0.001 for every part (see docs/PARTS_PIPELINE.md).

deflection_mm controls tessellation accuracy: the maximum distance the
triangle mesh may deviate from the true surface. Smaller is more accurate and
heavier. VEX screw holes are about 5 mm across, so this needs to stay well
under a millimetre or the holes turn into visible polygons.
"""

import os
import sys

import Part
import MeshPart

MM_PER_INCH = 25.4

# Fine enough that a 5 mm hole stays round, coarse enough to keep triangle
# counts sane across a parts library of this size.
DEFAULT_DEFLECTION_MM = 0.05

# Tessellation tolerance for curved surfaces, in degrees.
ANGULAR_DEFLECTION_DEG = 0.35


def describe(label, mm):
    return "{:<8} {:9.3f} mm  =  {:8.4f} in".format(label, mm, mm / MM_PER_INCH)


def convert(src, dst, deflection):
    if not os.path.isfile(src):
        raise SystemExit("Source file not found: {}".format(src))

    print("Reading  : {}".format(src))
    shape = Part.Shape()
    shape.read(src)

    bb = shape.BoundBox
    print("")
    print("Bounding box as read (FreeCAD normalises to mm):")
    print("  " + describe("X", bb.XLength))
    print("  " + describe("Y", bb.YLength))
    print("  " + describe("Z", bb.ZLength))
    print("")

    print("Tessellating at {} mm linear deflection...".format(deflection))
    mesh = MeshPart.meshFromShape(
        Shape=shape,
        LinearDeflection=deflection,
        AngularDeflection=ANGULAR_DEFLECTION_DEG,
        Relative=False,
    )

    out_dir = os.path.dirname(dst)
    if out_dir and not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    mesh.write(dst)

    print("")
    print("Wrote    : {}".format(dst))
    print("Triangles: {}".format(mesh.CountFacets))
    print("Vertices : {}".format(mesh.CountPoints))
    print("")
    print("Unity import settings for this file:")
    print("  Scale Factor : 0.001    (mm -> metres; 1 Unity unit = 1 metre)")
    print("  Read/Write   : enabled  (required to slice the mesh at runtime)")


def main():
    src = os.environ.get("STEP_IN")
    dst = os.environ.get("OBJ_OUT")

    if not src or not dst:
        raise SystemExit(
            "Set STEP_IN and OBJ_OUT environment variables. See module docstring."
        )

    deflection = float(os.environ.get("DEFLECTION_MM", DEFAULT_DEFLECTION_MM))
    convert(src, dst, deflection)


# Called unconditionally, with no __name__ == "__main__" guard. FreeCAD execs
# scripts in a context where __name__ is not "__main__", so the usual guard
# would silently skip everything and exit 0 with no output.
main()
