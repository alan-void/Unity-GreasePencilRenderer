# Grease Pencil Renderer

Blender-style Grease Pencil stroke rendering and GPU line art for URP (Unity 6000.6+).

## Install

Add to `Packages/manifest.json`:

```json
"com.alanvoid.greasepencil": "file:E:/UnityProjects/GreasePencilUnityRenderer/Packages/com.alanvoid.greasepencil"
```

or, from git:

```json
"com.alanvoid.greasepencil": "https://github.com/alanaman/Unity-GreasePencilRenderer.git?path=Packages/com.alanvoid.greasepencil"
```

## Contents

| Namespace | What |
|---|---|
| `GreasePencil` | `GreasePencilSO` data asset, `GreasePencilRenderer` |
| `Lineart.Silhouette` | `SilhouetteRenderer`, smooth/sharp silhouette edge calculators |
| `Lineart.EdgeGraph` | `EdgeSilhouetteCalculator` (edge-graph based silhouettes) |
| `Lineart.Pipeline` | Shared compute drivers (chain ranking, dense stroke building) |
| `Lineart.Stylize` | `StrokeStylizer` post-process (taper, noise, ...) |
| `NPR` | `NprLightRig`, `NprToon` shader |
| `CameraControls` | `BlenderViewportCamera` (Input System) |

Compute shaders are loaded from `Resources/GreasePencilRenderer/...`; shader includes use
`Packages/com.alanvoid.greasepencil/...` paths, so the package name must not change.

## Materials

| Material | Use |
|---|---|
| `Lineart/Pencil.mat` | Textured pencil look (scatter, random pixel drop). |
| `GreasePencil/GreasePencilSolid.mat` | Solid alternative to `Pencil.mat`: flat colour, round caps, tapers from the stroke geometry, no texture. Generic, for any `SilhouetteRenderer`. |
