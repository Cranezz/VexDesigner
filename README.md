# VexDesigner

A CAD / virtual building app for VEX Robotics, built in Unity. Design robots
using only real VEX parts, cut them to length on a virtual table saw, and
assemble them with real screws and nuts.

Inspired by [Protobot](https://github.com/davegersh/Protobot) and
[Protobot Rebuilt](https://github.com/BreadSoup/Protobot-Rebuilt), but rebuilt
from scratch to fix two architectural limits of the originals:

- **Parts are cut, not spliced.** The originals shipped parts pre-cut into
  fixed-length segments. Here, cutting performs a real geometry operation that
  removes triangles and vertices.
- **Screw holes are found, not placed.** The originals needed a hand-placed
  collider on every hole of every part. Here they are detected procedurally
  from the mesh.

Phase 1 targets PC/desktop. The architecture is kept VR-ready throughout, with
Steam Frame as the eventual target.

---

## Status

**Phase 1, early setup.** A workshop scene with an orbiting camera. No part
handling yet.

## Requirements

- Unity **6000.3.21f1** (Unity 6.3 LTS)
- Git **and Git LFS** — run `git lfs install` before cloning

## Getting started

```bash
git lfs install
git clone https://github.com/Cranezz/VexDesigner.git
```

Then open the folder via Unity Hub → Projects → Add. Full walkthrough,
including the Unity-specific git pitfalls, is in [docs/SETUP.md](docs/SETUP.md).

Open `Assets/Scenes/Workshop.unity` and press Play.

| Control | Action |
|---|---|
| Right mouse drag | Orbit |
| Middle mouse drag | Pan |
| Scroll wheel | Zoom |

## Documentation

- [docs/SETUP.md](docs/SETUP.md) — installation, git/Unity workflow, troubleshooting
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — design decisions and the reasoning behind them

## Roadmap

| Phase | Scope |
|---|---|
| 1 | Workshop scene, camera, part import, hole detection, table saw, undo/redo, JSON save format |
| 2 | Parts tree panel, import quality controls, direct part manipulation |
| 3 | VR (OpenXR / Steam Frame), paint tool |
| 4 | Multiplayer — collaborative building |

VR and multiplayer are both long-term goals, and the architecture is shaped for
them from the start even though neither is built. They are deferred for
different reasons: VR is mostly an input and rendering concern and can be added
late, whereas multiplayer constrains how state is represented and so has to be
respected in every phase. See [ARCHITECTURE.md](docs/ARCHITECTURE.md) §6.
