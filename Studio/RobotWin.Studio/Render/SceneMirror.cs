using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.Render
{
    /// <summary>A picture on a surface: the image, the part of the texture coordinates it covers, whether it repeats.</summary>
    internal readonly struct TextureMap
    {
        public TextureMap(ImageSource image, System.Windows.Rect uvRect, bool relative, bool tile)
        {
            Image = image;
            UvRect = uvRect;
            Relative = relative;
            Tile = tile;
        }

        public ImageSource Image { get; }

        /// <summary>The picture's viewport (a WPF brush's Viewport), relative to the texture coordinates' bounds or not.</summary>
        public System.Windows.Rect UvRect { get; }

        public bool Relative { get; }

        /// <summary>The picture repeats beyond its viewport; else its edge is drawn on.</summary>
        public bool Tile { get; }
    }

    /// <summary>A material as the renderer shades it: physically based (base colour, roughness, metalness, emission).</summary>
    internal readonly struct PbrMaterial
    {
        public PbrMaterial(Vector4 baseColor, TextureMap? albedo, Vector3 emissive, TextureMap? glow, Vector3 glowTint, float roughness, float metallic, bool unlit, Vector4 overlay)
        {
            BaseColor = baseColor;
            Albedo = albedo;
            Emissive = emissive;
            Glow = glow;
            GlowTint = glowTint;
            Roughness = roughness;
            Metallic = metallic;
            Unlit = unlit;
            Overlay = overlay;
        }

        public Vector4 BaseColor { get; }

        /// <summary>The base colour's picture (multiplies <see cref="BaseColor"/>).</summary>
        public TextureMap? Albedo { get; }

        public ImageSource? Texture => Albedo?.Image;
        public Vector3 Emissive { get; }

        /// <summary>A glowing picture (an emissive gradient or image), times <see cref="GlowTint"/>.</summary>
        public TextureMap? Glow { get; }

        public Vector3 GlowTint { get; }
        public float Roughness { get; }
        public float Metallic { get; }
        public bool Unlit { get; }

        /// <summary>The diffuse layers over the first one (WPF draws each over the last): colour (linear) and coverage.</summary>
        public Vector4 Overlay { get; }

        public bool Transparent => BaseColor.W + Overlay.W * (1 - BaseColor.W) < 0.995f;
    }

    /// <summary>One thing to draw: a mesh, where it is, what it is made of, and which of its sides show.</summary>
    internal readonly struct DrawItem
    {
        public DrawItem(MeshGeometry3D mesh, Matrix4x4 world, PbrMaterial material, bool back)
        {
            Mesh = mesh;
            World = world;
            Material = material;
            Back = back;
        }

        public MeshGeometry3D Mesh { get; }
        public Matrix4x4 World { get; }
        public PbrMaterial Material { get; }

        /// <summary>The mesh's back faces (WPF's BackMaterial): drawn with front faces culled instead.</summary>
        public bool Back { get; }
    }

    /// <summary>A directional light: the way its light travels, and its colour (radiance).</summary>
    internal readonly struct SunLight
    {
        public SunLight(Vector3 direction, Vector3 color)
        {
            Direction = direction;
            Color = color;
        }

        public Vector3 Direction { get; }
        public Vector3 Color { get; }
    }

    /// <summary>A light at a point (a lamp, an LED), or a spot: where it is, its colour, how it fades with distance.</summary>
    internal readonly struct Lamp
    {
        public Lamp(Vector3 position, Vector3 color, float range, Vector3 attenuation, Vector3 direction, float cosInner, float cosOuter, bool spot)
        {
            Position = position;
            Color = color;
            Range = range;
            Attenuation = attenuation;
            Direction = direction;
            CosInner = cosInner;
            CosOuter = cosOuter;
            Spot = spot;
        }

        public Vector3 Position { get; }
        public Vector3 Color { get; }
        public float Range { get; }

        /// <summary>Constant, linear and quadratic terms: the light falls off as 1 / (c + l·d + q·d²).</summary>
        public Vector3 Attenuation { get; }

        public Vector3 Direction { get; }
        public float CosInner { get; }
        public float CosOuter { get; }
        public bool Spot { get; }
    }

    /// <summary>
    /// [render] Reads a WPF 3D scene as it stands this frame: every mesh with its world transform and material, and the
    /// lights. The scene code goes on building WPF models (and picking against them); the GPU renderer draws what this
    /// finds. WPF's Lambert materials become physically based ones: a diffuse colour or picture is the base colour, a
    /// specular power becomes a roughness, a strong specular tint a metal, an emissive brush light of its own.
    /// </summary>
    internal sealed class SceneMirror
    {
        public readonly List<DrawItem> Items = new List<DrawItem>();
        public readonly List<SunLight> Suns = new List<SunLight>();
        public readonly List<Lamp> Lamps = new List<Lamp>();
        public Vector3 Ambient;

        public void Read(Viewport3D view)
        {
            Items.Clear();
            Suns.Clear();
            Lamps.Clear();
            Ambient = Vector3.Zero;
            foreach (var child in view.Children) Visit(child, Matrix3D.Identity);
        }

        private void Visit(Visual3D visual, Matrix3D parent)
        {
            var m = visual.Transform is { } t && !t.Value.IsIdentity ? t.Value * parent : parent;
            if (visual is ModelVisual3D mv)
            {
                if (mv.Content != null) Visit(mv.Content, m);
                foreach (var child in mv.Children) Visit(child, m);
            }
            else if (visual is ModelUIElement3D ui && ui.Model != null)
            {
                Visit(ui.Model, m);
            }
            else if (visual is ContainerUIElement3D container)
            {
                foreach (var child in container.Children) Visit(child, m);
            }
        }

        private void Visit(Model3D model, Matrix3D parent)
        {
            var m = model.Transform is { } t && !t.Value.IsIdentity ? t.Value * parent : parent;
            switch (model)
            {
                case Model3DGroup group:
                    if (FlatOf(group) is { } flat)
                    {
                        // [perf] its meshes as flattened when it last changed: no walk of WPF's tree, no material read
                        foreach (var e in flat.Meshes)
                        {
                            var w = ToNumerics(e.Local * m);
                            if (e.Front is { } fm) Items.Add(new DrawItem(e.Mesh, w, fm, false));
                            if (e.Back is { } bm) Items.Add(new DrawItem(e.Mesh, w, bm, true));
                        }
                    }
                    else
                    {
                        foreach (var child in group.Children) Visit(child, m);
                    }

                    break;
                case GeometryModel3D gm when gm.Geometry is MeshGeometry3D mesh && mesh.Positions.Count > 0:
                    var world = ToNumerics(m);
                    if (gm.Material != null && ConvertCached(gm.Material) is { } front) Items.Add(new DrawItem(mesh, world, front, false));
                    if (gm.BackMaterial != null && ConvertCached(gm.BackMaterial) is { } back) Items.Add(new DrawItem(mesh, world, back, true));
                    break;
                case AmbientLight a:
                    Ambient += Rgb(a.Color);
                    break;
                case DirectionalLight d:
                    var dir = m.Transform(d.Direction);
                    if (dir.LengthSquared > 1e-12) Suns.Add(new SunLight(Vector3.Normalize(new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z)), Rgb(d.Color)));
                    break;
                case PointLightBase p when p.Color.R + p.Color.G + p.Color.B > 0 && p.Range > 0:
                    var at = m.Transform(p.Position);
                    var aim = new Vector3D(0, 0, -1);
                    float cosIn = 1, cosOut = 1;
                    if (p is SpotLight s)
                    {
                        aim = m.Transform(s.Direction);
                        // WPF's cone angles are whole angles; the light is full inside the inner one and gone outside the outer.
                        double outer = Math.Clamp(s.OuterConeAngle, 0, 180), inner = Math.Clamp(Math.Min(s.InnerConeAngle, outer), 0, 180);
                        cosOut = (float)Math.Cos(outer * Math.PI / 360);
                        cosIn = (float)Math.Cos(inner * Math.PI / 360);
                    }

                    var aimN = aim.LengthSquared > 1e-12 ? Vector3.Normalize(new Vector3((float)aim.X, (float)aim.Y, (float)aim.Z)) : -Vector3.UnitZ;
                    Lamps.Add(new Lamp(new Vector3((float)at.X, (float)at.Y, (float)at.Z), Rgb(p.Color), (float)p.Range,
                        new Vector3((float)p.ConstantAttenuation, (float)p.LinearAttenuation, (float)p.QuadraticAttenuation), aimN, cosIn, cosOut, p is SpotLight));
                    break;
            }
        }

        /// <summary>[perf] A model group's meshes, relative to the group, with their materials converted; valid until it changes.</summary>
        private sealed class Flat
        {
            public bool Valid;
            public bool HasLights;
            public readonly List<(MeshGeometry3D Mesh, Matrix3D Local, PbrMaterial? Front, PbrMaterial? Back)> Meshes =
                new List<(MeshGeometry3D, Matrix3D, PbrMaterial?, PbrMaterial?)>();
        }

        private sealed class MaterialBox
        {
            public bool Valid;
            public PbrMaterial? Value;
        }

        private static readonly ConditionalWeakTable<Model3DGroup, Flat> Flats = new ConditionalWeakTable<Model3DGroup, Flat>();
        private static readonly ConditionalWeakTable<Material, MaterialBox> Materials = new ConditionalWeakTable<Material, MaterialBox>();

        /// <summary>
        /// [perf] A group flattened once and kept until WPF says it changed (Freezable.Changed: a child, a transform, a mesh's
        /// points, a material's brush); a frozen part model never does. A big scene's frame then costs a multiply per mesh
        /// instead of reading every dependency property of every model and material. Null when the group holds lights.
        /// </summary>
        private static Flat? FlatOf(Model3DGroup group)
        {
            if (Flats.TryGetValue(group, out var flat))
            {
                if (flat.Valid) return flat.HasLights ? null : flat;
            }
            else
            {
                flat = new Flat();
                Flats.AddOrUpdate(group, flat);
                if (!group.IsFrozen)
                {
                    var f = flat;
                    group.Changed += (_, _) => f.Valid = false;
                }
            }

            flat.Meshes.Clear();
            flat.HasLights = false;
            foreach (var child in group.Children) Flatten(child, Matrix3D.Identity, flat);
            flat.Valid = true;
            return flat.HasLights ? null : flat;
        }

        private static void Flatten(Model3D model, Matrix3D parent, Flat flat)
        {
            var m = model.Transform is { } t && !t.Value.IsIdentity ? t.Value * parent : parent;
            switch (model)
            {
                case Model3DGroup group:
                    foreach (var child in group.Children) Flatten(child, m, flat);
                    break;
                case GeometryModel3D gm when gm.Geometry is MeshGeometry3D mesh && mesh.Positions.Count > 0:
                    var front = gm.Material != null ? ConvertCached(gm.Material) : null;
                    var back = gm.BackMaterial != null ? ConvertCached(gm.BackMaterial) : null;
                    if (front != null || back != null) flat.Meshes.Add((mesh, m, front, back));
                    break;
                case Light:
                    flat.HasLights = true;
                    break;
            }
        }

        /// <summary>[perf] A material converted once and kept until it changes (its brush, colour or picture).</summary>
        private static PbrMaterial? ConvertCached(Material material)
        {
            if (Materials.TryGetValue(material, out var box))
            {
                if (box.Valid) return box.Value;
            }
            else
            {
                box = new MaterialBox();
                Materials.AddOrUpdate(material, box);
                if (!material.IsFrozen)
                {
                    var b = box;
                    material.Changed += (_, _) => b.Valid = false;
                }
            }

            box.Value = Convert(material);
            box.Valid = true;
            return box.Value;
        }

        public static Matrix4x4 ToNumerics(Matrix3D m) => new Matrix4x4(
            (float)m.M11, (float)m.M12, (float)m.M13, (float)m.M14,
            (float)m.M21, (float)m.M22, (float)m.M23, (float)m.M24,
            (float)m.M31, (float)m.M32, (float)m.M33, (float)m.M34,
            (float)m.OffsetX, (float)m.OffsetY, (float)m.OffsetZ, (float)m.M44);

        private static Vector3 Rgb(Color c) => new Vector3(SrgbToLinear(c.R), SrgbToLinear(c.G), SrgbToLinear(c.B));

        private static float SrgbToLinear(byte v)
        {
            double c = v / 255.0;
            return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }

        /// <summary>A WPF material as a physically based one; null when it draws nothing.</summary>
        private static PbrMaterial? Convert(Material material)
        {
            Vector4 baseColor = Vector4.Zero;
            TextureMap? albedo = null, glow = null;
            Vector3 emissive = Vector3.Zero, emissiveSrgb = Vector3.Zero, glowTint = Vector3.Zero;
            double specularPower = 0;
            Vector3 specular = Vector3.Zero;
            bool diffuse = false;
            Vector3 overColor = Vector3.Zero;
            float overAlpha = 0;
            void Take(Material mat)
            {
                switch (mat)
                {
                    case MaterialGroup g:
                        foreach (var child in g.Children) Take(child);
                        break;
                    case DiffuseMaterial d when d.Brush != null:
                        var (dc, dm) = BrushColor(d.Brush, d.Color);
                        if (!diffuse)
                        {
                            diffuse = true;
                            baseColor = dc;
                            albedo = dm;
                        }
                        else if (dc.W > 0)
                        {
                            // WPF draws each layer over the ones before it: an empty charring layer adds nothing, a
                            // half-burnt one darkens the body, picture and all.
                            float a = dc.W, cover = 1 - (1 - overAlpha) * (1 - a);
                            overColor = (overColor * overAlpha * (1 - a) + new Vector3(dc.X, dc.Y, dc.Z) * a) / cover;
                            overAlpha = cover;
                        }

                        break;
                    case SpecularMaterial s when s.Brush != null:
                        var (sc, _) = BrushColor(s.Brush, s.Color);
                        specular += new Vector3(sc.X, sc.Y, sc.Z) * sc.W;
                        specularPower = Math.Max(specularPower, s.SpecularPower);
                        break;
                    case EmissiveMaterial e when e.Brush != null:
                        var (ec, em) = BrushColor(e.Brush, e.Color);
                        if (em is { } map && glow == null)
                        {
                            // A picture that glows (a lit display, the sheen of a liquid, a blurred propeller disc).
                            glow = map;
                            glowTint = new Vector3(ec.X, ec.Y, ec.Z) * ec.W;
                            break;
                        }

                        emissive += new Vector3(ec.X, ec.Y, ec.Z) * ec.W;
                        if (e.Brush is SolidColorBrush es)
                            emissiveSrgb += new Vector3(es.Color.R, es.Color.G, es.Color.B) / 255f * (es.Color.A / 255f * (float)es.Opacity);
                        else
                            emissiveSrgb += new Vector3(LinearToSrgb(ec.X), LinearToSrgb(ec.Y), LinearToSrgb(ec.Z)) * ec.W;
                        break;
                }
            }

            Take(material);
            if (!diffuse && emissive == Vector3.Zero && glow == null) return null;
            // Nothing to see: a fully clear layer only (a pick target, an empty slot).
            if (diffuse && baseColor.W <= 0.002f && overAlpha <= 0.002f && emissive == Vector3.Zero && glow == null) return null;
            // Lit only by itself (a glow, a halo, the editor's handles): WPF adds its colour, as chosen, to what is behind.
            bool unlit = !diffuse;
            if (unlit)
            {
                if (glow != null)
                {
                    // Its picture is what it shows.
                    albedo = glow;
                    glow = null;
                    emissiveSrgb += new Vector3(LinearToSrgb(glowTint.X), LinearToSrgb(glowTint.Y), LinearToSrgb(glowTint.Z));
                }

                var s = Vector3.Clamp(emissiveSrgb, Vector3.Zero, Vector3.One);
                baseColor = new Vector4(SrgbToLinear((byte)Math.Round(s.X * 255)), SrgbToLinear((byte)Math.Round(s.Y * 255)), SrgbToLinear((byte)Math.Round(s.Z * 255)), 1);
            }

            // A shininess of n reads as a roughness of about √(2 / (n + 2)) (Blinn–Phong to GGX).
            float roughness = specularPower > 0 ? (float)Math.Clamp(Math.Sqrt(2 / (specularPower + 2)), 0.08, 0.95) : 0.8f;
            // A strong, tinted highlight is a metal's (it colours its reflection); a white one a dielectric's.
            float specLevel = Math.Max(specular.X, Math.Max(specular.Y, specular.Z));
            float tint = specLevel > 0 ? (specLevel - Math.Min(specular.X, Math.Min(specular.Y, specular.Z))) / specLevel : 0;
            float metallic = specLevel > 0.55f && specularPower >= 30 ? Math.Clamp(0.6f + tint, 0, 1) : 0;
            return new PbrMaterial(baseColor, albedo, unlit ? Vector3.Zero : emissive, glow, glowTint, roughness, metallic, unlit, new Vector4(overColor, overAlpha));
        }

        private static float LinearToSrgb(float c) => (float)(c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055);

        private static readonly ConditionalWeakTable<GradientBrush, BitmapSource> Rasters = new ConditionalWeakTable<GradientBrush, BitmapSource>();

        /// <summary>A frozen gradient drawn once into a picture over its own coordinate square (0..1 both ways).</summary>
        private static BitmapSource? Raster(GradientBrush g)
        {
            if (!g.IsFrozen) return null;   // one that changes every frame is drawn as its average colour instead
            if (Rasters.TryGetValue(g, out var cached)) return cached;
            const int size = 256;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size, size));
                dc.DrawRectangle(g, null, new System.Windows.Rect(0, 0, 1, 1));
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            Rasters.AddOrUpdate(g, bmp);
            return bmp;
        }

        /// <summary>A brush's colour in linear light, with its opacity in w, and its picture when it has one.</summary>
        private static (Vector4 Color, TextureMap? Map) BrushColor(Brush brush, Color tint)
        {
            float opacity = (float)brush.Opacity;
            var t = new Vector3(SrgbToLinear(tint.R), SrgbToLinear(tint.G), SrgbToLinear(tint.B));
            switch (brush)
            {
                case SolidColorBrush s:
                    var c = Rgb(s.Color) * t;
                    return (new Vector4(c, s.Color.A / 255f * opacity * tint.A / 255f), null);
                case ImageBrush ib when ib.ImageSource != null:
                    return (new Vector4(t, opacity * tint.A / 255f),
                        new TextureMap(ib.ImageSource, ib.Viewport, ib.ViewportUnits == BrushMappingMode.RelativeToBoundingBox, ib.TileMode != TileMode.None));
                case GradientBrush g when g.GradientStops.Count > 0:
                    if (Raster(g) is { } raster)
                    {
                        // The picture carries the brush's own opacity; the mapping follows the gradient's.
                        return (new Vector4(t, tint.A / 255f),
                            new TextureMap(raster, new System.Windows.Rect(0, 0, 1, 1), g.MappingMode == BrushMappingMode.RelativeToBoundingBox, g.SpreadMethod != GradientSpreadMethod.Pad));
                    }

                    var sum = Vector4.Zero;
                    foreach (var stop in g.GradientStops) sum += new Vector4(Rgb(stop.Color), stop.Color.A / 255f);
                    sum /= g.GradientStops.Count;
                    return (new Vector4(new Vector3(sum.X, sum.Y, sum.Z) * t, sum.W * opacity * tint.A / 255f), null);
                default:
                    return (new Vector4(t, opacity), null);
            }
        }
    }
}
