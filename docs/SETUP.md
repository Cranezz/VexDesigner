# Setup

Everything needed to go from a fresh machine to a running project.

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Unity Hub | any current | Manages editor installs |
| Unity Editor | **6000.3.21f1** (Unity 6.3 LTS) | Exact version matters — see below |
| Git | 2.x | |
| Git LFS | 3.x | Required *before* cloning, or meshes arrive as text stubs |
| IDE | Visual Studio 2022, VS Code, or Rider | Rider is the nicest for Unity; VS Community is free and bundled with the Hub |

### Why the exact editor version matters
Unity writes its version into `ProjectSettings/ProjectVersion.txt`. Opening the
project with a *newer* editor silently upgrades it and rewrites asset files —
which is a one-way trip and will show up as a huge confusing diff. Opening with
an older one may fail outright. Match the version.

---

## First-time clone

```bash
git lfs install
git clone https://github.com/Cranezz/VexDesigner.git
cd VexDesigner
git lfs pull
```

If `git lfs install` is skipped, mesh files clone as small text pointer files
and Unity will fail to import them with a confusing error. If that happens, run
`git lfs install && git lfs pull` and it resolves.

---

## Opening the project

1. Unity Hub → **Projects** → **Add** → **Add project from disk**.
2. Select the repository root (the folder containing `Assets/`).
3. Confirm the editor version dropdown shows **6000.3.21f1**.
4. Open. The first import takes several minutes — Unity is building the
   `Library/` folder from scratch. This is normal and happens only once.

`Library/` is generated and git-ignored. **Never commit it.** If the project
ever gets into a strange state, deleting `Library/` and reopening is the
standard fix and loses nothing.

---

## Unity + Git: two things that will bite you

### 1. Turn on visible meta files and text serialization
These should already be set in `ProjectSettings/`, but verify after any editor
upgrade:

- **Edit → Project Settings → Editor → Version Control → Mode: `Visible Meta Files`**
- **Edit → Project Settings → Editor → Asset Serialization → Mode: `Force Text`**

`Force Text` makes scenes and prefabs YAML instead of binary. Without it,
scene files are unreadable blobs that can never be merged or reviewed.

Every asset has a companion `.meta` file holding its GUID — the ID that all
references use. **Always commit the `.meta` alongside its asset.** Committing
one without the other is the usual cause of "all my references broke".

### 2. Set up Unity's merge tool for scenes
Scene and prefab YAML does not merge with normal git. Unity ships a purpose-built
merge tool for it. One-time setup:

```bash
git config merge.tool unityyamlmerge
git config mergetool.unityyamlmerge.cmd '"C:/Program Files/Unity/Hub/Editor/6000.3.21f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
git config mergetool.unityyamlmerge.trustExitCode false
```

Better still: **avoid the problem.** Two people editing the same scene at once
will conflict no matter what tooling exists. Prefer putting work in prefabs
(which are smaller and conflict less) and keep scenes thin.

---

## Packages

Managed in `Packages/manifest.json`, which **is** committed — that file is the
lockfile for the project's dependencies. Editing it by hand is fine and often
faster than the Package Manager UI.

| Package | Purpose |
|---|---|
| `com.unity.render-pipelines.universal` | URP — see ARCHITECTURE.md §1 |
| `com.unity.inputsystem` | Input abstraction; VR-ready |
| `com.unity.nuget.newtonsoft-json` | Save format — see ARCHITECTURE.md §4 |
| `com.unity.probuilder` | Greyboxing the workshop without external modelling |
| `com.unity.test-framework` | Cut/hole-detection maths is very worth testing |

Deferred until the VR phase: `com.unity.xr.management`,
`com.unity.xr.openxr`, `com.unity.xr.interaction.toolkit`.

Not from the Package Manager: `pb_CSG` (github.com/karl-/pb_CSG) is a source
drop-in, and is only needed if/when non-planar boolean operations are required.
See ARCHITECTURE.md §2 — the saw does not need it.

---

## Troubleshooting

**"The project is using an unsupported Unity version"** — editor version
mismatch. Install 6000.3.21f1 in the Hub and open with that.

**Meshes import as empty / errors about pointer files** — LFS wasn't
initialised before cloning. `git lfs install && git lfs pull`.

**All script references show as `Missing (Mono Script)`** — usually a `.meta`
file that didn't get committed, or a compile error blocking the whole assembly.
Check the Console for compile errors first; one broken script stops everything.

**Editor is extremely slow / random import failures** — check that no file
sync service (OneDrive, Dropbox, Google Drive) is syncing `Library/`. It is
thousands of files rewritten constantly and sync tools cannot keep up.
