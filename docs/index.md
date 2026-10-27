---
layout: default
title: Grease Pencil Renderer for Unity
---

A work-in-progress NPR (non-photorealistic rendering) toolkit for Unity's Universal Render Pipeline.
It renders Blender Grease Pencil data and generates Grease Pencil style line art from 3D meshes in real
time, with the whole stroke pipeline running in compute shaders.

## Features

- **Grease Pencil rendering**: `GreasePencilRenderer` draws `GreasePencilSO` assets (layers, frames, strokes,
  materials) with Blender's Grease Pencil vertex/fragment logic ported to HLSL, including frame-by-frame playback.
- **Real-time silhouettes**: edge calculators find contour edges on the GPU and chain them into strokes:
  - `SilhouetteEdgeCalculator`: smooth (interpolated normal) contours across triangles.
  - `SharpSilhouetteEdgeCalculator`: flat-shaded silhouettes along split-normal edges.
  - `EdgeSilhouetteCalculator`: edge-graph silhouettes with concave-edge skipping, silhouette falloff and branch joining.
- **Stroke stylization**: `StrokeStylizer` mirrors Blender's line art outline stack: radius from weights and
  noise variation, screen-space start/end taper profiles (`StrokeTaperProfile` assets), push/offset along the
  normal, and spike thinning at sharp corners.
- **NPR shading**: an `NPR/Toon` shader with `NprLightRig` key and rim lights.
- **Blender viewport camera**: `BlenderViewportCamera` orbit/pan/zoom and numpad views (Input System).

## Requirements

- Unity 6000.6 or newer
- Universal Render Pipeline 17.6
- Input System 1.20
- A GPU with compute shader support

## Installation

The renderer ships as a UPM package: `com.alanvoid.greasepencil`. Add one of these to your project's
`Packages/manifest.json`.

From git:

```json
"com.alanvoid.greasepencil": "https://github.com/alan-void/Unity-GreasePencilRenderer.git?path=Packages/com.alanvoid.greasepencil"
```

From a local clone (edits are picked up live):

```json
"com.alanvoid.greasepencil": "file:<path-to-clone>/Packages/com.alanvoid.greasepencil"
```

## Quick start

### Silhouette line art on a mesh

1. Select a GameObject with a `MeshFilter` and `MeshRenderer`.
2. Add an edge calculator (`SilhouetteEdgeCalculator`, `SharpSilhouetteEdgeCalculator` or `EdgeSilhouetteCalculator`)
   and assign its **View Camera**.
3. Add a `SilhouetteRenderer`, assign a material using the `Lineart/GreasePencilBlended` or `Lineart/Pencil` shader,
   and add at least one entry to **Grease Pencil Materials**.
4. Optional: add a `StrokeStylizer` on the same GameObject for radius, taper, push and corner thinning.

### Playing back Grease Pencil data

1. Create a `GreasePencilData` asset from an exported Grease Pencil JSON file
   (right-click the JSON → **Create → Grease Pencil Data from JSON**).
2. Add a `GreasePencilRenderer` to a GameObject, assign the asset and a `Custom/GreasePencil` material.
3. Use **Frame Idx** to pick the frame to show.

## Package layout

| Namespace | Contents |
|---|---|
| `GreasePencil` | `GreasePencilSO` data asset, `GreasePencilRenderer` |
| `Lineart.Silhouette` | `SilhouetteRenderer`, smooth and sharp silhouette edge calculators |
| `Lineart.EdgeGraph` | `EdgeSilhouetteCalculator` |
| `Lineart.Pipeline` | Shared compute drivers: chain ranking, dense stroke building |
| `Lineart.Stylize` | `StrokeStylizer`, `StrokeTaperProfile` |
| `NPR` | `NprLightRig`, `NPR/Toon` shader |
| `CameraControls` | `BlenderViewportCamera` |

## Demo

The repository's host project includes `SampleScene` and a **SpikePod** demo scene
(`Assets/SpikePod/Scenes/SpikePod.unity`) showing toon shading with stylized silhouette outlines.

## Source

[github.com/alan-void/Unity-GreasePencilRenderer](https://github.com/alan-void/Unity-GreasePencilRenderer)
