using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [components] Unity's component ⋮: Copy Component, Paste Component Values and Paste Component As New, for the
    /// components a part carries as parameters (WorldComponents: shell, pressure vessel, parachute, fault) and for the
    /// Fizik card (fixed, material, infill, mass, damping, frozen axes). Kept while the app runs, across worlds.
    /// </summary>
    public partial class WorldPage
    {
        private static (string Key, Dictionary<string, string> Values, string Title)? _copiedComponent;
        private static (bool Static, string? Material, double Infill, double? Mass, Dictionary<string, string> Rigidbody)? _copiedPhysics;

        private static readonly string[] RigidbodyKeys = { "rb.damping", "rb.angdamping", "rb.lockpos", "rb.lockrot" };

        private void ComponentClipboardItems(ContextMenu menu, string title, ComponentRemoval? removal)
        {
            var o = _inspected;
            if (o == null || World == null) return;
            int before = menu.Items.Count;
            if (removal?.Toggle is { } key)
            {
                menu.Items.Add(MenuEntry("Bileşeni kopyala", null, () => _copiedComponent = (key, WorldComponents.Copy(o, key), title)));
                var copied = _copiedComponent;
                menu.Items.Add(MenuEntry("Değerleri yapıştır", null, () =>
                    Edit(Shell.Lang.T($"{title}: değerler yapıştırıldı"), () => WorldComponents.Paste(o, key, copied!.Value.Values)), copied?.Key == key));
            }
            else if (title == "Fizik")
            {
                menu.Items.Add(MenuEntry("Değerleri kopyala", null, () =>
                    _copiedPhysics = (o.IsStatic, o.Material, o.Infill, o.MassKg, RigidbodyKeys.Where(o.Parameters.ContainsKey).ToDictionary(k => k, k => o.Parameters[k]))));
                var physics = _copiedPhysics;
                menu.Items.Add(MenuEntry("Değerleri yapıştır", null, () => Edit(Shell.Lang.T($"{o.Name}: fizik değerleri yapıştırıldı"), () =>
                {
                    var p = physics!.Value;
                    o.IsStatic = p.Static;
                    if (o.Kind is WorldObjectKind.Body or WorldObjectKind.Model)
                    {
                        o.Material = p.Material ?? o.Material;
                        o.Infill = p.Infill;
                    }

                    o.MassKg = p.Mass;
                    foreach (var k in RigidbodyKeys) o.Parameters.Remove(k);
                    foreach (var kv in p.Rigidbody) o.Parameters[kv.Key] = kv.Value;
                }), physics != null));
            }

            // Paste As New: from any card, when this part can carry the copied component and has none yet
            if (_copiedComponent is { } c && !WorldComponents.Has(o, c.Key) && WorldComponents.CanCarry(o, c.Key, c.Values))
            {
                menu.Items.Add(MenuEntry(Shell.Lang.T($"Yeni bileşen olarak yapıştır: {c.Title}"), null, () =>
                {
                    var known = FeatureTitles();
                    Edit(Shell.Lang.T($"{c.Title} yapıştırıldı"), () => WorldComponents.Paste(o, c.Key, c.Values));
                    BuildInspector();
                    RevealNewComponent(known);
                }));
            }

            if (menu.Items.Count > before) menu.Items.Add(new Separator());
        }
    }
}
