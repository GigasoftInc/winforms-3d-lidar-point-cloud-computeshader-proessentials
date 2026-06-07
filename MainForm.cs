using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Gigasoft.ProEssentials;
using Gigasoft.ProEssentials.Enums;

namespace MtTamalpaisLidar3D
{
    /// <summary>
    /// ProEssentials WinForms 3D LiDAR Scatter — ComputeShader + per-point PointColors  (.NET 8)
    ///
    /// Code-built WinForms port. Renders a real airborne LiDAR scan of Mt.
    /// Tamalpais (~2.5M ground returns) as a Pe3do scatter chart with every
    /// point individually colored by elevation, GPU-constructed via ComputeShader.
    ///
    /// Data file: mttam_lidar.bin — flat binary produced by data/prepare_data.py:
    ///   int32 nPoints, then float32*nPoints X, then Y, then Z.
    ///
    /// PORT NOTES (only the WPF host pieces changed; the binary loader, the
    /// elevation colormap, and the entire Pe3do configuration are IDENTICAL to
    /// the WPF code-behind):
    ///   - MessageBox / MessageBoxButton.OK / MessageBoxImage.Warning
    ///       -> WinForms MessageBox / MessageBoxButtons.OK / MessageBoxIcon.Warning
    ///   - Application.Current.Shutdown() -> Application.Exit()
    ///   - System.Windows.Media.Color     -> System.Drawing.Color
    ///   - PesgoWpf/Pe3doWpf control       -> Pe3do
    ///   AppDomain.CurrentDomain.BaseDirectory is framework-agnostic (unchanged).
    /// </summary>
    public class MainForm : Form
    {
        private Pe3do Pe3do1;

        public MainForm()
        {
            Pe3do1 = new Pe3do();
            Pe3do1.Dock = DockStyle.Fill;
            Controls.Add(Pe3do1);

            Text = "ProEssentials — Mt. Tamalpais LiDAR — 3D Scatter ComputeShader";
            ClientSize = new Size(1600, 900);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;

            this.Load += MainForm_Load;
        }

        // -------------------------------------------------------------------
        // MainForm_Load — chart initialization (WinForms Form.Load)
        // -------------------------------------------------------------------
        private void MainForm_Load(object sender, EventArgs e)
        {
            // Step 1 — Load LiDAR binary (pure .NET — IDENTICAL to WPF)
            string filepath = AppDomain.CurrentDomain.BaseDirectory + "mttam_lidar.bin";

            if (!File.Exists(filepath))
            {
                MessageBox.Show(
                    "mttam_lidar.bin not found.\n\n" +
                    "Build the dataset by running:\n" +
                    "    python data\\prepare_data.py path\\to\\*.laz\n\n" +
                    "See data/README.md for where to download LAZ tiles.\n\n" +
                    "Then rebuild so the file copies to the output directory.",
                    "Data file missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Application.Exit();
                return;
            }

            int nPoints;
            float[] lidarX, lidarY, lidarZ;
            using (var fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                nPoints = br.ReadInt32();
                int byteCount = nPoints * sizeof(float);

                lidarX = new float[nPoints];
                lidarY = new float[nPoints];
                lidarZ = new float[nPoints];
                Buffer.BlockCopy(br.ReadBytes(byteCount), 0, lidarX, 0, byteCount);
                Buffer.BlockCopy(br.ReadBytes(byteCount), 0, lidarY, 0, byteCount);
                Buffer.BlockCopy(br.ReadBytes(byteCount), 0, lidarZ, 0, byteCount);
            }

            float zMin = float.MaxValue, zMax = float.MinValue;
            for (int i = 0; i < nPoints; i++)
            {
                if (lidarZ[i] < zMin) zMin = lidarZ[i];
                if (lidarZ[i] > zMax) zMax = lidarZ[i];
            }

            // Step 2 — Build Pe3do XYZ arrays with the coordinate swap
            float[] peX = new float[nPoints];
            float[] peY = new float[nPoints];
            float[] peZ = new float[nPoints];
            for (int i = 0; i < nPoints; i++)
            {
                peX[i] = lidarX[i];   // east-west
                peY[i] = lidarZ[i];   // elevation -> Pe3do vertical axis
                peZ[i] = lidarY[i];   // north-south (depth)
            }

            // Step 3 — Build per-point colors (peColor32 int layout: 0xAABBGGRR)
            int[] packedColors = new int[nPoints];
            float zRange = Math.Max(1f, zMax - zMin);
            for (int i = 0; i < nPoints; i++)
            {
                float t = (lidarZ[i] - zMin) / zRange;
                var (r, g, b) = ElevationColorBytes(t);
                packedColors[i] = (255 << 24) | (b << 16) | (g << 8) | r;
            }

            // Step 4 — Configure Pe3do
            Pe3do1.PeFunction.Reset();
            Pe3do1.PeConfigure.RenderEngine = RenderEngine.Direct3D;

            Pe3do1.PePlot.PolyMode = PolyMode.Scatter;
            Pe3do1.PePlot.Method = ThreeDGraphPlottingMethod.Zero;   // 0 = Points

            Pe3do1.PeData.Subsets = 1;
            Pe3do1.PeData.Points = nPoints;

            Pe3do1.PeData.X[0, nPoints - 1] = 0f;
            Pe3do1.PeData.Y[0, nPoints - 1] = 0f;
            Pe3do1.PeData.Z[0, nPoints - 1] = 0f;

            Pe3do1.PeData.X.FastCopyFrom(peX, nPoints);
            Pe3do1.PeData.Y.FastCopyFrom(peY, nPoints);
            Pe3do1.PeData.Z.FastCopyFrom(peZ, nPoints);

            Pe3do1.PePlot.PointColors.FastCopyFrom(packedColors);

            {
                var (r, g, b) = ElevationColorBytes(0.5f);
                Pe3do1.PeColor.SubsetColors[0] = Color.FromArgb(255, r, g, b);
            }

            Pe3do1.PePlot.SubsetPointTypes[0] = PointType.DotSolid;
            Pe3do1.PePlot.PointSize = PointSize.Small;

            // Step 5 — ComputeShader (GPU scatter path)
            Pe3do1.PeData.ComputeShader = true;
            Pe3do1.PeData.StagingBufferX = true;
            Pe3do1.PeData.StagingBufferY = true;
            Pe3do1.PeData.StagingBufferZ = true;

            // Step 6 — Manual scales (skip per-point ranging)
            float padXZ = 50f;
            float padY  = 30f;

            float xMin = Min(peX), xMax = Max(peX);
            float zMinChart = Min(peZ), zMaxChart = Max(peZ);

            Pe3do1.PeGrid.Configure.ManualScaleControlX = ManualScaleControl.MinMax;
            Pe3do1.PeGrid.Configure.ManualMinX = xMin - padXZ;
            Pe3do1.PeGrid.Configure.ManualMaxX = xMax + padXZ;

            Pe3do1.PeGrid.Configure.ManualScaleControlY = ManualScaleControl.MinMax;
            Pe3do1.PeGrid.Configure.ManualMinY = zMin - 5f;            // elevation
            Pe3do1.PeGrid.Configure.ManualMaxY = zMax + padY;

            Pe3do1.PeGrid.Configure.ManualScaleControlZ = ManualScaleControl.MinMax;
            Pe3do1.PeGrid.Configure.ManualMinZ = zMinChart - padXZ;
            Pe3do1.PeGrid.Configure.ManualMaxZ = zMaxChart + padXZ;

            Pe3do1.PeData.SkipRanging = true;

            // Step 7 — Camera / view defaults (hero camera)
            Pe3do1.PeUserInterface.Scrollbar.ViewingHeight = 19;
            Pe3do1.PeUserInterface.Scrollbar.DegreeOfRotation = 200;
            Pe3do1.PePlot.Option.DxFOV = 1;
            Pe3do1.PePlot.Option.DxZoom = 0.49F;
            Pe3do1.PePlot.Option.DxViewportX = 0.00F;
            Pe3do1.PePlot.Option.DxViewportY = 0.33F;
            Pe3do1.PePlot.Option.DxZoomMax = 20F;
            Pe3do1.PePlot.Option.DxZoomMin = -16F;
            Pe3do1.PePlot.Option.DxFitControlShape = false;
            Pe3do1.PePlot.Option.DxViewportPanFactor = 1.5F;

            Pe3do1.PeUserInterface.Scrollbar.ScrollSmoothness = 3;
            Pe3do1.PeUserInterface.Scrollbar.MouseWheelZoomSmoothness = 4;
            Pe3do1.PeUserInterface.Scrollbar.PinchZoomSmoothness = 2;
            Pe3do1.PeUserInterface.Scrollbar.MouseWheelZoomFactor = 1.8F;
            Pe3do1.PeUserInterface.Scrollbar.MouseDraggingX = true;
            Pe3do1.PeUserInterface.Scrollbar.MouseDraggingY = true;

            Pe3do1.PePlot.Option.DegreePrompting = true;

            Pe3do1.PeFunction.SetLight(0, 4.06F, -5.42F, 8.13F);
            Pe3do1.PePlot.Option.LightStrength = 0.65F;
            Pe3do1.PePlot.Option.BackLight = 10;

            // Step 8 — Visual styling
            Pe3do1.PeColor.QuickStyle = QuickStyle.DarkNoBorder;
            Pe3do1.PeColor.BitmapGradientMode = false;

            Pe3do1.PeString.MainTitle =
                $"Mt. Tamalpais LiDAR — {nPoints:N0} points, GPU ComputeShader scatter";
            Pe3do1.PeString.SubTitle = "ProEssentials v10.0.0.24+ — comment the ComputeShader lines for the slow-path comparison";
            Pe3do1.PeString.XAxisLabel = "East (m)";
            Pe3do1.PeString.YAxisLabel = "Elevation (m)";
            Pe3do1.PeString.ZAxisLabel = "North (m)";

            Pe3do1.PeFont.Fixed = true;
            Pe3do1.PeFont.FontSize = Gigasoft.ProEssentials.Enums.FontSize.Medium;
            Pe3do1.PeFont.Label.Bold = true;
            Pe3do1.PeConfigure.TextShadows = TextShadows.BoldText;

            Pe3do1.PeGrid.Option.GridAspectX = 1.0F;
            Pe3do1.PeGrid.Option.GridAspectY = 1.0F;
            Pe3do1.PeGrid.Option.GridAspectZ = 1.0F;

            Pe3do1.PeLegend.Show = false;
            Pe3do1.PeUserInterface.Menu.LegendLocation = MenuControl.Show;

            Pe3do1.PeConfigure.ImageAdjustLeft = 50;
            Pe3do1.PeConfigure.ImageAdjustRight = 50;
            Pe3do1.PeConfigure.ImageAdjustTop = 30;
            Pe3do1.PeConfigure.ImageAdjustBottom = 30;

            Pe3do1.PeConfigure.PrepareImages = true;
            Pe3do1.PeConfigure.CacheBmp = true;
            Pe3do1.PeConfigure.AntiAliasGraphics = true;
            Pe3do1.PeConfigure.AntiAliasText = true;

            Pe3do1.PeUserInterface.Allow.FocalRect = false;

            // Cursor / hotspot tracking disabled at 2.5M points (see WPF notes).
            // Below ~500K points, uncomment for live tooltips.
            // Pe3do1.PeUserInterface.HotSpot.Data = true;
            // Pe3do1.PeUserInterface.Cursor.PromptTracking = true;
            // Pe3do1.PeUserInterface.Cursor.PromptStyle = CursorPromptStyle.XYZValues;
            // Pe3do1.PeUserInterface.Cursor.HighlightColor = Color.FromArgb(255, 255, 0, 0);

            // Export defaults
            Pe3do1.PeSpecial.DpiX = 600;
            Pe3do1.PeSpecial.DpiY = 600;
            Pe3do1.PeUserInterface.Dialog.ExportSizeDef  = ExportSizeDef.NoSizeOrPixel;
            Pe3do1.PeUserInterface.Dialog.ExportTypeDef  = ExportTypeDef.Png;
            Pe3do1.PeUserInterface.Dialog.ExportDestDef  = ExportDestDef.Clipboard;
            Pe3do1.PeUserInterface.Dialog.ExportUnitXDef = "1600";
            Pe3do1.PeUserInterface.Dialog.ExportUnitYDef = "900";
            Pe3do1.PeUserInterface.Dialog.ExportImageDpi = 300;
            Pe3do1.PeUserInterface.Dialog.AllowEmfExport = false;
            Pe3do1.PeUserInterface.Dialog.AllowWmfExport = false;

            // Step 9 — Finalize
            Pe3do1.PeFunction.Force3dxNewColors = true;
            Pe3do1.PeFunction.Force3dxVerticeRebuild = true;
            Pe3do1.PeFunction.ReinitializeResetImage();
            Pe3do1.Invalidate();
            Pe3do1.Update();
            Pe3do1.Refresh();
        }

        // -------------------------------------------------------------------
        // ElevationColorBytes — turbo-ish colormap. IDENTICAL to WPF (pure C#).
        // -------------------------------------------------------------------
        private static readonly (float Stop, byte R, byte G, byte B)[] kStops =
        {
            (0.00f,  20,  60, 130),
            (0.08f,   0, 130, 180),
            (0.18f,   0, 200, 190),
            (0.32f,   0, 190,  90),
            (0.55f, 180, 210,  40),
            (0.75f, 240, 170,  40),
            (1.00f, 230,  60,  60),
        };

        private static (byte R, byte G, byte B) ElevationColorBytes(float t)
        {
            if (t <= kStops[0].Stop) return (kStops[0].R, kStops[0].G, kStops[0].B);
            if (t >= kStops[^1].Stop) return (kStops[^1].R, kStops[^1].G, kStops[^1].B);

            for (int i = 1; i < kStops.Length; i++)
            {
                if (t <= kStops[i].Stop)
                {
                    var lo = kStops[i - 1];
                    var hi = kStops[i];
                    float u = (t - lo.Stop) / (hi.Stop - lo.Stop);
                    byte r = (byte)(lo.R + (hi.R - lo.R) * u);
                    byte g = (byte)(lo.G + (hi.G - lo.G) * u);
                    byte b = (byte)(lo.B + (hi.B - lo.B) * u);
                    return (r, g, b);
                }
            }
            return (255, 255, 255);
        }

        private static float Min(float[] a)
        {
            float m = a[0];
            for (int i = 1; i < a.Length; i++) if (a[i] < m) m = a[i];
            return m;
        }
        private static float Max(float[] a)
        {
            float m = a[0];
            for (int i = 1; i < a.Length; i++) if (a[i] > m) m = a[i];
            return m;
        }
    }
}
