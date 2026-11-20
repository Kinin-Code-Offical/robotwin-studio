using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sdk;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>[sdk] "Eklentiler ve betikler" card of the settings page. SettingsPage.xaml.cs only calls SdkCard().</summary>
    public partial class SettingsPage
    {
        private void SdkCard()
        {
            SdkServices.Init();
            var ps = SdkServices.Settings;
            Card("Eklentiler ve betikler", "Kendi fizik ve davranış kodunuz: eklenti DLL'leri, dünya betikleri (C#) ve ifadeler. Eklenti kodu uygulamanın tüm yetkileriyle çalışır; yalnızca güvendiğiniz eklentileri kurun.", c =>
            {
                c.Children.Add(Text("Ek eklenti klasörleri (; ile ayırın)", string.Join(";", ps.ExtraFolders), v =>
                {
                    ps.ExtraFolders = v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                    ps.Save();
                }, "Her zaman taranan klasör: " + PluginHost.UserPluginDirectory));
                c.Children.Add(Toggle("Dünya dosyasının yanındaki 'Plugins' klasörünü yükle", ps.LoadWorldPlugins, v =>
                {
                    ps.LoadWorldPlugins = v;
                    ps.Save();
                }, "Bir dünyayla birlikte dağıtılan eklentiler. Yalnızca güvendiğiniz dünyalarda açık bırakın."));
                c.Children.Add(Toggle("Değişen betikleri çalıştırırken otomatik derle", ps.AutoCompileScripts, v =>
                {
                    ps.AutoCompileScripts = v;
                    ps.Save();
                }, ScriptCompiler.Detect().Explanation));

                var list = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                void Fill()
                {
                    list.Children.Clear();
                    var plugins = PluginHost.Shared.Plugins.Where(p => p.Source == "user" || p.Source == "world").ToList();
                    var disabledOnly = ps.Disabled.Where(id => plugins.All(p => !string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (plugins.Count == 0 && disabledOnly.Count == 0)
                    {
                        list.Children.Add(new TextBlock { Text = "Yüklü eklenti yok. Şablon: sdk/templates/RobotWinPlugin (docs/SDK.md).", Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap });
                    }

                    foreach (var p in plugins)
                    {
                        var plugin = p;
                        bool off = ps.Disabled.Contains(plugin.Id, StringComparer.OrdinalIgnoreCase);
                        string state = plugin.Error != null ? "hata: " + plugin.Error : plugin.IsLoaded ? $"{plugin.Types.Count} tür, {plugin.PartIds.Count} parça" : "yüklü değil";
                        var cb = new CheckBox
                        {
                            Content = $"{plugin.Manifest.Name} {plugin.Manifest.Version} — {plugin.Manifest.Author} ({state})",
                            IsChecked = !off, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 8), ToolTip = plugin.Manifest.Directory
                        };
                        cb.Click += (_, _) =>
                        {
                            SdkServices.SetDisabled(plugin.Id, cb.IsChecked != true);
                            Fill();
                        };
                        list.Children.Add(cb);
                    }

                    foreach (var id in disabledOnly)
                    {
                        var pid = id;
                        var cb = new CheckBox { Content = pid + " (kapalı)", IsChecked = false, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 8) };
                        cb.Click += (_, _) =>
                        {
                            SdkServices.SetDisabled(pid, false);
                            Fill();
                        };
                        list.Children.Add(cb);
                    }
                }

                Fill();
                c.Children.Add(list);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var reload = new Button { Content = "Eklentileri yeniden yükle" };
                reload.Click += (_, _) =>
                {
                    SdkServices.ReloadAll();
                    Fill();
                    Workspace.Current.Status = "Eklentiler yeniden yüklendi (çalışan dünyalar durdurulmalı)";
                };
                var open = new Button { Content = "Eklenti klasörünü aç", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(8, 0, 0, 0) };
                open.Click += (_, _) =>
                {
                    try
                    {
                        Directory.CreateDirectory(PluginHost.UserPluginDirectory);
                        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + PluginHost.UserPluginDirectory + "\"") { UseShellExecute = true });
                    }
                    catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException)
                    {
                        Workspace.Current.Status = "Klasör açılamadı: " + ex.Message;
                    }
                };
                row.Children.Add(reload);
                row.Children.Add(open);
                c.Children.Add(row);
            });
        }
    }
}
