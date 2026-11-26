using System.Linq;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [trigger] Unity's Is Trigger as a component: the part becomes a zone nothing bumps into that knows what is inside it;
    /// comings and goings go to the log, and while running the card lists what is inside. Switched off, it is a solid part
    /// again. CoreSim Sandbox/WorldPhysics.Triggers.cs.
    /// </summary>
    public partial class WorldPage
    {
        private TextBlock? _triggerLive;
        private string? _triggerLiveId;

        private void TriggerSection(WorldObject o)
        {
            _triggerLive = null;
            _triggerLiveId = null;
            const string key = WorldPhysics.TriggerKey;
            if (!WorldComponents.Has(o, key)) return;   // else under "Bileşen ekle"
            void Remove() => o.Parameters.Remove(key);
            if (OffCard(o, key, "Tetik bölgesi", "Tetik bölgesi kaldırıldı", Remove)) return;
            Inspector.Children.Add(FeatureHeader("Tetik bölgesi", "Tetik bölgesi kaldırıldı", Remove, toggle: key));
            Inspector.Children.Add(Note("Hiçbir şey ona çarpmaz, içinden geçer; giren ve çıkan parçalar olaylara yazılır. Bir parçaya sabitlenirse onunla gider (tutucunun erişimi, bandın ucu)."));
            _triggerLive = new TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new System.Windows.Thickness(0, 2, 0, 6) };
            _triggerLive.SetResourceReference(StyleProperty, "Text.Caption");
            _triggerLiveId = o.Id;
            Inspector.Children.Add(_triggerLive);
            UpdateTriggerLive();
        }

        private void UpdateTriggerLive()
        {
            if (_triggerLive == null || _triggerLiveId == null) return;
            var physics = _session.State == SimState.Stopped ? null : _session.Physics;
            if (physics == null)
            {
                _triggerLive.Text = "İçindekiler çalışırken burada görünür.";
                return;
            }

            var inside = physics.TriggerOccupants(_triggerLiveId).Select(id => World?.Find(id)?.Name ?? id).ToList();
            _triggerLive.Text = inside.Count == 0 ? "İçinde parça yok." : "İçinde: " + string.Join(", ", inside);
        }
    }
}
