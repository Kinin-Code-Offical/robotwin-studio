using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using D3D9 = Vortice.Direct3D9;

namespace RobotWin.Studio.Render
{
    /// <summary>
    /// [render] RobotWin's own renderer, on the GPU through Direct3D 11 (Vortice is only the binding to Windows' API).
    /// It draws what <see cref="SceneMirror"/> read: first the sun's shadow map, then the opaque meshes with physically
    /// based shading and 4× multisampling, then the transparent ones back to front; the result is resolved into a
    /// texture shared with Direct3D 9, which WPF shows through a D3DImage (so the 2D overlays stay as they are).
    /// </summary>
    internal sealed class GpuRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FrameData
        {
            public Matrix4x4 ViewProj, Light0, Light1, Light2;
            public Vector3 CameraPos; public float Exposure;
            public Vector3 SunDir; public float ShadowTexel;
            public Vector3 SunColor; public float ShadowsOn;
            public Vector3 SkyColor; public float AmbientScale;
            public Vector3 GroundColor; public float FillCount;
            public Vector4 FillDir0, FillDir1, FillDir2;
            public Vector4 FillColor0, FillColor1, FillColor2;
            public Vector4 Cascades, CascadeBias, CameraDir;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectData
        {
            public Matrix4x4 World, NormalMatrix;
            public Vector4 BaseColor;
            public Vector3 Emissive; public float Roughness;
            public float Metallic, HasTexture, Unlit, Pad;
            public Vector4 UvTransform;
            public Vector4 Overlay;
            public Vector4 GlowUv;
            public Vector4 GlowTint;   // w: has a glowing picture
        }

        private sealed class GpuMesh
        {
            public ID3D11Buffer? Vertices, Indices;
            public int VertexBytes, IndexBytes;
            public int IndexCount;
            public Vector3 Min, Max;
            public System.Windows.Rect UvBounds;
            public bool Dirty;
            public int LastUsed;
            public EventHandler? OnChanged;
        }

        private const int ShadowSize = 2048;
        private const int MaxLamps = 16;
        private const int VertexStride = 32;

        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _ctx;
        private readonly D3D9.IDirect3D9Ex? _d3d9;
        private readonly D3D9.IDirect3DDevice9Ex? _d9;
        private readonly ID3D11VertexShader _vs, _vsDepth;
        private readonly ID3D11PixelShader _ps;
        private readonly ID3D11InputLayout _layout;
        private readonly ID3D11Buffer _frameCb, _lampCb;
        private ID3D11Buffer? _objects, _instanceIds;
        private ID3D11ShaderResourceView? _objectsView;
        private int _objectCapacity;
        private ObjectData[] _objectData = Array.Empty<ObjectData>();
        private GpuMesh?[] _drawMesh = Array.Empty<GpuMesh?>();
        private Pass[] _drawPass = Array.Empty<Pass>();
        private float[] _drawDepth = Array.Empty<float>();
        private Vector4[] _drawSphere = Array.Empty<Vector4>();
        private bool[] _drawSeen = Array.Empty<bool>();   // [perf] in the view this frame (see Cull)
        private readonly List<int> _transparentOrder = new List<int>();

        /// <summary>Which pass draws a thing.</summary>
        private enum Pass : byte { None, Opaque, Transparent, Glow }

        private const int ObjectStride = 240;
        private readonly Vector4[] _lampData = new Vector4[1 + 4 * MaxLamps];
        private readonly ID3D11RasterizerState _cullBack, _cullFront, _shadowRaster;
        private readonly ID3D11BlendState _opaque, _alpha, _additive;
        private readonly ID3D11DepthStencilState _depthWrite, _sceneWrite, _sceneRead;
        private readonly ID3D11SamplerState _wrap, _clamp, _shadowCmp;
        private readonly ID3D11ShaderResourceView _white;
        private readonly ID3D11Texture2D _shadowTex;
        private readonly ID3D11DepthStencilView[] _shadowDsv = new ID3D11DepthStencilView[MaxCascades];
        private readonly ID3D11ShaderResourceView _shadowSrv;
        private readonly ID3D11Buffer _cascadeCb;
        private readonly uint[] _cascadeIndex = new uint[4];
        private readonly Matrix4x4[] _cascadeLight = new Matrix4x4[MaxCascades];
        private readonly float[] _cascadeRadius = new float[MaxCascades];
        private const int MaxCascades = 3;
        private readonly ID3D11Query _done;
        private readonly uint _samples;

        private sealed class GpuTexture
        {
            public ID3D11Texture2D Texture = null!;
            public ID3D11ShaderResourceView View = null!;
            public int Width, Height;

            /// <summary>Some of it is see-through (alpha below 250 of 255): drawn over what is behind.</summary>
            public bool Clear;

            public bool Dirty;
            public int LastUsed;
            public EventHandler? OnChanged;
        }

        /// <summary>
        /// Where one view draws: its multisampled colour and depth, and the resolved picture shared with Direct3D 9 (the
        /// surface its D3DImage shows). Each view has its own; the device, shaders and caches are the renderer's.
        /// </summary>
        public sealed class Target : IDisposable
        {
            private readonly GpuRenderer _r;
            internal int Width, Height;
            internal ID3D11Texture2D? Shared, Msaa, Depth;
            internal ID3D11RenderTargetView? Rtv;
            internal ID3D11DepthStencilView? Dsv;
            private D3D9.IDirect3DTexture9? _d9Texture;
            private D3D9.IDirect3DSurface9? _surface;

            internal Target(GpuRenderer r) => _r = r;

            /// <summary>Drawn for reading back only (tests): no surface shared with WPF.</summary>
            private bool Offscreen => _r._d9 == null;

            /// <summary>The Direct3D 9 surface WPF shows (D3DImage.SetBackBuffer), or zero before the first size.</summary>
            public IntPtr Surface => _surface?.NativePointer ?? IntPtr.Zero;

            /// <summary>Makes the target <paramref name="width"/> × <paramref name="height"/> pixels; true when the surface changed.</summary>
            public bool Resize(int width, int height)
            {
                width = Math.Clamp(width, 1, 16384);
                height = Math.Clamp(height, 1, 16384);
                if (width == Width && height == Height && Shared != null) return false;
                Release();
                Width = width;
                Height = height;
                var d = _r._device;
                Shared = d.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, Offscreen ? ResourceOptionFlags.None : ResourceOptionFlags.Shared));
                Msaa = d.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, _r._samples, 0, ResourceOptionFlags.None));
                Rtv = d.CreateRenderTargetView(Msaa);
                Depth = d.CreateTexture2D(new Texture2DDescription(Format.D32_Float, (uint)width, (uint)height, 1, 1, BindFlags.DepthStencil, ResourceUsage.Default, CpuAccessFlags.None, _r._samples, 0, ResourceOptionFlags.None));
                Dsv = d.CreateDepthStencilView(Depth);
                if (Offscreen) return true;
                using var resource = Shared.QueryInterface<IDXGIResource>();
                IntPtr handle = resource.SharedHandle;
                _d9Texture = _r._d9!.CreateTexture((uint)width, (uint)height, 1, D3D9.Usage.RenderTarget, D3D9.Format.A8R8G8B8, D3D9.Pool.Default, ref handle);
                _surface = _d9Texture.GetSurfaceLevel(0);
                return true;
            }

            private void Release()
            {
                _surface?.Dispose();
                _d9Texture?.Dispose();
                Dsv?.Dispose();
                Depth?.Dispose();
                Rtv?.Dispose();
                Msaa?.Dispose();
                Shared?.Dispose();
                _surface = null;
                _d9Texture = null;
                Dsv = null;
                Depth = null;
                Rtv = null;
                Msaa = null;
                Shared = null;
                Width = Height = 0;
            }

            public void Dispose() => Release();
        }

        /// <summary>A new place for a view to draw into.</summary>
        public Target CreateTarget() => new Target(this);

        /// <summary>Why the GPU renderer could not start, or null when it runs.</summary>
        public static string? Failure { get; private set; }

        /// <param name="window">A window for the Direct3D 9 device WPF shares the picture with; zero draws offscreen only.</param>
        /// <param name="driver">The GPU, or (tests on a machine without one) Windows' software rasterizer, WARP.</param>
        private GpuRenderer(IntPtr window, DriverType driver = DriverType.Hardware)
        {
            D3D11.D3D11CreateDevice(IntPtr.Zero, driver, DeviceCreationFlags.BgraSupport, new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 }, out var device, out var ctx).CheckError();
            _device = device!;
            _ctx = ctx!;
            if (window != IntPtr.Zero) (_d3d9, _d9) = CreateD3D9(window);
            var vsCode = Compiler.Compile(Shaders.Source, "VS", "render", "vs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
            var depthCode = Compiler.Compile(Shaders.Source, "VSDepth", "render", "vs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
            var psCode = Compiler.Compile(Shaders.Source, "PS", "render", "ps_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
            _vs = _device.CreateVertexShader(vsCode.Span);
            _vsDepth = _device.CreateVertexShader(depthCode.Span);
            _ps = _device.CreatePixelShader(psCode.Span);
            _layout = _device.CreateInputLayout(new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
                // The object's number, from a buffer of 0, 1, 2 … read at the draw's first instance.
                new InputElementDescription("OBJECT", 0, Format.R32_UInt, 0, 1, InputClassification.PerInstanceData, 1),
            }, vsCode.Span);
            if (Marshal.SizeOf<ObjectData>() != ObjectStride) throw new InvalidOperationException("ObjectData");

            _frameCb = _device.CreateBuffer((uint)Marshal.SizeOf<FrameData>(), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
            _lampCb = _device.CreateBuffer((uint)(16 * _lampData.Length), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);

            // WPF's front faces wind counter-clockwise.
            _cullBack = _device.CreateRasterizerState(new RasterizerDescription(CullMode.Back, FillMode.Solid) { FrontCounterClockwise = true, DepthClipEnable = true, MultisampleEnable = true, AntialiasedLineEnable = false });
            _cullFront = _device.CreateRasterizerState(new RasterizerDescription(CullMode.Front, FillMode.Solid) { FrontCounterClockwise = true, DepthClipEnable = true, MultisampleEnable = true });
            _shadowRaster = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthBias = 600, SlopeScaledDepthBias = 2.0f, DepthBiasClamp = 0.01f, DepthClipEnable = true });
            _opaque = _device.CreateBlendState(new BlendDescription(Blend.One, Blend.Zero));
            _alpha = _device.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
            _additive = _device.CreateBlendState(new BlendDescription(Blend.One, Blend.One, Blend.Zero, Blend.One));
            _depthWrite = _device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.LessEqual));
            _sceneWrite = _device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.GreaterEqual));
            _sceneRead = _device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.GreaterEqual));
            _wrap = _device.CreateSamplerState(new SamplerDescription(Filter.Anisotropic, TextureAddressMode.Wrap, 0, 8, ComparisonFunction.Never, 0, float.MaxValue));
            _clamp = _device.CreateSamplerState(new SamplerDescription(Filter.Anisotropic, TextureAddressMode.Clamp, 0, 8, ComparisonFunction.Never, 0, float.MaxValue));
            _shadowCmp = _device.CreateSamplerState(new SamplerDescription(Filter.ComparisonMinMagLinearMipPoint, TextureAddressMode.Border, TextureAddressMode.Border, TextureAddressMode.Border, 0, 1, ComparisonFunction.LessEqual, new Color4(1, 1, 1, 1), 0, 0));

            var white = _device.CreateTexture2D(new uint[] { 0xFFFFFFFF }, Format.B8G8R8A8_UNorm, 1, 1, 1, 1, BindFlags.ShaderResource, ResourceOptionFlags.None, ResourceUsage.Immutable, CpuAccessFlags.None);
            _white = _device.CreateShaderResourceView(white);
            white.Dispose();

            _shadowTex = _device.CreateTexture2D(new Texture2DDescription(Format.R32_Typeless, ShadowSize, ShadowSize, MaxCascades, 1, BindFlags.DepthStencil | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
            for (int k = 0; k < MaxCascades; k++)
                _shadowDsv[k] = _device.CreateDepthStencilView(_shadowTex, new DepthStencilViewDescription(DepthStencilViewDimension.Texture2DArray, Format.D32_Float, 0, (uint)k, 1, DepthStencilViewFlags.None));
            _shadowSrv = _device.CreateShaderResourceView(_shadowTex, new ShaderResourceViewDescription
            {
                Format = Format.R32_Float,
                ViewDimension = ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = new Texture2DArrayShaderResourceView { MipLevels = 1, FirstArraySlice = 0, ArraySize = MaxCascades },
            });
            _cascadeCb = _device.CreateBuffer(16, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
            _done = _device.CreateQuery(QueryType.Event, QueryFlags.None);

            _samples = 4;
            while (_samples > 1 && _device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, _samples) == 0) _samples /= 2;
        }

        private static (D3D9.IDirect3D9Ex, D3D9.IDirect3DDevice9Ex) CreateD3D9(IntPtr window)
        {
            var d3d9 = D3D9.D3D9.Direct3DCreate9Ex();
            var pp = new D3D9.PresentParameters
            {
                Windowed = true,
                SwapEffect = D3D9.SwapEffect.Discard,
                DeviceWindowHandle = window,
                PresentationInterval = D3D9.PresentInterval.Default,
                BackBufferFormat = D3D9.Format.Unknown,
                BackBufferWidth = 1,
                BackBufferHeight = 1,
            };
            var d9 = d3d9.CreateDeviceEx(0, D3D9.DeviceType.Hardware, window,
                D3D9.CreateFlags.HardwareVertexProcessing | D3D9.CreateFlags.Multithreaded | D3D9.CreateFlags.FpuPreserve, pp);
            return (d3d9, d9);
        }

        /// <summary>
        /// A renderer that draws offscreen only, for the render self-test: on the GPU, or with WARP where there is none
        /// (a build machine). Null when neither works.
        /// </summary>
        internal static GpuRenderer? CreateOffscreen(out string driver)
        {
            // RW_RENDER_WARP=1: WARP only (to check the software path on a machine that has a GPU).
            var kinds = Environment.GetEnvironmentVariable("RW_RENDER_WARP") == "1" ? new[] { DriverType.Warp } : new[] { DriverType.Hardware, DriverType.Warp };
            foreach (var kind in kinds)
            {
                try
                {
                    driver = kind.ToString();
                    return new GpuRenderer(IntPtr.Zero, kind);
                }
                catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException || ex is COMException || ex is DllNotFoundException)
                {
                }
            }

            driver = "none";
            return null;
        }

        /// <summary>The target's last picture as BGRA bytes, rows top to bottom (tests, and pictures of a view).</summary>
        internal byte[] ReadPixels(Target target)
        {
            int w = target.Width, h = target.Height;
            var data = new byte[w * h * 4];
            if (target.Shared == null) return data;
            using var staging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));
            _ctx.CopyResource(staging, target.Shared);
            var map = _ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(map.DataPointer + y * (int)map.RowPitch, data, y * w * 4, w * 4);
            }
            finally
            {
                _ctx.Unmap(staging, 0);
            }

            return data;
        }

        private static GpuRenderer? _shared1;
        private static bool _tried;

        /// <summary>The process's renderer (one device for every view), or null when the GPU cannot be used.</summary>
        public static GpuRenderer? Get(IntPtr window)
        {
            if (_tried) return _shared1;
            _tried = true;
            try
            {
                _shared1 = new GpuRenderer(window);
            }
            catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException || ex is COMException || ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is InvalidOperationException)
            {
                Failure = ex.Message;
                _shared1 = null;
            }

            return _shared1;
        }

        /// <summary>A camera as the renderer needs it: a perspective one (WPF's field of view is horizontal) or an orthographic one.</summary>
        public readonly struct Camera
        {
            public Camera(Point3D position, Vector3D look, Vector3D up, double horizontalFov, double near, double far)
                : this(position, look, up, near, far, false, horizontalFov * Math.PI / 180, 0)
            {
            }

            private Camera(Point3D position, Vector3D look, Vector3D up, double near, double far, bool orthographic, double fov, double width)
            {
                Position = new Vector3((float)position.X, (float)position.Y, (float)position.Z);
                Look = new Vector3((float)look.X, (float)look.Y, (float)look.Z);
                Up = new Vector3((float)up.X, (float)up.Y, (float)up.Z);
                HorizontalFov = (float)fov;
                Near = (float)near;
                Far = double.IsInfinity(far) || far > 1e12 ? float.PositiveInfinity : (float)far;
                Orthographic = orthographic;
                Width = (float)width;
            }

            /// <summary>An orthographic camera <paramref name="width"/> wide (WPF's OrthographicCamera.Width).</summary>
            public static Camera Ortho(Point3D position, Vector3D look, Vector3D up, double width, double near, double far) =>
                new Camera(position, look, up, near, far, true, 0, width);

            public Vector3 Position { get; }
            public Vector3 Look { get; }
            public Vector3 Up { get; }
            public float HorizontalFov { get; }
            public float Near { get; }

            /// <summary>The far plane; infinite when WPF's is (its default).</summary>
            public float Far { get; }

            public bool Orthographic { get; }
            public float Width { get; }

            /// <summary>How far ahead what the view looks at stands (an orbit camera's distance); 0 when not known.</summary>
            public float Focus { get; init; }
        }

        /// <summary>Draws the scene from the camera onto the shared surface; the colour behind everything is <paramref name="background"/>.</summary>
        public void Render(Target target, SceneMirror scene, Camera camera, System.Windows.Media.Color background)
        {
            if (target.Rtv == null || target.Dsv == null || target.Shared == null || target.Msaa == null) return;
            int width = target.Width, height = target.Height;
            float aspect = width / (float)height;
            var view = Matrix4x4.CreateLookAt(camera.Position, camera.Position + camera.Look, camera.Up);
            Matrix4x4 proj;
            if (camera.Orthographic)
            {
                // An orthographic view needs a far plane: the farthest the scene reaches along the view when WPF's is infinite.
                float far = float.IsInfinity(camera.Far) ? SceneFar(scene.Items, camera) : camera.Far;
                proj = ReversedOrtho(camera.Width, camera.Width / aspect, camera.Near, Math.Max(far, camera.Near + 1e-4f));
            }
            else
            {
                float vfov = 2 * MathF.Atan(MathF.Tan(camera.HorizontalFov / 2) / aspect);
                float near = Math.Max(camera.Near, 1e-5f);
                proj = float.IsInfinity(camera.Far) ? ReversedInfinite(vfov, aspect, near) : ReversedPerspective(vfov, aspect, near, Math.Max(camera.Far, near * 2));
            }

            // ---- lights: the strongest directional light is the sun (it casts the shadow), the rest fill in
            var suns = scene.Suns.OrderByDescending(s => s.Color.X + s.Color.Y + s.Color.Z).ToList();
            var sun = suns.Count > 0 ? suns[0] : new SunLight(Vector3.Normalize(new Vector3(-0.4f, 0.5f, -0.75f)), new Vector3(0.8f));
            var frame = new FrameData
            {
                ViewProj = view * proj,
                CameraPos = camera.Position,
                Exposure = 1.0f,
                SunDir = sun.Direction,
                SunColor = sun.Color * MathF.PI,   // WPF's light colours are irradiance on a Lambert surface: π × that is radiance
                SkyColor = scene.Ambient * 1.25f + new Vector3(0.02f, 0.025f, 0.035f),
                GroundColor = scene.Ambient * 0.55f,
                AmbientScale = 1.0f,
                ShadowTexel = 1f / ShadowSize,
            };
            int fills = Math.Min(3, suns.Count - 1);
            frame.FillCount = fills;
            for (int i = 0; i < fills; i++)
            {
                var d = new Vector4(suns[i + 1].Direction, 0);
                var c = new Vector4(suns[i + 1].Color * MathF.PI, 0);
                if (i == 0) { frame.FillDir0 = d; frame.FillColor0 = c; }
                else if (i == 1) { frame.FillDir1 = d; frame.FillColor1 = c; }
                else { frame.FillDir2 = d; frame.FillColor2 = c; }
            }

            // ---- the sun's shadow: up to three cascades along the view, each an orthographic map over its slice
            var items = scene.Items;
            float shadowFov = camera.Orthographic ? 0 : 2 * MathF.Atan(MathF.Tan(camera.HorizontalFov / 2) / aspect);
            int cascades = FitCascades(items, camera, aspect, shadowFov, sun.Direction, ref frame);
            frame.ShadowsOn = cascades > 0 ? 1 : 0;
            _ctx.UpdateSubresource(in frame, _frameCb);

            // ---- lamps: the nearest (to what the camera looks at) and brightest first
            var lamps = scene.Lamps;
            if (lamps.Count > MaxLamps)
            {
                var look = camera.Position + camera.Look;
                lamps = lamps.OrderByDescending(l => (l.Color.X + l.Color.Y + l.Color.Z) / (1 + (l.Position - look).LengthSquared())).Take(MaxLamps).ToList();
            }

            Array.Clear(_lampData, 0, _lampData.Length);
            _lampData[0] = new Vector4(lamps.Count, 0, 0, 0);
            for (int i = 0; i < lamps.Count; i++)
            {
                var l = lamps[i];
                _lampData[1 + i] = new Vector4(l.Position, l.Range);
                _lampData[1 + MaxLamps + i] = new Vector4(l.Color * MathF.PI, l.Spot ? 1 : 0);
                _lampData[1 + 2 * MaxLamps + i] = new Vector4(l.Attenuation, l.CosInner);
                _lampData[1 + 3 * MaxLamps + i] = new Vector4(l.Direction, l.CosOuter);
            }

            _ctx.UpdateSubresource(_lampData, _lampCb);

            // ---- every object's data, once for the frame
            int count = Prepare(items, camera.Position);
            Cull(count, view, camera, aspect, height);
            _ctx.IASetInputLayout(_layout);
            _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _ctx.IASetVertexBuffer(1, _instanceIds!, 4, 0);
            _ctx.VSSetConstantBuffer(0, _frameCb);
            _ctx.PSSetConstantBuffer(0, _frameCb);
            _ctx.PSSetConstantBuffer(2, _lampCb);
            _ctx.VSSetShaderResource(3, _objectsView!);
            _ctx.PSSetShaderResource(3, _objectsView!);
            ResetFilter();

            if (cascades > 0)
            {
                _ctx.PSSetShaderResource(1, null!);
                _ctx.RSSetViewport(0, 0, ShadowSize, ShadowSize, 0, 1);
                _ctx.RSSetState(_shadowRaster);
                _ctx.OMSetDepthStencilState(_depthWrite, 0);
                _ctx.VSSetShader(_vsDepth);
                _ctx.PSSetShader(null);
                _ctx.VSSetConstantBuffer(3, _cascadeCb);
                for (int k = 0; k < cascades; k++)
                {
                    _ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, _shadowDsv[k]);
                    _ctx.ClearDepthStencilView(_shadowDsv[k], DepthStencilClearFlags.Depth, 1, 0);
                    _cascadeIndex[0] = (uint)k;
                    _ctx.UpdateSubresource(_cascadeIndex, _cascadeCb);
                    for (int i = 0; i < count; i++)
                        if (_drawPass[i] == Pass.Opaque && !items[i].Back && InCascade(i, k))
                            Draw(i);
                }
            }

            // ---- the scene (reversed depth: near is 1, far 0, so a float depth buffer keeps its precision far away)
            _ctx.OMSetRenderTargets(target.Rtv, target.Dsv);
            // Premultiplied, as WPF composes the picture: a clear background lets the panel behind show through.
            float ca = background.A / 255f;
            var clear = new Color4(background.R / 255f * ca, background.G / 255f * ca, background.B / 255f * ca, ca);
            _ctx.ClearRenderTargetView(target.Rtv, clear);
            _ctx.ClearDepthStencilView(target.Dsv, DepthStencilClearFlags.Depth, 0, 0);
            _ctx.RSSetViewport(0, 0, width, height, 0, 1);
            _ctx.VSSetShader(_vs);
            _ctx.PSSetShader(_ps);
            _ctx.PSSetShaderResource(1, _shadowSrv);
            _ctx.PSSetSampler(1, _shadowCmp);
            _ctx.OMSetBlendState(_opaque);
            _ctx.OMSetDepthStencilState(_sceneWrite, 0);
            ResetFilter();
            for (int i = 0; i < count; i++)
                if (_drawPass[i] == Pass.Opaque && _drawSeen[i])
                    DrawShaded(items[i], i);

            // ---- transparent things, farthest first, over what is behind them
            _transparentOrder.Clear();
            for (int i = 0; i < count; i++)
                if (_drawPass[i] == Pass.Transparent && _drawSeen[i])
                    _transparentOrder.Add(i);
            _transparentOrder.Sort((x, y) => _drawDepth[y].CompareTo(_drawDepth[x]));
            _ctx.OMSetBlendState(_alpha);
            _ctx.OMSetDepthStencilState(_sceneRead, 0);
            foreach (int i in _transparentOrder) DrawShaded(items[i], i);

            // ---- light of their own (a glow, a halo, the editor's handles): added to what is behind, as WPF does
            _ctx.OMSetBlendState(_additive);
            for (int i = 0; i < count; i++)
                if (_drawPass[i] == Pass.Glow && _drawSeen[i])
                    DrawShaded(items[i], i);

            _ctx.ResolveSubresource(target.Shared, 0, target.Msaa, 0, Format.B8G8R8A8_UNorm);
            _ctx.End(_done);
            _ctx.Flush();
            // WPF composes the surface from another device: it must be finished before it is shown.
            while (!_ctx.IsDataAvailable(_done)) System.Threading.Thread.SpinWait(32);
            Sweep();
        }

        /// <summary>
        /// Builds the frame's object table (world matrix, normals, material, picture mappings) and sorts each thing into
        /// its pass; the table goes to the GPU in one write. Returns how many things there are.
        /// </summary>
        private int Prepare(List<DrawItem> items, Vector3 eye)
        {
            int n = items.Count;
            EnsureObjects(n);
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                var gm = Mesh(it.Mesh);
                _drawMesh[i] = gm;
                if (gm == null)
                {
                    _drawPass[i] = Pass.None;
                    continue;
                }

                _objectData[i] = Object(it, gm);
                var w = it.World;
                float scale = MathF.Sqrt(MathF.Max(new Vector3(w.M11, w.M12, w.M13).LengthSquared(), MathF.Max(new Vector3(w.M21, w.M22, w.M23).LengthSquared(), new Vector3(w.M31, w.M32, w.M33).LengthSquared())));
                _drawSphere[i] = new Vector4(Vector3.Transform((gm.Min + gm.Max) / 2, w), (gm.Max - gm.Min).Length() / 2 * scale);
                if (it.Material.Unlit)
                {
                    _drawPass[i] = Pass.Glow;
                }
                else if (IsTransparent(it))
                {
                    _drawPass[i] = Pass.Transparent;
                    _drawDepth[i] = (Vector3.Transform((gm.Min + gm.Max) / 2, it.World) - eye).LengthSquared();
                }
                else
                {
                    _drawPass[i] = Pass.Opaque;
                }
            }

            if (n > 0)
                _ctx.UpdateSubresource(new ReadOnlySpan<ObjectData>(_objectData, 0, n), _objects!, 0, 0, 0, new Box(0, 0, 0, n * ObjectStride, 1, 1));
            return n;
        }

        /// <summary>
        /// [perf] Which objects the view shows: one whose bounding sphere lies wholly outside the view's sides or behind
        /// it is not drawn, nor one that would cover less than half a pixel across (a screw on the far side of a field).
        /// The sun's shadow still has them all: what is out of view still casts shade into it.
        /// </summary>
        private void Cull(int count, Matrix4x4 view, Camera camera, float aspect, int heightPx)
        {
            float halfH = camera.Orthographic ? camera.Width / 2 : MathF.Tan(camera.HorizontalFov / 2);
            float halfV = halfH / aspect;
            // The side planes through the eye (perspective), as normals in view space (x right, y up, looking down −z).
            float ch = 1 / MathF.Sqrt(1 + halfH * halfH), sh = halfH * ch, cv = 1 / MathF.Sqrt(1 + halfV * halfV), sv = halfV * cv;
            // Half a pixel across, as an angle (perspective) or a length (orthographic).
            float halfPixel = 0.5f * 2 * halfV / Math.Max(1, heightPx);
            for (int i = 0; i < count; i++)
            {
                if (_drawPass[i] == Pass.None)
                {
                    _drawSeen[i] = false;
                    continue;
                }

                var s = _drawSphere[i];
                var c = Vector3.Transform(new Vector3(s.X, s.Y, s.Z), view);
                float r = s.W, z = -c.Z;   // depth ahead of the eye
                bool seen;
                if (camera.Orthographic)
                    seen = MathF.Abs(c.X) <= halfH + r && MathF.Abs(c.Y) <= halfV + r && 2 * r >= halfPixel;
                else
                    seen = z >= -r
                           && c.X * ch - z * sh <= r && -c.X * ch - z * sh <= r
                           && c.Y * cv - z * sv <= r && -c.Y * cv - z * sv <= r
                           && (z <= r || 2 * r >= halfPixel * z);
                _drawSeen[i] = seen;
            }
        }

        /// <summary>Room for <paramref name="n"/> objects in the GPU's table (it doubles as needed).</summary>
        private void EnsureObjects(int n)
        {
            if (n <= _objectCapacity && _objects != null) return;
            int cap = Math.Max(1024, _objectCapacity);
            while (cap < n) cap *= 2;
            _objectsView?.Dispose();
            _objects?.Dispose();
            _instanceIds?.Dispose();
            _objects = _device.CreateBuffer(new BufferDescription((uint)(cap * ObjectStride), BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, ObjectStride));
            _objectsView = _device.CreateShaderResourceView(_objects, new ShaderResourceViewDescription
            {
                Format = Format.Unknown,
                ViewDimension = ShaderResourceViewDimension.Buffer,
                Buffer = new BufferShaderResourceView { FirstElement = 0, NumElements = (uint)cap },
            });
            var ids = new uint[cap];
            for (int i = 0; i < cap; i++) ids[i] = (uint)i;
            _instanceIds = _device.CreateBuffer(ids, BindFlags.VertexBuffer, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
            Array.Resize(ref _objectData, cap);
            Array.Resize(ref _drawMesh, cap);
            Array.Resize(ref _drawPass, cap);
            Array.Resize(ref _drawDepth, cap);
            Array.Resize(ref _drawSphere, cap);
            Array.Resize(ref _drawSeen, cap);
            _objectCapacity = cap;
        }

        // What is bound now: a draw sets only what differs from the one before.
        private GpuMesh? _boundMesh;
        private ID3D11RasterizerState? _boundRaster;
        private ID3D11ShaderResourceView? _boundAlbedo, _boundGlow;
        private ID3D11SamplerState? _boundAlbedoSampler, _boundGlowSampler;

        private void ResetFilter()
        {
            _boundMesh = null;
            _boundRaster = null;
            _boundAlbedo = _boundGlow = null;
            _boundAlbedoSampler = _boundGlowSampler = null;
        }

        private void DrawShaded(DrawItem it, int index)
        {
            var raster = it.Back ? _cullFront : _cullBack;
            if (raster != _boundRaster) _ctx.RSSetState(_boundRaster = raster);
            var m = it.Material;
            var albedo = m.Albedo is { } a && Texture(a.Image) is { } at ? at.View : _white;
            if (albedo != _boundAlbedo) _ctx.PSSetShaderResource(0, _boundAlbedo = albedo);
            var albedoSampler = m.Albedo is { Tile: true } ? _wrap : _clamp;
            if (albedoSampler != _boundAlbedoSampler) _ctx.PSSetSampler(0, _boundAlbedoSampler = albedoSampler);
            var glow = m.Glow is { } g && Texture(g.Image) is { } gt ? gt.View : _white;
            if (glow != _boundGlow) _ctx.PSSetShaderResource(2, _boundGlow = glow);
            var glowSampler = m.Glow is { Tile: true } ? _wrap : _clamp;
            if (glowSampler != _boundGlowSampler) _ctx.PSSetSampler(2, _boundGlowSampler = glowSampler);
            Draw(index);
        }

        private void Draw(int index)
        {
            var gm = _drawMesh[index]!;
            if (gm != _boundMesh)
            {
                _boundMesh = gm;
                _ctx.IASetVertexBuffer(0, gm.Vertices!, VertexStride, 0);
                _ctx.IASetIndexBuffer(gm.Indices!, Format.R32_UInt, 0);
            }

            // One instance, numbered as the object: the shaders read its data from the table.
            _ctx.DrawIndexedInstanced((uint)gm.IndexCount, 1, 0, 0, (uint)index);
        }

        /// <summary>As <see cref="ReversedPerspective"/> with the far plane at infinity (depth near / distance).</summary>
        private static Matrix4x4 ReversedInfinite(float vfov, float aspect, float near)
        {
            float y = 1 / MathF.Tan(vfov / 2);
            return new Matrix4x4(
                y / aspect, 0, 0, 0,
                0, y, 0, 0,
                0, 0, 0, -1,
                0, 0, near, 0);
        }

        /// <summary>A right-handed orthographic projection <paramref name="width"/> × <paramref name="height"/>, reversed depth.</summary>
        private static Matrix4x4 ReversedOrtho(float width, float height, float near, float far)
        {
            float range = far - near;
            return new Matrix4x4(
                2 / width, 0, 0, 0,
                0, 2 / height, 0, 0,
                0, 0, 1 / range, 0,
                0, 0, far / range, 1);
        }

        /// <summary>How far along the view the scene reaches (for an orthographic camera with WPF's infinite far plane).</summary>
        private float SceneFar(List<DrawItem> items, Camera camera)
        {
            var dir = Vector3.Normalize(camera.Look);
            float far = camera.Near + 1;
            foreach (var it in items)
            {
                if (Mesh(it.Mesh) is not { } gm) continue;
                for (int i = 0; i < 8; i++)
                {
                    var p = Vector3.Transform(Corner(gm, i), it.World);
                    far = Math.Max(far, Vector3.Dot(p - camera.Position, dir));
                }
            }

            return far * 1.05f;
        }

        private static Vector3 Corner(GpuMesh gm, int i) =>
            new Vector3((i & 1) == 0 ? gm.Min.X : gm.Max.X, (i & 2) == 0 ? gm.Min.Y : gm.Max.Y, (i & 4) == 0 ? gm.Min.Z : gm.Max.Z);

        /// <summary>A right-handed perspective with reversed depth (near → 1, far → 0), as the scene pass expects.</summary>
        private static Matrix4x4 ReversedPerspective(float vfov, float aspect, float near, float far)
        {
            float y = 1 / MathF.Tan(vfov / 2);
            return new Matrix4x4(
                y / aspect, 0, 0, 0,
                0, y, 0, 0,
                0, 0, near / (far - near), -1,
                0, 0, near * far / (far - near), 0);
        }

        private static ObjectData Object(DrawItem it, GpuMesh gm)
        {
            // A part scaled flat (zero along an axis) has no inverse: its normals then go through the matrix itself.
            var normal = Matrix4x4.Invert(it.World, out var inv) ? Matrix4x4.Transpose(inv) : it.World;
            normal.M41 = normal.M42 = normal.M43 = 0;
            var m = it.Material;
            return new ObjectData
            {
                World = it.World,
                NormalMatrix = normal,
                BaseColor = m.BaseColor,
                Emissive = m.Emissive,
                Roughness = m.Roughness,
                Metallic = m.Metallic,
                HasTexture = m.Albedo != null ? 1 : 0,
                Unlit = m.Unlit ? 1 : 0,
                UvTransform = m.Albedo is { } a ? UvTransform(a, gm) : new Vector4(1, 1, 0, 0),
                Overlay = m.Overlay,
                GlowUv = m.Glow is { } g ? UvTransform(g, gm) : new Vector4(1, 1, 0, 0),
                GlowTint = new Vector4(m.GlowTint, m.Glow != null ? 1 : 0),
            };
        }

        /// <summary>A picture's mapping as uv · xy + zw: its viewport, or (relative units) a part of the texture coordinates' own bounds.</summary>
        private static Vector4 UvTransform(TextureMap map, GpuMesh gm)
        {
            var r = map.UvRect;
            if (map.Relative)
            {
                var b = gm.UvBounds;
                r = new System.Windows.Rect(b.X + r.X * b.Width, b.Y + r.Y * b.Height, Math.Max(1e-9, r.Width * b.Width), Math.Max(1e-9, r.Height * b.Height));
            }

            float sx = (float)(1 / Math.Max(1e-9, r.Width)), sy = (float)(1 / Math.Max(1e-9, r.Height));
            return new Vector4(sx, sy, (float)(-r.X * sx), (float)(-r.Y * sy));
        }

        /// <summary>Drawn over what is behind it: a see-through colour, or a picture with clear parts.</summary>
        private bool IsTransparent(DrawItem it) =>
            it.Material.Transparent || (it.Material.Albedo is { } a && Texture(a.Image) is { Clear: true });

        /// <summary>The middle and reach of what the shadow must cover: the scene, but no farther than a few views' depth around the look point.</summary>
        private readonly List<float> _depths = new List<float>();

        /// <summary>
        /// The scene's box (what casts and takes shadows: opaque, lit) and how far ahead the camera's subject stands: the
        /// view's orbit distance when it gives one, a look direction drawn to the target, else where most things in
        /// sight stand (not the middle of a kilometre of terrain). False for an empty scene.
        /// </summary>
        private bool SceneBox(List<DrawItem> items, Camera camera, out Vector3 min, out Vector3 max, out float focus)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            focus = 0;
            var dir = Vector3.Normalize(camera.Look);
            float cone = camera.Orthographic ? -1 : MathF.Cos(MathF.Min(1.4f, camera.HorizontalFov * 0.75f));
            _depths.Clear();
            foreach (var it in items)
            {
                if (it.Material.Unlit || IsTransparent(it) || Mesh(it.Mesh) is not { } gm) continue;
                for (int i = 0; i < 8; i++)
                {
                    var p = Vector3.Transform(Corner(gm, i), it.World);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }

                var v = Vector3.Transform((gm.Min + gm.Max) / 2, it.World) - camera.Position;
                float d = Vector3.Dot(v, dir), len = v.Length();
                if (d > camera.Near && (camera.Orthographic || d >= cone * len)) _depths.Add(d);
            }

            if (min.X > max.X) return false;
            float lookLength = camera.Look.Length();
            if (camera.Focus > 0)
            {
                focus = camera.Focus;
            }
            else if (MathF.Abs(lookLength - 1) > 0.01f)
            {
                focus = lookLength;   // a look direction drawn to the target (the world's orbit camera)
            }
            else if (_depths.Count > 0)
            {
                _depths.Sort();
                focus = _depths[(int)(_depths.Count * 0.4)];
            }
            else
            {
                focus = Vector3.Dot((min + max) / 2 - camera.Position, dir);
            }

            focus = Math.Max(focus, (max - min).Length() * 0.002f);
            return true;
        }

        /// <summary>
        /// Fits the sun's shadow cascades: the view split at 1.6, 5 and 20 times the subject's distance (fewer when the
        /// scene ends sooner), each slice of the view enclosed in a sphere, shrunk to the scene when that is smaller (a
        /// small table gets the map's whole resolution), and seen by the sun from far enough back to catch whatever in
        /// the scene can cast into it. An orthographic view has one cascade over the scene. Returns how many.
        /// </summary>
        private int FitCascades(List<DrawItem> items, Camera camera, float aspect, float vfov, Vector3 sunDir, ref FrameData frame)
        {
            if (!SceneBox(items, camera, out var min, out var max, out float focus)) return 0;
            var dir = Vector3.Normalize(camera.Look);
            float sceneNear = float.MaxValue, sceneFar = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
                float d = Vector3.Dot(c - camera.Position, dir);
                sceneNear = Math.Min(sceneNear, d);
                sceneFar = Math.Max(sceneFar, d);
            }

            if (!camera.Orthographic && sceneFar <= camera.Near) return 0;   // all of it behind the camera
            Span<float> ends = stackalloc float[MaxCascades];
            int n;
            if (camera.Orthographic)
            {
                n = 1;
                ends[0] = 1e30f;
            }
            else
            {
                n = 0;
                float[] want = { focus * 1.6f, focus * 5f, focus * 20f };
                for (int k = 0; k < MaxCascades; k++)
                {
                    ends[k] = want[k];
                    n++;
                    if (want[k] >= sceneFar)
                    {
                        ends[k] = 1e30f;   // the scene ends inside this cascade: no fading out at its end
                        break;
                    }
                }
            }

            var right = Vector3.Cross(dir, camera.Up);
            right = right.LengthSquared() > 1e-12f ? Vector3.Normalize(right) : Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
            var up = Vector3.Cross(right, dir);
            float th = camera.Orthographic ? camera.Width / aspect / 2 : MathF.Tan(vfov / 2);
            float tw = camera.Orthographic ? camera.Width / 2 : th * aspect;
            float a = camera.Orthographic ? Math.Max(sceneNear, camera.Near) : Math.Max(camera.Near, 1e-5f);
            Span<Vector3> corners = stackalloc Vector3[8];
            for (int k = 0; k < n; k++)
            {
                float b = Math.Max(Math.Min(ends[k], sceneFar * 1.001f), a + 1e-4f);
                var center = Vector3.Zero;
                for (int i = 0; i < 8; i++)
                {
                    float d = (i & 4) == 0 ? a : b;
                    float hh = camera.Orthographic ? th : d * th, hw = camera.Orthographic ? tw : d * tw;
                    corners[i] = camera.Position + dir * d + right * ((i & 1) == 0 ? -hw : hw) + up * ((i & 2) == 0 ? -hh : hh);
                    center += corners[i];
                }

                center /= 8;
                float radius = 0;
                for (int i = 0; i < 8; i++) radius = Math.Max(radius, (corners[i] - center).Length());
                // No bigger than the scene within it.
                var lo = Vector3.Max(min, center - new Vector3(radius));
                var hi = Vector3.Min(max, center + new Vector3(radius));
                if (lo.X <= hi.X && lo.Y <= hi.Y && lo.Z <= hi.Z && (hi - lo).Length() / 2 < radius)
                {
                    center = (lo + hi) / 2;
                    radius = (hi - lo).Length() / 2;
                }

                radius = Math.Max(radius, 1e-4f);
                // The sun stands back as far as the scene reaches toward it, so a tall thing outside the slice still casts in.
                float back = radius;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
                    back = Math.Max(back, Vector3.Dot(c - center, -sunDir));
                }

                back *= 1.01f;
                var light = LightMatrix(sunDir, center, radius, back);
                _cascadeLight[k] = light;
                _cascadeRadius[k] = radius;
                if (k == 0) frame.Light0 = light;
                else if (k == 1) frame.Light1 = light;
                else frame.Light2 = light;
                // A shadow texel in the map's depth units (its depth spans back + radius).
                float bias = 2 * (2 * radius / ShadowSize) / (back + radius);
                if (k == 0) frame.CascadeBias.X = bias;
                else if (k == 1) frame.CascadeBias.Y = bias;
                else frame.CascadeBias.Z = bias;
                // The next slice starts where this one begins to fade into it.
                a = a + (b - a) * 0.85f;
            }

            frame.Cascades = new Vector4(ends[0], n > 1 ? ends[1] : ends[0], n > 2 ? ends[2] : (n > 1 ? ends[1] : ends[0]), n);
            frame.CameraDir = new Vector4(dir, 0);
            return n;
        }

        /// <summary>Whether a thing can cast into cascade <paramref name="k"/>: its sphere overlaps the map's square.</summary>
        private bool InCascade(int index, int k)
        {
            var s = _drawSphere[index];
            var p = Vector4.Transform(new Vector4(s.X, s.Y, s.Z, 1), _cascadeLight[k]);
            float reach = 1 + s.W / _cascadeRadius[k];
            return MathF.Abs(p.X) <= reach && MathF.Abs(p.Y) <= reach;
        }

        /// <summary>The sun's view of a sphere: from <paramref name="back"/> up-sun, an orthographic square around it.</summary>
        private static Matrix4x4 LightMatrix(Vector3 dir, Vector3 center, float radius, float back)
        {
            var up = MathF.Abs(dir.Z) > 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            var eye = center - dir * back;
            var view = Matrix4x4.CreateLookAt(eye, center, up);
            // Snap the centre to whole shadow texels, so the shadow does not shimmer as the camera moves.
            var c = Vector3.Transform(center, view);
            float texel = 2 * radius / ShadowSize;
            var snap = Matrix4x4.CreateTranslation(-(c.X - MathF.Floor(c.X / texel) * texel), -(c.Y - MathF.Floor(c.Y / texel) * texel), 0);
            var ortho = Matrix4x4.CreateOrthographic(2 * radius, 2 * radius, 0, back + radius);
            return view * snap * ortho;
        }

        // ================================================================ meshes and pictures on the GPU

        private int _frame;
        private readonly Dictionary<MeshGeometry3D, GpuMesh> _meshes = new Dictionary<MeshGeometry3D, GpuMesh>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ImageSource, GpuTexture> _textures = new Dictionary<ImageSource, GpuTexture>(ReferenceEqualityComparer.Instance);
        private readonly List<(ID3D11Buffer Buffer, int Bytes)> _freeVertices = new List<(ID3D11Buffer, int)>();
        private readonly List<(ID3D11Buffer Buffer, int Bytes)> _freeIndices = new List<(ID3D11Buffer, int)>();
        private readonly List<MeshGeometry3D> _staleMeshes = new List<MeshGeometry3D>();
        private readonly List<ImageSource> _staleTextures = new List<ImageSource>();

        /// <summary>A mesh not drawn for this many frames gives its buffers back (a cable built anew every frame leaves its old one).</summary>
        private const int KeepMeshFrames = 8;

        /// <summary>A picture not drawn for this many frames is let go.</summary>
        private const int KeepTextureFrames = 600;

        /// <summary>Meshes and pictures on the GPU now (for tests and the performance overlay).</summary>
        internal (int Meshes, int Textures, int FreeBuffers) CacheSizes => (_meshes.Count, _textures.Count, _freeVertices.Count + _freeIndices.Count);

        private GpuMesh? Mesh(MeshGeometry3D mesh)
        {
            if (_meshes.TryGetValue(mesh, out var gm) && !gm.Dirty)
            {
                gm.LastUsed = _frame;
                return gm;
            }

            var positions = mesh.Positions;
            int n = positions.Count;
            if (n == 0) return null;
            var normals = mesh.Normals;
            var uvs = mesh.TextureCoordinates;
            var idx = mesh.TriangleIndices;
            int[] indices;
            if (idx.Count >= 3)
            {
                indices = new int[idx.Count - idx.Count % 3];
                for (int i = 0; i < indices.Length; i++) indices[i] = Math.Clamp(idx[i], 0, n - 1);
            }
            else
            {
                indices = new int[n - n % 3];
                for (int i = 0; i < indices.Length; i++) indices[i] = i;
            }

            if (indices.Length == 0) return null;
            var nrm = new Vector3[n];
            if (normals.Count == n)
            {
                for (int i = 0; i < n; i++) nrm[i] = new Vector3((float)normals[i].X, (float)normals[i].Y, (float)normals[i].Z);
            }
            else
            {
                // As WPF does when a mesh has none: each vertex the sum of its triangles' (area-weighted) normals.
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    var a = positions[indices[t]];
                    var b = positions[indices[t + 1]];
                    var c = positions[indices[t + 2]];
                    var f = Vector3D.CrossProduct(b - a, c - a);
                    var fv = new Vector3((float)f.X, (float)f.Y, (float)f.Z);
                    nrm[indices[t]] += fv;
                    nrm[indices[t + 1]] += fv;
                    nrm[indices[t + 2]] += fv;
                }
            }

            var data = new float[n * 8];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            double uMin = double.MaxValue, vMin = double.MaxValue, uMax = double.MinValue, vMax = double.MinValue;
            bool hasUv = uvs.Count == n;
            for (int i = 0; i < n; i++)
            {
                var p = positions[i];
                var v = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
                var nn = nrm[i].LengthSquared() > 0 ? Vector3.Normalize(nrm[i]) : Vector3.UnitZ;
                double u = 0, w = 0;
                if (hasUv)
                {
                    var uv = uvs[i];
                    u = uv.X;
                    w = uv.Y;
                    uMin = Math.Min(uMin, u);
                    uMax = Math.Max(uMax, u);
                    vMin = Math.Min(vMin, w);
                    vMax = Math.Max(vMax, w);
                }

                int k = 8 * i;
                data[k] = v.X;
                data[k + 1] = v.Y;
                data[k + 2] = v.Z;
                data[k + 3] = nn.X;
                data[k + 4] = nn.Y;
                data[k + 5] = nn.Z;
                data[k + 6] = (float)u;
                data[k + 7] = (float)w;
            }

            if (gm == null)
            {
                gm = new GpuMesh();
                if (!mesh.IsFrozen)
                {
                    // A cable, a liquid's surface: built again when it changes.
                    var self = gm;
                    gm.OnChanged = (_, _) => self.Dirty = true;
                    mesh.Changed += gm.OnChanged;
                }

                _meshes[mesh] = gm;
            }

            // Into buffers big enough, taken from those other meshes gave back when there are (no new allocation per cable per frame).
            int vertexBytes = data.Length * 4, indexBytes = indices.Length * 4;
            if (gm.Vertices == null || gm.VertexBytes < vertexBytes)
            {
                if (gm.Vertices != null) Give(_freeVertices, gm.Vertices, gm.VertexBytes);
                (gm.Vertices, gm.VertexBytes) = Take(_freeVertices, vertexBytes, BindFlags.VertexBuffer);
            }

            if (gm.Indices == null || gm.IndexBytes < indexBytes)
            {
                if (gm.Indices != null) Give(_freeIndices, gm.Indices, gm.IndexBytes);
                (gm.Indices, gm.IndexBytes) = Take(_freeIndices, indexBytes, BindFlags.IndexBuffer);
            }

            _ctx.UpdateSubresource(new ReadOnlySpan<float>(data), gm.Vertices, 0, 0, 0, new Box(0, 0, 0, vertexBytes, 1, 1));
            _ctx.UpdateSubresource(new ReadOnlySpan<int>(indices), gm.Indices, 0, 0, 0, new Box(0, 0, 0, indexBytes, 1, 1));
            gm.IndexCount = indices.Length;
            gm.Min = min;
            gm.Max = max;
            gm.UvBounds = uMin <= uMax ? new System.Windows.Rect(uMin, vMin, Math.Max(1e-9, uMax - uMin), Math.Max(1e-9, vMax - vMin)) : new System.Windows.Rect(0, 0, 1, 1);
            gm.Dirty = false;
            gm.LastUsed = _frame;
            return gm;
        }

        /// <summary>The smallest free buffer that holds <paramref name="bytes"/> (and is not wastefully bigger), else a new one.</summary>
        private (ID3D11Buffer Buffer, int Bytes) Take(List<(ID3D11Buffer Buffer, int Bytes)> pool, int bytes, BindFlags bind)
        {
            int best = -1;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].Bytes >= bytes && (best < 0 || pool[i].Bytes < pool[best].Bytes))
                    best = i;
            if (best >= 0 && pool[best].Bytes <= Math.Max(4096, bytes * 4))
            {
                var found = pool[best];
                pool.RemoveAt(best);
                return found;
            }

            int size = 1024;
            while (size < bytes) size *= 2;
            return (_device.CreateBuffer(new BufferDescription((uint)size, bind, ResourceUsage.Default)), size);
        }

        private static void Give(List<(ID3D11Buffer Buffer, int Bytes)> pool, ID3D11Buffer buffer, int bytes)
        {
            pool.Add((buffer, bytes));
            // Enough for a frame's worth of changing meshes; the oldest beyond that are freed.
            while (pool.Count > 512)
            {
                pool[0].Buffer.Dispose();
                pool.RemoveAt(0);
            }
        }

        /// <summary>After a frame: meshes and pictures no longer drawn give back what they hold on the GPU.</summary>
        private void Sweep()
        {
            _frame++;
            if ((_frame & 3) != 0) return;
            _staleMeshes.Clear();
            foreach (var pair in _meshes)
                if (_frame - pair.Value.LastUsed > KeepMeshFrames)
                    _staleMeshes.Add(pair.Key);
            foreach (var mesh in _staleMeshes)
            {
                var gm = _meshes[mesh];
                if (gm.OnChanged != null && !mesh.IsFrozen) mesh.Changed -= gm.OnChanged;
                if (gm.Vertices != null) Give(_freeVertices, gm.Vertices, gm.VertexBytes);
                if (gm.Indices != null) Give(_freeIndices, gm.Indices, gm.IndexBytes);
                _meshes.Remove(mesh);
            }

            _staleTextures.Clear();
            foreach (var pair in _textures)
                if (_frame - pair.Value.LastUsed > KeepTextureFrames)
                    _staleTextures.Add(pair.Key);
            foreach (var image in _staleTextures)
            {
                var t = _textures[image];
                if (t.OnChanged != null && !image.IsFrozen) image.Changed -= t.OnChanged;
                t.View.Dispose();
                t.Texture.Dispose();
                _textures.Remove(image);
            }
        }

        private GpuTexture? Texture(ImageSource image)
        {
            if (_textures.TryGetValue(image, out var cached) && !cached.Dirty)
            {
                cached.LastUsed = _frame;
                return cached;
            }

            if (image is not BitmapSource bmp) return null;
            try
            {
                var src = bmp.Format == PixelFormats.Bgra32 || bmp.Format == PixelFormats.Pbgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int w = src.PixelWidth, h = src.PixelHeight;
                if (w <= 0 || h <= 0) return null;
                var pixels = new byte[w * h * 4];
                src.CopyPixels(pixels, w * 4, 0);
                bool clear = false;
                for (int i = 3; i < pixels.Length && !clear; i += 4) clear = pixels[i] < 250;
                if (src.Format == PixelFormats.Pbgra32)
                {
                    // Premultiplied: back to straight alpha, as the shader blends it.
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        int a = pixels[i + 3];
                        if (a == 0 || a == 255) continue;
                        pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                        pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                        pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
                    }
                }

                if (cached == null)
                {
                    cached = new GpuTexture();
                    if (!image.IsFrozen)
                    {
                        // A display's picture drawn anew, a written bitmap: uploaded again when it changes.
                        var self = cached;
                        cached.OnChanged = (_, _) => self.Dirty = true;
                        image.Changed += cached.OnChanged;
                    }

                    _textures[image] = cached;
                }

                if (cached.Texture == null || cached.Width != w || cached.Height != h)
                {
                    cached.View?.Dispose();
                    cached.Texture?.Dispose();
                    cached.Texture = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 0, BindFlags.ShaderResource | BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips));
                    cached.View = _device.CreateShaderResourceView(cached.Texture);
                    cached.Width = w;
                    cached.Height = h;
                }

                _ctx.UpdateSubresource(pixels, cached.Texture, 0, (uint)(w * 4), 0, null);
                _ctx.GenerateMips(cached.View);
                cached.Clear = clear;
                cached.Dirty = false;
                cached.LastUsed = _frame;
                return cached;
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is InvalidOperationException || ex is ArgumentException || ex is SharpGen.Runtime.SharpGenException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            foreach (var gm in _meshes.Values)
            {
                gm.Vertices?.Dispose();
                gm.Indices?.Dispose();
            }

            foreach (var t in _textures.Values)
            {
                t.View.Dispose();
                t.Texture.Dispose();
            }

            foreach (var (buffer, _) in _freeVertices) buffer.Dispose();
            foreach (var (buffer, _) in _freeIndices) buffer.Dispose();
            _done.Dispose();
            _shadowSrv.Dispose();
            foreach (var dsv in _shadowDsv) dsv.Dispose();
            _shadowTex.Dispose();
            _cascadeCb.Dispose();
            _white.Dispose();
            _shadowCmp.Dispose();
            _clamp.Dispose();
            _wrap.Dispose();
            _sceneRead.Dispose();
            _sceneWrite.Dispose();
            _depthWrite.Dispose();
            _additive.Dispose();
            _alpha.Dispose();
            _opaque.Dispose();
            _shadowRaster.Dispose();
            _cullFront.Dispose();
            _cullBack.Dispose();
            _lampCb.Dispose();
            _objectsView?.Dispose();
            _objects?.Dispose();
            _instanceIds?.Dispose();
            _frameCb.Dispose();
            _layout.Dispose();
            _ps.Dispose();
            _vsDepth.Dispose();
            _vs.Dispose();
            _d9?.Dispose();
            _d3d9?.Dispose();
            _ctx.Dispose();
            _device.Dispose();
        }
    }
}
