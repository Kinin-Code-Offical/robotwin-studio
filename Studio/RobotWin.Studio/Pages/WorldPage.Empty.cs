using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [empty] Unity's Create Empty, Create Empty Child and Create Empty Parent for Montaj and Dünya. An empty is a
    /// named place: parts mounted on it move and turn with it in the editor (a group), and the run passes them on to
    /// whatever the empty is mounted on (WorldDocument.ForRun), so it never changes the physics.
    /// </summary>
    public partial class WorldPage
    {
        private WorldObject NewEmpty(WorldDocument world, string name, WorldPose pose) => new WorldObject
        {
            Id = world.NextId("O"),
            Name = Shell.Lang.T(name),
            Kind = WorldObjectKind.Empty,
            Shape = "empty",
            SizeX = 0.02,
            SizeY = 0.02,
            SizeZ = 0.02,
            Pose = pose
        };

        private static WorldJoint MountOn(WorldDocument world, WorldObject child, WorldObject parent) => new WorldJoint
        {
            Id = world.NextId("J"),
            Kind = WorldJointKind.Fixed,
            ObjectA = child.Id,
            ObjectB = parent.Id,
            AnchorX = child.Pose.X,
            AnchorY = child.Pose.Y,
            AnchorZ = child.Pose.Z
        };

        /// <summary>Ctrl+Alt+N (Unity: Ctrl+Shift+N, here New world): an empty where new objects land.</summary>
        private void CreateEmpty()
        {
            EnsureWorld();
            var world = World!;
            WorldObject? added = null;
            Edit("Boş nesne oluşturuldu", () =>
            {
                added = NewEmpty(world, "Boş nesne", NewPose());
                world.Objects.Add(added);
            });
            if (added != null) Scene.Select(added.Id);
        }

        /// <summary>Alt+Shift+N: an empty at the selected part's place, mounted on it.</summary>
        private void CreateEmptyChild()
        {
            var world = World;
            if (world?.Find(Scene.Selected) is not { } parent)
            {
                CreateEmpty();
                return;
            }

            WorldObject? added = null;
            Edit($"{parent.Name}: boş alt nesne oluşturuldu", () =>
            {
                added = NewEmpty(world, "Boş nesne", parent.Pose.Clone());
                world.Objects.Add(added);
                world.Joints.Add(MountOn(world, added, parent));
            });
            if (added != null) Scene.Select(added.Id);
        }

        /// <summary>
        /// Ctrl+Shift+G: an empty in the middle of the selection, the selection put under it. A part already mounted on
        /// something (a servo horn, a shaft, another part) keeps its mounting: only the free ones move under the empty,
        /// so the mechanics do not change. When every selected part hangs on the same parent, the empty hangs there too.
        /// </summary>
        private void CreateEmptyParent()
        {
            var world = World;
            if (world == null) return;
            var picked = SelectionIds().Select(id => world.Find(id)).Where(o => o != null).Cast<WorldObject>().ToList();
            if (picked.Count == 0)
            {
                CreateEmpty();
                return;
            }

            WorldJoint? ParentJoint(WorldObject o) => world.Joints.FirstOrDefault(j => string.Equals(j.ObjectA, o.Id, StringComparison.OrdinalIgnoreCase));
            var free = picked.Where(o => ParentJoint(o) == null).ToList();
            var parents = picked.Select(o => ParentJoint(o)?.ObjectB).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // all under one (non-ground) parent, fixed to it: they move under the empty, the empty takes their place on the parent
            var commonParent = parents.Count == 1 && parents[0] != null && picked.All(o => ParentJoint(o)?.Kind == WorldJointKind.Fixed) ? world.Find(parents[0]) : null;
            var moving = commonParent != null ? picked : free;
            if (moving.Count == 0)
            {
                W.Status = Shell.Lang.T("Seçili parçaların hepsi bir şeye takılı: boş üst nesne mekaniği değiştirmesin diye oluşturulmadı");
                return;
            }

            double x = moving.Average(o => o.Pose.X), y = moving.Average(o => o.Pose.Y), z = moving.Min(o => o.Pose.Z);
            WorldObject? added = null;
            Edit($"{moving.Count} nesne boş üst nesneye alındı", () =>
            {
                added = NewEmpty(world, "Grup", new WorldPose { X = x, Y = y, Z = z });
                world.Objects.Add(added);
                foreach (var o in moving)
                {
                    if (commonParent != null) world.Joints.RemoveAll(j => string.Equals(j.ObjectA, o.Id, StringComparison.OrdinalIgnoreCase));
                    world.Joints.Add(MountOn(world, o, added));
                }

                if (commonParent != null) world.Joints.Add(MountOn(world, added, commonParent));
            });
            if (added != null) Scene.Select(added.Id);
        }

        /// <summary>The inspector of an empty: its place (above) and what it holds; nothing physical to set.</summary>
        private void EmptySection(WorldDocument world, WorldObject o)
        {
            var kids = world.MountedOn(o.Id);
            Inspector.Children.Add(Caption("Boş nesne"));
            Inspector.Children.Add(Note(kids.Count == 0
                ? "Şekli, kütlesi ve devresi yok. Bir yer ya da grup olarak kullanın: sahne ağacında parçaları onun altına alın (sağ tık → Oluştur → Boş üst nesne) ya da alt nesne ekleyin."
                : $"{kids.Count} nesne bunun altında: düzenleyicide onunla taşınır ve döner. Çalıştırmada boş nesne yoktur; altındakiler onun takılı olduğu yere takılı sayılır, boşta duruyorsa serbesttir."));
            Inspector.Children.Add(Action("Boş alt nesne oluştur (Alt+Shift+N)", CreateEmptyChild));
        }

        private void EmptyMenuItems(System.Windows.Controls.MenuItem create)
        {
            create.Items.Insert(0, MenuEntry("Boş nesne", "Ctrl+Alt+N", CreateEmpty));
            create.Items.Insert(1, MenuEntry("Boş alt nesne", "Alt+Shift+N", CreateEmptyChild, Scene.Selected != null));
            create.Items.Insert(2, MenuEntry("Boş üst nesne (seçileni altına al)", "Ctrl+Shift+G", CreateEmptyParent, Scene.Selected != null));
            create.Items.Insert(3, new System.Windows.Controls.Separator());
        }
    }
}
