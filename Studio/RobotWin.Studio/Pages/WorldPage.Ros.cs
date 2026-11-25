using System;
using System.Windows;
using System.Windows.Controls;

namespace RobotWin.Studio.Pages
{
    /// <summary>[ros] Ortam → ROS 2 (rosbridge): the address, connect or disconnect, and how the link is doing.</summary>
    public partial class WorldPage
    {
        private string _rosAddress = "ws://localhost:9090";
        private TextBlock? _rosStatus;

        private void RosPalette()
        {
            Palette.Children.Add(Caption("ROS 2 (rosbridge)"));
            var ros = _session.Ros;
            bool on = ros != null && ros.Connected;
            if (!on)
            {
                var address = new TextBox
                {
                    Text = _rosAddress,
                    Margin = new Thickness(0, 0, 0, 6),
                    ToolTip = "rosbridge_server'ın adresi. ROS 2 tarafında: ros2 launch rosbridge_server rosbridge_websocket_launch.xml (port 9090)"
                };
                Palette.Children.Add(address);
                Palette.Children.Add(Action("ROS 2'ye bağlan", async () =>
                {
                    _rosAddress = address.Text.Trim();
                    if (!Uri.TryCreate(_rosAddress, UriKind.Absolute, out var uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                    {
                        W.Status = "Adres ws://bilgisayar:9090 biçiminde olmalı.";
                        return;
                    }

                    W.Status = "rosbridge'e bağlanılıyor: " + _rosAddress;
                    string? error = await _session.ConnectRos(uri);
                    W.Status = error == null ? "ROS 2'ye bağlandı: " + _rosAddress : "ROS 2'ye bağlanılamadı: " + error;
                    BuildEnvironmentPalette();
                }, "Çalışan dünya /clock ve /joint_states yayımlar, /joint_command ile gelen hedefleri eklem motorlarına verir"));
                if (ros?.Error is { } why) Palette.Children.Add(Note("Bağlantı koptu: " + why));
                return;
            }

            _rosStatus = Note(RosStatusText());
            Palette.Children.Add(_rosStatus);
            Palette.Children.Add(Action("Bağlantıyı kes", () =>
            {
                _session.DisconnectRos();
                W.Status = "ROS 2 bağlantısı kesildi.";
                BuildEnvironmentPalette();
            }));
        }

        /// <summary>The link at a glance: where, what goes out and in, joints ROS sees.</summary>
        private string RosStatusText()
        {
            var ros = _session.Ros;
            if (ros == null) return string.Empty;
            string state = ros.Connected ? "Bağlı: " + ros.Address : "Koptu: " + (ros.Error ?? ros.Address.ToString());
            string traffic = $"{ros.Sent} mesaj gitti, {ros.Received} geldi";
            var link = _session.RosLink;
            string joints = link == null
                ? "Dünya çalışınca /clock ve /joint_states yayımlanır, /joint_command dinlenir."
                : $"{link.JointNames.Count} eklem: {string.Join(", ", link.JointNames)}" + (link.UnknownNames.Count > 0 ? $" · bilinmeyen adlar: {string.Join(", ", link.UnknownNames)}" : string.Empty);
            return state + " · " + traffic + "\n" + joints;
        }
    }
}
