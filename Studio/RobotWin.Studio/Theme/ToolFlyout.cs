using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RobotWin.Studio.Theme
{
    /// <summary>[flyout] One tool of a group: its tag (what the page switches on), name, icon resource and keys.</summary>
    public sealed record ToolChoice(string Tag, string Label, string Icon, string? Keys = null);

    /// <summary>
    /// [flyout] A tool button standing for a group of tools, as Blender's and KiCad's: a small triangle in its corner;
    /// pressing and holding it, or a right click, lists the group's tools with their names and keys; the one picked
    /// becomes the button's face and is switched on. A short click uses the tool on its face.
    /// </summary>
    public static class ToolFlyout
    {
        private sealed class State
        {
            public IReadOnlyList<ToolChoice> Choices = Array.Empty<ToolChoice>();
            public Icon Face = null!;
            public DispatcherTimer Hold = null!;
            public bool Swallow;
            public Popup? Popup;
        }

        private static readonly ConditionalWeakTable<ButtonBase, State> States = new ConditionalWeakTable<ButtonBase, State>();

        /// <summary>Makes a tool button a group; its face is the first choice until another is picked.</summary>
        public static void Attach(ButtonBase button, IReadOnlyList<ToolChoice> choices, double iconSize = 16)
        {
            var face = new Icon { Size = iconSize };
            var corner = new Path
            {
                Data = Geometry.Parse("M4,0 L4,4 L0,4 Z"), Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -6, -6), IsHitTestVisible = false
            };
            corner.SetResourceReference(Shape.FillProperty, "B.TextMuted");
            var grid = new Grid();
            grid.Children.Add(face);
            grid.Children.Add(corner);
            button.Content = grid;
            var state = new State { Choices = choices, Face = face, Hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) } };
            States.AddOrUpdate(button, state);
            Show(button, choices[0]);

            state.Hold.Tick += (_, _) =>
            {
                state.Hold.Stop();
                state.Swallow = true;
                Open(button, state);
            };
            button.PreviewMouseLeftButtonDown += (_, _) =>
            {
                state.Swallow = false;
                state.Hold.Start();
            };
            button.PreviewMouseLeftButtonUp += (_, e) =>
            {
                state.Hold.Stop();
                if (state.Swallow)
                {
                    e.Handled = true;   // the hold opened the list: no click on release
                    state.Swallow = false;
                }
            };
            button.MouseLeave += (_, _) => state.Hold.Stop();
            button.PreviewMouseRightButtonUp += (_, e) =>
            {
                Open(button, state);
                e.Handled = true;
            };
            string tip = button.ToolTip as string ?? string.Empty;
            button.ToolTip = (tip.Length > 0 ? tip + "\n" : string.Empty) + "Basılı tutun ya da sağ tıklayın: gruptaki diğer araçlar";
        }

        /// <summary>The group's face follows a tool picked elsewhere (its key, a menu); false when the tag is not in the group.</summary>
        public static bool Select(ButtonBase button, string tag)
        {
            if (!States.TryGetValue(button, out var state)) return false;
            var choice = state.Choices.FirstOrDefault(c => c.Tag == tag);
            if (choice == null) return false;
            Show(button, choice);
            return true;
        }

        public static bool Contains(ButtonBase button, string tag) => States.TryGetValue(button, out var s) && s.Choices.Any(c => c.Tag == tag);

        private static void Show(ButtonBase button, ToolChoice c)
        {
            if (!States.TryGetValue(button, out var state)) return;
            button.Tag = c.Tag;
            state.Face.Data = (Geometry)button.FindResource(c.Icon);
            AutomationName(button, c.Label);
        }

        private static void AutomationName(ButtonBase b, string name) => System.Windows.Automation.AutomationProperties.SetName(b, name);

        private static void Open(ButtonBase button, State state)
        {
            state.Popup ??= new Popup { PlacementTarget = button, Placement = PlacementMode.Right, StaysOpen = false, AllowsTransparency = true, HorizontalOffset = 4 };
            var list = new StackPanel { MinWidth = 200 };
            foreach (var c in state.Choices)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icon = new Icon { Data = (Geometry)button.FindResource(c.Icon), Size = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                row.Children.Add(icon);
                var label = new TextBlock { Text = c.Label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                if (c.Keys != null)
                {
                    var keys = new TextBlock { Text = c.Keys, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(16, 0, 0, 0) };
                    keys.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
                    Grid.SetColumn(keys, 2);
                    row.Children.Add(keys);
                }

                var item = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5, 10, 5), Margin = new Thickness(0, 0, 0, 1) };
                item.SetResourceReference(FrameworkElement.StyleProperty, "Btn.Ghost");
                if (Equals(button.Tag, c.Tag)) item.SetResourceReference(Control.BackgroundProperty, "B.Pressed");
                var choice = c;
                item.Click += (_, _) =>
                {
                    state.Popup!.IsOpen = false;
                    Show(button, choice);
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                };
                list.Children.Add(item);
            }

            var card = new Border { Child = list, Padding = new Thickness(4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            card.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            card.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            state.Popup.Child = card;
            state.Popup.IsOpen = true;
        }
    }
}
