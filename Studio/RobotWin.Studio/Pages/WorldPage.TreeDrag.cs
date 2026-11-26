using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [reparent] Unity's drag in the Hierarchy: a row dropped on another row is mounted on it (a fixed mount where it
    /// stands), dropped on empty room it leaves its parent. Only a part that stands free or is fixed may move, so a servo
    /// horn, a shaft or a hinge keeps its mechanics; a part is never put under itself or under what rides on it.
    /// </summary>
    public partial class WorldPage
    {
        private const string TreeDragFormat = "robotwin-scene-row";
        private Point? _treeDragFrom;
        private string? _treeDragId;

        /// <summary>Makes a row draggable and a drop target.</summary>
        private void TreeRowDrag(FrameworkElement row, string? id)
        {
            if (id == null || World?.Find(id) == null) return;
            row.AllowDrop = true;
            Brush? before = null;   // the row's own look while a drag hovers it (Unity lights the row it would go under)
            void Restore()
            {
                if (before != null) row.SetValue(Panel.BackgroundProperty, before);
                before = null;
            }
            row.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _treeDragFrom = e.GetPosition(Palette);
                _treeDragId = id;
            };
            row.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _treeDragFrom is not { } from || _treeDragId != id) return;
                var now = e.GetPosition(Palette);
                if (Math.Abs(now.X - from.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - from.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _treeDragFrom = null;
                DragDrop.DoDragDrop(row, new DataObject(TreeDragFormat, id), DragDropEffects.Move);
            };
            row.DragOver += (_, e) =>
            {
                bool ok = e.Data.GetData(TreeDragFormat) is string moving && CanMount(moving, id);
                e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
                if (ok && before == null)
                {
                    before = row.GetValue(Panel.BackgroundProperty) as Brush ?? Brushes.Transparent;
                    row.SetValue(Panel.BackgroundProperty, (Brush)FindResource("B.AccentSoft"));
                }
                e.Handled = true;
            };
            row.DragLeave += (_, _) => Restore();
            row.Drop += (_, e) =>
            {
                Restore();
                if (e.Data.GetData(TreeDragFormat) is string moving) Reparent(moving, id);
                e.Handled = true;
            };
        }

        /// <summary>Drops on the tree's empty room take the part off its parent.</summary>
        private void TreeRootDrop()
        {
            Palette.AllowDrop = true;
            Palette.Background ??= Brushes.Transparent;
            Palette.DragOver -= OnTreeRootDragOver;
            Palette.DragOver += OnTreeRootDragOver;
            Palette.Drop -= OnTreeRootDrop;
            Palette.Drop += OnTreeRootDrop;
        }

        private void OnTreeRootDragOver(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            e.Effects = e.Data.GetData(TreeDragFormat) is string moving && ParentJoint(moving) is { Kind: WorldJointKind.Fixed } ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnTreeRootDrop(object sender, DragEventArgs e)
        {
            if (e.Handled || e.Data.GetData(TreeDragFormat) is not string moving) return;
            Reparent(moving, null);
            e.Handled = true;
        }

        private WorldJoint? ParentJoint(string id) =>
            World?.Joints.FirstOrDefault(j => string.Equals(j.ObjectA, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether <paramref name="moving"/> may go under <paramref name="parent"/>.</summary>
        private bool CanMount(string moving, string parent)
        {
            var world = World;
            if (world == null || string.Equals(moving, parent, StringComparison.OrdinalIgnoreCase)) return false;
            if (world.MountedOn(moving).Any(x => string.Equals(x.Id, parent, StringComparison.OrdinalIgnoreCase))) return false;   // not under its own rider
            var joint = ParentJoint(moving);
            if (joint != null && joint.Kind != WorldJointKind.Fixed) return false;   // a hinge or slider keeps its mechanics
            return joint == null || !string.Equals(joint.ObjectB, parent, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Mounts a part on another (or takes it off its parent when <paramref name="parent"/> is null), as one undo step.</summary>
        private void Reparent(string moving, string? parent)
        {
            var world = World;
            var o = world?.Find(moving);
            if (world == null || o == null) return;
            if (parent != null && !CanMount(moving, parent)) return;
            var joint = ParentJoint(moving);
            if (parent == null && joint is not { Kind: WorldJointKind.Fixed }) return;
            var target = parent != null ? world.Find(parent) : null;
            Edit(target != null ? $"{o.Name} → {target.Name} altına alındı" : $"{o.Name} üst nesnesinden ayrıldı", () =>
            {
                if (joint != null) world.Joints.Remove(joint);
                if (target != null) world.Joints.Add(MountOn(world, o, target));
            });
            BuildPalette();
        }
    }
}
