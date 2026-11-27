using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Editor
{
    /// <summary>Mouse and keyboard handling of the schematic canvas (KiCad Eeschema-like hotkeys).</summary>
    public sealed partial class SchematicCanvas
    {
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            _mouse = e.GetPosition(this);
            _mouseGrid = ToGrid(_mouse);
            var mods = Keyboard.Modifiers;
            bool shift = mods.HasFlag(ModifierKeys.Shift), ctrl = mods.HasFlag(ModifierKeys.Control);

            if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && _spaceDown))
            {
                CaptureMouse();
                _drag = DragMode.Pan;
                _dragStart = _mouse;
                _panStartOffset = _offset;
                if (_spaceDown) _spacePanned = true;
                Cursor = Cursors.SizeAll;
                return;
            }

            if (e.ChangedButton == MouseButton.Right)
            {
                if (_floating != null)
                {
                    CancelFloating();
                    return;
                }

                if (_keyMove)
                {
                    CancelKeyMove();
                    return;
                }

                if (_wirePoints != null)
                {
                    FinishWire();
                    return;
                }

                if (_placingLead != null)
                {
                    _placingLead = null;
                    Cursor = _tool == EditorTool.Select ? Cursors.Arrow : Cursors.Cross;
                    InvalidateVisual();
                    return;
                }

                if (_tool != EditorTool.Select)
                {
                    Tool = EditorTool.Select;
                    return;
                }

                var hit = HitObject(_mouseGrid);
                if (hit == null && HitPin(_mouseGrid) is { } pinHit) hit = pinHit.Part;
                if (hit != null && !_selection.Contains(hit))
                {
                    _selection.Clear();
                    _selection.Add(hit);
                    WholeGroup(hit, false);   // [group]
                    NotifySelection();
                }

                ShowContextMenu(hit);
                return;
            }

            if (e.ChangedButton != MouseButton.Left) return;

            if (_placingLead != null)
            {
                PlaceLead();
                return;
            }

            if (Simulating)
            {
                CaptureMouse();
                HandleSimulationClick(e);
                return;
            }

            if (_floating != null)
            {
                CommitFloating();
                return;
            }

            if (_keyMove)
            {
                CommitKeyMove();
                return;
            }

            switch (_tool)
            {
                case EditorTool.Place:
                    PlaceGhost();
                    return;
                case EditorTool.Wire:
                    WireClick(e.ClickCount >= 2);
                    return;
                case EditorTool.Label:
                case EditorTool.GlobalLabel:
                    PlaceLabel();
                    return;
                case EditorTool.NoConnect:
                    ToggleNoConnectAt(_mouseGrid);
                    return;
                case EditorTool.Highlight:   // [highlight] the net under the click, or off again
                    ToggleHighlightUnderCursor();
                    return;
                case EditorTool.Junction:   // [junction]
                    ToggleJunctionAt(_mouseGrid);
                    return;
                case EditorTool.Erase:   // [erase] whatever is under the click goes; the tool stays
                {
                    if (!GuardEditing()) return;
                    if (HitObject(_mouseGrid) is not { } hit)
                    {
                        W.Status = Shell.Lang.T("Silici: silinecek bir öğeye tıklayın");
                        return;
                    }

                    W.BeginChange();
                    SchematicEditing.Delete(Doc, new[] { hit });
                    _selection.Remove(hit);
                    W.CommitChange(Shell.Lang.T("Silindi"));
                    NotifySelection();
                    InvalidateVisual();
                    return;
                }
                case EditorTool.Line:   // [shapes]
                case EditorTool.Rectangle:
                case EditorTool.Circle:
                case EditorTool.Arc:
                    ShapeClick(e.ClickCount >= 2);
                    return;
                case EditorTool.Text:   // [notes]
                    RequestCommand(SchematicCommand.EditNote, Snap(_mouseGrid));
                    Tool = EditorTool.Select;
                    return;
            }

            if (_wirePoints != null)
            {
                // A wire started from a pin with the select tool: keep routing.
                WireClick(e.ClickCount >= 2);
                return;
            }

            if (e.ClickCount >= 2)
            {
                if (ValueTextAt(_mouse) is { } valuePart)
                {
                    RequestCommand(SchematicCommand.EditValue, valuePart);
                    return;
                }

                switch (HitObject(_mouseGrid))
                {
                    case SchematicPart dp:
                        PartDoubleClicked?.Invoke(this, dp);
                        RequestCommand(SchematicCommand.EditProperties, dp);
                        return;
                    case NetLabel dl:
                        RequestCommand(SchematicCommand.EditValue, dl);
                        return;
                    case SchematicNote dn:   // [notes]
                        RequestCommand(SchematicCommand.EditNote, dn);
                        return;
                    case SchematicWire:
                        if (NetUnderCursor() is { } net) SelectConnection(net);
                        return;
                }
            }

            // Select tool: clicking a pin tip starts a wire directly.
            var pin = HitPin(_mouseGrid);
            if (pin != null && e.ClickCount == 1 && !ctrl && !shift)
            {
                _wirePoints = new System.Collections.Generic.List<GridPoint> { pin.Value.Part.Transform(pin.Value.Pin.X, pin.Value.Pin.Y) };
                InvalidateVisual();
                return;
            }

            CaptureMouse();
            var target = HitObject(_mouseGrid);
            if (target == null)
            {
                _boxToggle = ctrl;
                _boxAdd = shift;
                if (!ctrl && !shift) _selection.Clear();
                _drag = DragMode.Box;
                _dragStart = _mouse;
                NotifySelection();
                return;
            }

            if (ctrl)
            {
                if (!_selection.Remove(target)) _selection.Add(target);
            }
            else if (shift)
            {
                _selection.Add(target);
            }
            else if (!_selection.Contains(target))
            {
                _selection.Clear();
                _selection.Add(target);
            }

            WholeGroup(target, ctrl);   // [group] Editor/SchematicCanvas.Groups.cs
            NotifySelection();
            if (!_selection.Contains(target)) return;

            // Press on a selected item: dragging moves the selection (or one wire segment).
            _drag = DragMode.Move;
            _dragStart = _mouse;
            _dragStartGrid = Snap(_mouseGrid);
            _moved = false;
            _pressedWire = target as SchematicWire;
            _pressedSegment = _pressedWire != null ? SchematicEditing.HitSegment(_pressedWire, _mouseGrid, Math.Max(0.35, 6 / _scale)) : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _mouse = e.GetPosition(this);
            var oldSnap = Snap(_mouseGrid);
            _mouseGrid = ToGrid(_mouse);
            MouseGridChanged?.Invoke(this, _mouseGrid);
            bool snapMoved = !Snap(_mouseGrid).Equals(oldSnap);

            switch (_drag)
            {
                case DragMode.Pan:
                    _offset = _panStartOffset + (_mouse - _dragStart);
                    InvalidateVisual();
                    return;
                case DragMode.Move:
                case DragMode.Segment:
                    UpdateMouseDrag();
                    return;
                case DragMode.Box:
                    InvalidateVisual();
                    return;
            }

            if (_keyMove)
            {
                UpdateKeyMove();
                return;
            }

            if (_floating != null)
            {
                var at = Snap(_mouseGrid);
                if (!at.Equals(_floating.Anchor))
                {
                    _floating.MoveAnchorTo(at);
                    InvalidateVisual();
                }

                return;
            }

            if (_tool == EditorTool.Place && _ghost != null)
            {
                _ghost.Position = Snap(_mouseGrid);
                InvalidateVisual();
                return;
            }

            if (_shapeDraft != null)   // [shapes] the preview follows the cursor
            {
                if (snapMoved) InvalidateVisual();
                return;
            }

            var oldHover = _hover;
            var oldPin = _hoverPin;
            if (_placingLead != null)
            {
                _hover = HitObject(_mouseGrid);
                _hoverPin = null;
                InvalidateVisual();
                return;
            }

            _hoverPin = Simulating ? null : HitPin(_mouseGrid);
            _hover = _hoverPin != null ? null : HitObject(_mouseGrid);
            if (!Equals(oldHover, _hover) || !Equals(oldPin, _hoverPin) || _wirePoints != null || Simulating || _crosshair || snapMoved)
            {
                InvalidateVisual();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            var mode = _drag;
            _drag = DragMode.None;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Cursor = _tool == EditorTool.Select && _placingLead == null ? Cursors.Arrow : Cursors.Cross;

            if (mode == DragMode.Interact && _pressedButton != null)
            {
                Sim.SetSwitch(_pressedButton.RefDes, false);
                _pressedButton = null;
            }
            else if (mode == DragMode.Move || mode == DragMode.Segment)
            {
                EndMouseDrag();
            }
            else if (mode == DragMode.Box)
            {
                ApplyBoxSelection();
            }

            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            _hover = null;
            _hoverPin = null;
            InvalidateVisual();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                _spaceDown = false;
                if (!_spacePanned && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // KiCad: Space resets the relative coordinate origin to the cursor.
                    var at = Snap(_mouseGrid);
                    _relOrigin = _relOrigin is { } o && o.Equals(at) ? null : at;
                    W.Status = _relOrigin != null ? $"Göreli başlangıç: ({at.X:0}, {at.Y:0}) — HUD'da dX/dY" : "Göreli başlangıç kaldırıldı";
                    InvalidateVisual();
                }

                e.Handled = true;
            }
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            switch (e.Text)
            {
                case "/":
                    if (_wireMode == WireMode.Free) break;
                    _alternatePosture = !_alternatePosture;
                    W.Status = "Kablo köşesi: " + WireModeText;
                    InvalidateVisual();
                    break;
                case "?":
                    RequestCommand(SchematicCommand.Help);
                    break;
                case "+":
                case "=":
                    ZoomBy(1.25, IsMouseOver ? _mouse : null);
                    break;
                case "-":
                    ZoomBy(1 / 1.25, IsMouseOver ? _mouse : null);
                    break;
                default:
                    base.OnTextInput(e);
                    return;
            }

            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (mods.HasFlag(ModifierKeys.Alt)) return;
            // [keys] a key the user moved a tool to becomes the tool's own key; the tool's old key does nothing
            if (!Shell.Shortcuts.Translate(Shell.PageShortcuts.Schematic, ref key, ref mods))
            {
                e.Handled = true;
                return;
            }

            bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
            if (HandleKey(key, ctrl, shift, e.IsRepeat)) e.Handled = true;
        }

        /// <summary>Runs a hotkey; returns false when the key is not a schematic shortcut.</summary>
        public bool HandleKey(Key key, bool ctrl, bool shift, bool repeat = false)
        {
            switch (key)
            {
                // ---------------------------------------------------- modes
                case Key.Escape:
                    if (_placingLead != null)
                    {
                        _placingLead = null;
                        Cursor = _tool == EditorTool.Select ? Cursors.Arrow : Cursors.Cross;
                    }
                    else if (_floating != null) CancelFloating();
                    else if (_keyMove) CancelKeyMove();
                    else if (_wirePoints != null) FinishWire();
                    else if (_shapeDraft != null) FinishShape();   // [shapes] a line ends here; an unfinished rectangle, circle or arc is dropped
                    else if (_tool != EditorTool.Select) Tool = EditorTool.Select;
                    else if (_selection.Count > 0) ClearSelection();
                    else if (_netHighlight != null) HighlightedNet = null;
                    else if (_ercMarkers != null) ErcMarkers = null;
                    InvalidateVisual();
                    return true;
                case Key.Space:
                    if (shift && !ctrl)
                    {
                        WireMode = _wireMode switch { WireMode.Orthogonal => WireMode.Diagonal45, WireMode.Diagonal45 => WireMode.Free, _ => WireMode.Orthogonal };
                        W.Status = "Kablo modu: " + WireModeText;
                        return true;
                    }

                    if (!repeat)
                    {
                        _spaceDown = true;
                        _spacePanned = false;
                    }

                    return true;
                case Key.Enter when _floating != null:
                    CommitFloating();
                    return true;
                case Key.Enter when _keyMove:
                    CommitKeyMove();
                    return true;
                case Key.Enter when _shapeDraft != null:   // [shapes]
                    FinishShape();
                    return true;
                case Key.Enter when _wirePoints != null:
                    FinishWire();
                    return true;
                case Key.F1:
                    RequestCommand(SchematicCommand.Help);
                    return true;
                case Key.OemQuestion when shift && !ctrl:
                    RequestCommand(SchematicCommand.Help);
                    return true;

                // ---------------------------------------------------- view
                case Key.F when ctrl:
                    RequestCommand(SchematicCommand.Find);
                    return true;
                case Key.H when ctrl:
                    RequestCommand(SchematicCommand.Replace);
                    return true;
                case Key.F:
                case Key.Home:
                    FitToContent();
                    return true;
                case Key.Add:
                case Key.OemPlus when ctrl:
                    ZoomBy(1.25, IsMouseOver ? _mouse : null);
                    return true;
                case Key.Subtract:
                case Key.OemMinus when ctrl:
                    ZoomBy(1 / 1.25, IsMouseOver ? _mouse : null);
                    return true;
                case Key.Oem3 when !ctrl:
                    ToggleHighlightUnderCursor();
                    return true;
                case Key.X when ctrl && shift:
                    Crosshair = !Crosshair;
                    W.Status = Crosshair ? "Artı imleç açık" : "Artı imleç kapalı";
                    return true;
                case Key.Tab when !ctrl:
                    CyclePart(shift ? -1 : 1);
                    return true;
                case Key.P when ctrl && !shift:
                    RequestCommand(SchematicCommand.Print);
                    return true;
                case Key.C when ctrl:
                    CopySelection();
                    return true;
                case Key.A when ctrl:
                    SelectAll();
                    return true;
            }

            if (Simulating)
            {
                // Editing keys are ignored while simulating (the instruments use the canvas).
                bool editKey = key is Key.Delete or Key.Back or Key.R or Key.X or Key.Y or Key.M or Key.G or Key.W or Key.L or Key.P or Key.Q or Key.J or Key.A or Key.C or Key.V or Key.E or Key.D
                    or Key.Left or Key.Right or Key.Up or Key.Down || (ctrl && key is Key.Z or Key.Y or Key.X or Key.V or Key.D);
                if (editKey) W.Status = "Düzenlemek için önce simülasyonu durdurun (Shift+F5).";
                return editKey;
            }

            switch (key)
            {
                // ---------------------------------------------------- undo / clipboard
                case Key.Z when ctrl && shift:
                case Key.Y when ctrl:
                    if (_keyMove) CancelKeyMove();
                    W.Redo();
                    return true;
                case Key.Z when ctrl:
                    if (_keyMove) CancelKeyMove();
                    else if (_floating != null) CancelFloating();
                    else W.Undo();
                    return true;
                case Key.X when ctrl:
                    CutSelection();
                    return true;
                case Key.V when ctrl:
                    Paste();
                    return true;
                case Key.D when ctrl:
                case Key.C when !ctrl:
                    DuplicateSelection();
                    return true;
                case Key.G when ctrl && shift:   // [group]
                    UngroupSelection();
                    return true;
                case Key.G when ctrl:
                    GroupSelection();
                    return true;
                case Key.L when ctrl:
                    Tool = EditorTool.GlobalLabel;
                    return true;

                // ---------------------------------------------------- editing
                case Key.Back when _wirePoints != null:
                    if (_wirePoints.Count > 1) _wirePoints.RemoveAt(_wirePoints.Count - 1);
                    else _wirePoints = null;
                    InvalidateVisual();
                    return true;
                case Key.Delete:
                case Key.Back:
                    DeleteSelection();
                    return true;
                case Key.R when !ctrl:
                    RotateSelection(shift);
                    return true;
                case Key.X when !ctrl:
                    MirrorSelection(false);
                    return true;
                case Key.Y when !ctrl:
                    MirrorSelection(true);
                    return true;
                case Key.M when !ctrl:
                    BeginKeyMove(false);
                    return true;
                case Key.G when !ctrl:
                    BeginKeyMove(true);
                    return true;
                case Key.E when !ctrl:
                    RequestCommand(SchematicCommand.EditProperties, PartForCommand());
                    return true;
                case Key.V when !ctrl:
                case Key.F2:
                    {
                        object? t = _selection.Count == 1 ? _selection.First() : HitObject(_mouseGrid);
                        if (t is SchematicPart || t is NetLabel) RequestCommand(SchematicCommand.EditValue, t);
                        return true;
                    }

                case Key.D when !ctrl:
                    RequestCommand(SchematicCommand.Datasheet, PartForCommand());
                    return true;
                case Key.U when !ctrl:
                    if (NetUnderCursor() is { } net) SelectConnection(net);
                    return true;

                // ---------------------------------------------------- tools
                case Key.A when !ctrl:
                    RequestCommand(SchematicCommand.AddPart);
                    return true;
                case Key.P when !ctrl:
                    RequestCommand(SchematicCommand.AddPower);
                    return true;
                case Key.W when !ctrl:
                    if (shift) BeginWireAtCursor();
                    else Tool = EditorTool.Wire;
                    return true;
                case Key.L when !ctrl:
                    Tool = EditorTool.Label;
                    return true;
                case Key.T when !ctrl:   // [notes] KiCad's text tool
                    Tool = EditorTool.Text;
                    return true;
                case Key.Q when !ctrl:
                    if (IsMouseOver && HitPin(_mouseGrid) != null) ToggleNoConnectAt(_mouseGrid);
                    else Tool = EditorTool.NoConnect;
                    return true;
                case Key.J when !ctrl:   // [junction] KiCad: a junction where the pointer is
                    if (IsMouseOver) ToggleJunctionAt(_mouseGrid);
                    else Tool = EditorTool.Junction;
                    return true;

                // ---------------------------------------------------- nudge
                case Key.Left:
                case Key.Right:
                case Key.Up:
                case Key.Down:
                    {
                        double step = shift ? 10 : 1;
                        double dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
                        double dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
                        if (_selection.Count > 0) Nudge(dx, dy);
                        else
                        {
                            _offset += new Vector(-dx * _scale * 2, -dy * _scale * 2);
                            InvalidateVisual();
                        }

                        return true;
                    }
            }

            return false;
        }

        private SchematicPart? PartForCommand()
        {
            if (_selection.OfType<SchematicPart>().FirstOrDefault() is { } p) return p;
            return HitObject(_mouseGrid) as SchematicPart ?? HitPin(_mouseGrid)?.Part;
        }
    }
}
