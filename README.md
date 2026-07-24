# Mt. Tamalpais LiDAR — 3D Point Cloud (ComputeShader) — WinForms

ProEssentials v10 **WinForms .NET 8** — a `Pe3do` 3D scatter chart rendering
~2.5M airborne LiDAR returns of Mt. Tamalpais, every point individually colored
by elevation, GPU-constructed via ComputeShader. Direct3D.

![LiDAR 3D WinForms](docs/winforms-chart-lidar-point-cloud-computeshader-direct3d-proessentials.png)

# WinForms 3D LiDAR Point Cloud — GPU Compute Shader (ProEssentials v10)

A focused, reproducible WinForms demo that renders **2,500,000 real airborne LiDAR returns** as an interactive 3D point cloud, with vertex construction performed on the GPU by a Direct3D **compute shader** and the finished frame presented **directly to the WinForms window's device context (hDC)**.

Clone it, press **F5**, and rotate a real point cloud. No trial signup, no account, no gridded raster stand-in — unstructured airborne LiDAR XYZ, exactly as surveyed.

---

> **Found ProEssentials through this repo?** Use code **GITHUB15_OCT31** at checkout for 15% off your first license.
>
> Thanks for sharing — every share and star helps another engineer find this repo.

---

## What this WinForms LiDAR demo includes

| Feature | Value |
| --- | --- |
| Chart type | `PolyMode = Scatter`, `Method = Points` |
| Control | `Pe3doWin` (3D Scientific Graph, WinForms) |
| Vertex construction | GPU ComputeShader (v10.0.0.24) |
| Point count | 2,500,000 (subsampled from ~22.7M source) |
| Color strategy | `PointColors` per data point — every LiDAR return individually colored |
| Render engine | Direct3D, coupled directly to the window hDC |
| Coordinate convention | LiDAR XYZ → Pe3do (X, Z, Y) — elevation maps to vertical |
| Source data | NCALM 2006 Marin Headlands airborne LiDAR via OpenTopography |
| Data prep | `data/prepare_data.py` — converts any LAZ source into the demo's input |

Two related repos round out the LiDAR series on the same dataset:

- *(coming)* `winforms-3d-lidar-surface-proessentials` — Delaunay-triangulated surface from the same point cloud
- *(coming)* `winforms-2d-lidar-contour-proessentials` — Pesgo top-down contour view

---

## Why WinForms is the *fast* interface (the hDC advantage)

It is widely assumed that WPF is the high-performance target and WinForms is the legacy fallback. For ProEssentials, **the opposite is true** — and a LiDAR point cloud is a good place to see why.

A GPU-rendering chart on **WPF** cannot draw Direct3D straight to the screen. WPF's compositor owns those pixels, so the chart must render its scene **to an off-screen texture**, hand that texture to WPF through a `D3DImage` (or composition interop), and let WPF composite it into the visual tree on the next tick. Every frame pays a **texture-copy plus a compositor-sync** cost that has nothing to do with how fast the chart was actually drawn.

The **WinForms** control has no such tax. `Pe3doWin` is a real `System.Windows.Forms.Control` with a real Win32 window handle and a real device context. Direct3D is coupled **directly to that hDC**, so the compute-shader-constructed frame is presented straight to the window — no render-to-texture, no `D3DImage` hand-off, no second composite.

**Net effect:** across representative datasets the native WinForms interface runs **roughly 5% faster end-to-end** than the *same* ProEssentials engine driving a WPF control. Same compute shader, same zero-copy data path, same on-demand frame model — the only difference is that WinForms skips WPF's compositor. (The C++/MFC and Delphi/VCL interfaces tie to the hDC even more directly, with no managed layer at all.)

---

## How this WinForms LiDAR repo compares to other charting libraries

We looked at every major charting vendor's public GitHub presence for a clone-and-run **WinForms or native** LiDAR / large point-cloud demo:

| Vendor | Public WinForms / native LiDAR demo | Standalone repo | Points rendered |
| --- | --- | --- | --- |
| **ProEssentials (this repo)** | ✅ Yes | ✅ Yes — clone & F5 | **2,500,000 raw airborne returns** |
| SciChart | ✅ Yes (WPF only; no native WinForms control) | ❌ Sub-folder of examples mega-repo | ~250,000 (gridded raster) |
| LightningChart | 📝 Blog tutorial only | ❌ Trial install required | Marketing claims up to 55M; no public repo to verify |
| DevExpress | ❌ None found | ❌ | — |
| Syncfusion | ❌ None found | ❌ | — |
| Telerik | ❌ None found | ❌ | — |

**Notes on the comparison.** SciChart has no native WinForms control at all — its WinForms story is the WPF `SciChartSurface` hosted in a Microsoft `ElementHost`, and its public LiDAR example is a 1km × 1km gridded DEFRA raster (`tq3080_DSM_2M`, 50m elevation range) embedded as a `UserControl` inside its 130+ example monorepo. LightningChart's tutorial references a similar gridded dataset; its public collateral claims much higher numbers but ships no clone-and-run repo to verify them. ProEssentials' dataset here is **unstructured airborne LiDAR returns** from the NCALM 2006 Marin Headlands survey, ranging from sea level to 674m, prepared by the included `data/prepare_data.py` from any LAZ source.

The pitch isn't "ProEssentials is the fastest" — that's a benchmark fight no vendor wins cleanly. It's **"ProEssentials is the only WinForms charting vendor that ships a focused, reproducible LiDAR repo at this scale."** Verifiable, by definition, because you're holding it.

---

## How the GPU ComputeShader rendering path works

### ComputeShader — GPU vertex construction for scatter

Without ComputeShader, the CPU walks each of the 2.5M points sequentially on a single core to build their vertex data. With `ComputeShader = true` the **GPU** does this work — potentially 2,000+ shader cores operating in parallel. This path was added to `PolyMode = Scatter` in v10.0.0.24.

**Measured impact on this dataset:** click-to-first-paint drops from **~3 seconds** (CPU vertex construction) to **essentially instant** (GPU). Same code, same data, same hardware.

Comment the four lines below to take the slow path on purpose — useful for B-roll or before/after comparison shots:

```csharp
// 3D scatter, GPU vertex construction, presented to the WinForms hDC
Pe3do1.PeData.ComputeShader  = true;
Pe3do1.PeData.StagingBufferX = true;
Pe3do1.PeData.StagingBufferY = true;
Pe3do1.PeData.StagingBufferZ = true;
```

### Staging buffers — non-stalling CPU→GPU upload

The staging buffers are **GPU-accessible intermediate memory regions** that allow efficient CPU-to-GPU data transfer without stalling the render pipeline during the upload. The CPU writes your XYZ arrays into the staging regions; the GPU pulls from them to build vertices in parallel. Because the upload doesn't block the render pipeline, the compute shader and the present-to-hDC stay decoupled from the transfer.

### Zero-copy data, per-point color

The chart reads your existing arrays in place (no internal copy, no float-to-double conversion, no object-per-point allocation), and `PointColors` assigns a color to **every individual LiDAR return** — so elevation, intensity, or classification can drive color across all 2.5M points without collapsing them into a handful of series.

---

## Build & run

1. Clone this repository.
2. Open the `.sln` in **Visual Studio 2022**.
3. **Build → Rebuild Solution** (the ProEssentials WinForms NuGet package restores automatically).
4. Press **F5**.
5. Left-drag to rotate, wheel to zoom; toggle the four ComputeShader lines to feel the CPU-vs-GPU difference.

> **Designer note:** the Visual Studio designer requires the full ProEssentials installation, but the project **builds and runs from NuGet alone** — no full install needed for clone-and-F5. Example code is MIT licensed.

---

## Data attribution

Airborne LiDAR: **NCALM 2006 Marin Headlands** survey, distributed by **OpenTopography**. Use of the dataset should follow OpenTopography's citation guidance. `data/prepare_data.py` will regenerate the demo input from any LAZ source if you wish to substitute your own survey.

---

## Related reading

- **WinForms Chart Performance — Native Direct3D hDC Coupling vs GDI+** → https://gigasoft.com/why-proessentials/winforms-chart-performance
- **Platform Coverage — one native engine across WPF, WinForms, C++ MFC, Delphi VCL, ActiveX** → https://gigasoft.com/why-proessentials/platform-coverage
- Companion repos: 100M-point demo · real-time circular-buffer compute-shader demo · real-time 3D surface compute-shader demo (all under https://github.com/GigasoftInc)


## What This Demonstrates
- **GPU ComputeShader scatter** (`PolyMode.Scatter`, v10.0.0.24+): per-point
  vertex construction across thousands of shader cores.
- **Per-point `PointColors`** packed as `peColor32` int layout (0xAABBGGRR),
  bulk-loaded with `FastCopyFrom(int[])`.
- **Binary data load** — `mttam_lidar.bin` (int32 count + float32 X/Y/Z blocks)
  read with `BinaryReader` + `Buffer.BlockCopy`.
- **Code-built UI** — single chart in `MainForm.cs`; no `.Designer.cs` / `.resx`.

## WinForms vs WPF
The binary loader, the elevation colormap, and the entire Pe3do configuration are
identical to the WPF version. Only host pieces changed: WinForms `MessageBox`,
`Application.Exit()`, `System.Drawing.Color`, and `Pe3doWpf` → `Pe3do`.

➡️ WPF version: [wpf-3d-lidar-point-cloud-computeshader-proessentials](https://github.com/GigasoftInc/wpf-3d-lidar-point-cloud-computeshader-proessentials)

## Data File
`mttam_lidar.bin` (~29 MB) is **not** included. Build it with
`python data/prepare_data.py path/to/*.laz` (see `data/README.md`), then rebuild
so it copies next to the executable. The app shows an error dialog and exits if
the file is missing at runtime.

## NuGet
References `ProEssentials.Chart.Net80.x64.Winforms` (>= 10.0.0.28).

## License
Example code is MIT licensed. ProEssentials requires a commercial license. LiDAR
data: OpenTopography / NCALM 2006 Marin collection.
