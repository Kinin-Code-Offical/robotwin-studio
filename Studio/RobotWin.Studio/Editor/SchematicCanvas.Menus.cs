using System;
using System.Linq;
using System.Windows.Controls;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Editor
{
    /// <summary>Right-click menus of the schematic canvas: one per kind of item, with the hotkeys shown.</summary>
    public sealed partial class SchematicCanvas
    {
        private void ShowContextMenu(object? hit)
        {
            var menu = BuildContextMenu(hit);
            menu.PlacementTarget = this;
            menu.Closed += (_, _) => Focus();
            menu.IsOpen = true;
        }

        /// <summary>Builds the menu for the item under the cursor (also used by --snapshot to draw it inline).</summary>
        public ContextMenu BuildContextMenu(object? hit)
        {
            var menu = new ContextMenu();
            var at = _mouseGrid;
            var pin = HitPin(at);
            var (hitNet, hitAt) = NetAt(at);
            bool editable = !Simulating;

            MenuItem Item(string header, string gesture, Action action, bool enabled = true)
            {
                var mi = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
                mi.Click += (_, _) => action();
                menu.Items.Add(mi);
                return mi;
            }

            void Sep()
            {
                if (menu.Items.Count > 0 && menu.Items[^1] is not Separator) menu.Items.Add(new Separator());
            }

            string Check(bool on, string text) => (on ? "✓  " : "     ") + text;

            // Instruments: scope channels and meter leads on any net (also before the run starts).
            if (hitNet != null && (hit is SchematicWire || hit is NetLabel || pin != null))
            {
                for (int ch = 0; ch < 4; ch++)
                {
                    int c = ch;
                    Item($"{hitNet} → osiloskop CH{ch + 1}", ch == 0 ? "Tık" : "", () =>
                    {
                        if (!Simulating) W.Status = "Prob takıldı; simülasyon başlayınca ölçer.";
                        Sim.AddVoltageProbe(hitNet, c, hitAt);
                    });
                }

                Sep();
                Item($"Multimetre kırmızı ucu → {hitNet}", "", () => Sim.SetMeterLead(true, hitNet, hitAt));
                Item($"Multimetre siyah ucu (COM) → {hitNet}", "", () => Sim.SetMeterLead(false, hitNet, hitAt));
                Sep();
                Item(_netHighlight == hitNet ? "Bağlantı vurgusunu kaldır" : $"Bağlantıyı vurgula ({hitNet})", "`", () =>
                    HighlightedNet = string.Equals(_netHighlight, hitNet, StringComparison.OrdinalIgnoreCase) ? null : hitNet);
                Item("Bağlantıyı seç", "U", () => SelectConnection(hitNet), editable);
                Sep();
            }

            if (Simulating)
            {
                if (hit is SchematicPart part)
                {
                    Item($"{part.RefDes} akımını osiloskoba ekle", "Ctrl+Tık", () => Sim.AddCurrentProbe(part.RefDes, null, PartCenter(part)));
                    if (Sim.IsPartFailed(part.RefDes)) Item($"{part.RefDes} parçasını onar (yenisiyle değiştir)", "", () => Sim.RepairPart(part.RefDes));
                    Item("PCB'de göster", "", () => RequestCommand(SchematicCommand.ShowInPcb, part));
                }

                Item("Hasarlı parçaları onar", "", Sim.RepairAll);
                Sep();
                Item("Kopyala", "Ctrl+C", CopySelection, _selection.Count > 0);
                Item("Sığdır", "F", FitToContent);
                Item("Bul…", "Ctrl+F", () => RequestCommand(SchematicCommand.Find));
                return menu;
            }

            int n = _selection.Count;
            string many = n > 1 ? $" ({n} öğe)" : string.Empty;
            switch (hit)
            {
                case SchematicPart part:
                    {
                        var def = Lib.Find(part.PartId);
                        bool power = def?.Symbol is SymbolKind.Ground or SymbolKind.PowerRail;
                        Item(n > 1 ? $"Özellikler ({n} parça)…" : $"{part.RefDes} özellikleri…", "E", () => RequestCommand(SchematicCommand.EditProperties, part));
                        Item(power ? "Hat adını düzenle" : "Değer düzenle", "V", () => RequestCommand(SchematicCommand.EditValue, part), def != null && def.ValueKey.Length > 0);
                        Item("Footprint seç…", "", () => RequestCommand(SchematicCommand.ChooseFootprint, part), !power);
                        Item("Datasheet", "D", () => RequestCommand(SchematicCommand.Datasheet, part), !power);
                        Sep();
                        Item("Döndür" + many, "R", () => RotateSelection(false));
                        Item("Saat yönünde döndür", "Shift+R", () => RotateSelection(true));
                        Item("Yatay aynala", "X", () => MirrorSelection(false));
                        Item("Dikey aynala", "Y", () => MirrorSelection(true));
                        Item("Taşı", "M", () => BeginKeyMove(false));
                        Item("Sürükle (kablolar uzar)", "G", () => BeginKeyMove(true));
                        Sep();
                        Item("Kopyala", "Ctrl+C", CopySelection);
                        Item("Kes", "Ctrl+X", CutSelection);
                        Item("Çoğalt", "Ctrl+D", DuplicateSelection);
                        Item("Sil" + many, "Del", DeleteSelection);
                        Sep();
                        if (def != null && def.Pins.Count == 2 && !power)
                        {
                            Item("Simülasyonda ölç: akım probu ekle", "", () =>
                            {
                                Sim.AddCurrentProbe(part.RefDes, null, PartCenter(part));
                                W.Status = $"{part.RefDes} akım probu takıldı; simülasyon başlayınca ölçer.";
                            });
                        }

                        if (pin != null) Item($"{part.RefDes}.{pin.Value.Pin.Name}: NC işareti", "Q", () => ToggleNoConnectAt(at));
                        Item("PCB'de göster", "", () => RequestCommand(SchematicCommand.ShowInPcb, part), !power);
                        break;
                    }

                case SchematicWire wire:
                    Item("Kabloyu böl", "", () => BreakWireAt(wire, at));
                    Item("Bu segmenti sil", "", () => DeleteSegmentAt(wire, at));
                    Item("Etiket ekle", "L", () => AddLabelAt(WireSnap(at), false));
                    Item("Global etiket ekle", "Ctrl+L", () => AddLabelAt(WireSnap(at), true));
                    Item("Buradan kablo çiz", "Shift+W", () =>
                    {
                        Tool = EditorTool.Wire;
                        _wirePoints = new System.Collections.Generic.List<GridPoint> { WireSnap(at) };
                        InvalidateVisual();
                    });
                    Sep();
                    Item("Sürükle", "G", () => BeginKeyMove(true));
                    Item("Kopyala", "Ctrl+C", CopySelection);
                    Item("Kes", "Ctrl+X", CutSelection);
                    Item("Sil" + many, "Del", DeleteSelection);
                    break;

                case NetLabel label:
                    Item("Yeniden adlandır", "V", () => RequestCommand(SchematicCommand.EditValue, label));
                    Item(label.Global ? "Yerel etikete çevir" : "Global etikete çevir", "", () =>
                    {
                        W.BeginChange();
                        label.Global = !label.Global;
                        W.CommitChange(label.Global ? "Global etiket" : "Yerel etiket");
                    });
                    Sep();
                    Item("Taşı", "M", () => BeginKeyMove(false));
                    Item("Kopyala", "Ctrl+C", CopySelection);
                    Item("Çoğalt", "Ctrl+D", DuplicateSelection);
                    Item("Sil" + many, "Del", DeleteSelection);
                    break;

                case SchematicShape { Kind: SheetShapeKind.Table } table:   // [table]
                    Item("Tabloyu düzenle…", "E", () => RequestCommand(SchematicCommand.EditTable, table));
                    Item("Taşı", "M", () => BeginKeyMove(false));
                    Item("Kopyala", "Ctrl+C", CopySelection);
                    Item("Sil" + many, "Del", DeleteSelection);
                    break;

                case NoConnectFlag:
                    Item("NC işaretini sil", "Del", DeleteSelection);
                    Item("Taşı", "M", () => BeginKeyMove(false));
                    break;

                default:
                    {
                        var clip = ReadClipboard();
                        Item(clip != null ? $"Yapıştır ({clip.Count} öğe)" : "Yapıştır", "Ctrl+V", Paste, clip != null);
                        Sep();
                        Item("Parça ekle…", "A", () => RequestCommand(SchematicCommand.AddPart));
                        Item("Güç sembolü ekle…", "P", () => RequestCommand(SchematicCommand.AddPower));
                        Item("Kablo", "W", () => Tool = EditorTool.Wire);
                        Item("Etiket", "L", () => AddLabelAt(WireSnap(at), false));
                        Item("Global etiket", "Ctrl+L", () => AddLabelAt(WireSnap(at), true));
                        Item("NC işareti", "Q", () => Tool = EditorTool.NoConnect);
                        Item("Bağlantı noktası", "J", () => ToggleJunctionAt(at));   // [junction]
                        Sep();
                        Item("Tümünü seç", "Ctrl+A", SelectAll);
                        Item("Sığdır", "F / Home", FitToContent);
                        Item(Check(_gridStyle == GridStyle.Dots, "Izgara: noktalar"), "", () => GridStyle = GridStyle.Dots);
                        Item(Check(_gridStyle == GridStyle.Lines, "Izgara: çizgiler"), "", () => GridStyle = GridStyle.Lines);
                        Item(Check(_gridStyle == GridStyle.None, "Izgara: kapalı"), "", () => GridStyle = GridStyle.None);
                        Item(Check(_crosshair, "Artı imleç"), "Ctrl+Shift+X", () => Crosshair = !Crosshair);
                        // [units] KiCad's units: the corner's coordinates in grid steps, mm, mil or inches
                        Item("Birim: " + Units switch { "mm" => "mm", "mil" => "mil", "in" => "inç", _ => "ızgara adımı" }, "", () =>
                        {
                            Units = Units switch { "grid" => "mm", "mm" => "mil", "mil" => "in", _ => "grid" };
                            Shell.AppSettings.Current.SchematicUnits = Units;
                            Shell.AppSettings.Current.Save();
                            InvalidateVisual();
                        });
                        Item("Kablo modu: " + (_wireMode == WireMode.Orthogonal ? "90°" : _wireMode == WireMode.Diagonal45 ? "45°" : "serbest"), "Shift+Boşluk", () =>
                            WireMode = _wireMode switch { WireMode.Orthogonal => WireMode.Diagonal45, WireMode.Diagonal45 => WireMode.Free, _ => WireMode.Orthogonal });
                        Sep();
                        Item("Numaralandır…", "", () => RequestCommand(SchematicCommand.Annotate));
                        Item("Alan tablosu…", "", () => RequestCommand(SchematicCommand.FieldsTable));   // [fields]
                        Item("Elektriksel kural denetimi (ERC)", "", () => RequestCommand(SchematicCommand.Erc));
                        Item("Bul / değiştir…", "Ctrl+F", () => RequestCommand(SchematicCommand.Find));
                        Item("Multimetre uçlarını yerleştir", "", () => BeginLeadPlacement(true));
                        Item("Kısayollar", "F1", () => RequestCommand(SchematicCommand.Help));
                        break;
                    }
            }

            // [group] Editor/SchematicCanvas.Groups.cs
            bool grouped = _selection.Any(o => SchematicGroups.GroupOf(o) != null);
            if (hit != null && (n >= 2 || grouped))
            {
                Sep();
                if (n >= 2) Item("Grupla", "Ctrl+G", GroupSelection);
                if (grouped) Item("Grubu çöz", "Ctrl+Shift+G", UngroupSelection);
            }

            return menu;
        }

        /// <summary>For --snapshot: moves the cursor to a sheet point and returns the menu for what is there.</summary>
        public ContextMenu BuildContextMenuAt(GridPoint at)
        {
            _mouseGrid = at;
            _mouse = ToScreen(at);
            var hit = HitObject(at) ?? HitPin(at)?.Part;
            if (hit != null)
            {
                _selection.Clear();
                _selection.Add(hit);
                NotifySelection();
            }

            return BuildContextMenu(hit);
        }

        /// <summary>For --snapshot scripts: what a left click does while something follows the cursor.</summary>
        public void CommitPending()
        {
            if (_keyMove)
            {
                UpdateKeyMove();
                CommitKeyMove();
            }
            else if (_floating != null)
            {
                _floating.MoveAnchorTo(Snap(_mouseGrid));
                CommitFloating();
            }
        }

        /// <summary>For --snapshot: shows a box selection from a to b in progress.</summary>
        public void SimulateBox(GridPoint a, GridPoint b)
        {
            _dragStart = ToScreen(a);
            _mouse = ToScreen(b);
            _mouseGrid = b;
            _drag = DragMode.Box;
            InvalidateVisual();
        }

        /// <summary>For --snapshot: puts the cursor at a sheet point (hover, HUD, wire preview).</summary>
        public void SimulateCursor(GridPoint at)
        {
            _mouseGrid = at;
            _mouse = ToScreen(at);
            _hoverPin = HitPin(at);
            _hover = _hoverPin != null ? null : HitObject(at);
            InvalidateVisual();
        }

        /// <summary>For --snapshot: a wire being drawn through the given points (last = cursor).</summary>
        public void SimulateWire(WireMode mode, params GridPoint[] points)
        {
            Tool = EditorTool.Wire;
            _wireMode = mode;
            _wirePoints = points.Take(points.Length - 1).ToList();
            SimulateCursor(points[^1]);
        }
    }
}
