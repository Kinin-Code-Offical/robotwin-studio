using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[urdf] Robot descriptions (URDF, xacro) into a world: parts, joints with limits and drives, meshes from ROS packages.</summary>
    public class UrdfImportTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rw-urdf-" + Guid.NewGuid().ToString("N"));

        public UrdfImportTests(ITestOutputHelper output)
        {
            _out = output;
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }

        private const string TwoAxisArm = @"<robot name='iki_eksen'>
  <material name='mavi'><color rgba='0 0 1 1'/></material>
  <link name='base_link'>
    <inertial><mass value='2'/></inertial>
    <visual><origin xyz='0 0 0.05'/><geometry><box size='0.2 0.2 0.1'/></geometry><material name='mavi'/></visual>
  </link>
  <link name='upper'>
    <inertial><mass value='0.5'/></inertial>
    <visual><origin xyz='0 0 0.15'/><geometry><cylinder radius='0.02' length='0.3'/></geometry></visual>
  </link>
  <link name='fore'>
    <inertial><mass value='0.3'/></inertial>
    <visual><origin xyz='0.1 0 0'/><geometry><box size='0.2 0.03 0.03'/></geometry></visual>
  </link>
  <link name='tool0'/>
  <joint name='shoulder' type='revolute'><parent link='base_link'/><child link='upper'/><origin xyz='0 0 0.1'/><axis xyz='0 0 1'/>
    <limit lower='-1.57' upper='1.57' effort='20' velocity='2'/></joint>
  <joint name='elbow' type='revolute'><parent link='upper'/><child link='fore'/><origin xyz='0 0 0.3'/><axis xyz='0 1 0'/>
    <limit lower='-2' upper='2' effort='10' velocity='3'/></joint>
  <joint name='tool' type='fixed'><parent link='fore'/><child link='tool0'/><origin xyz='0.2 0 0'/></joint>
</robot>";

        private string Write(string name, string text)
        {
            string path = Path.Combine(_dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        private static WorldJoint JointTo(WorldDocument w, string childName) => w.Joints.Single(j => w.Find(j.ObjectA)!.Name == childName);

        [Fact]
        public void AnArmBecomesPartsHingesWithLimitsAndDrives()
        {
            var world = new WorldDocument();
            var r = UrdfImport.Import(Write("arm.urdf", TwoAxisArm), world);
            _out.WriteLine(r.Summary);
            Assert.Equal(new[] { "base_link", "upper", "fore" }, world.Objects.Select(o => o.Name));   // tool0 folds into fore
            Assert.True(r.FixedBase);
            Assert.True(world.Objects[0].IsStatic);
            Assert.Equal("#0000FF", world.Objects[0].Color);
            Assert.Equal(2.0, world.Objects[0].MassKg);

            var shoulder = JointTo(world, "upper");
            Assert.Equal(WorldJointKind.Hinge, shoulder.Kind);
            Assert.Equal(world.Objects[0].Id, shoulder.ObjectB);
            Assert.Equal(0.1, shoulder.AnchorZ, 9);
            Assert.Equal(1.0, shoulder.AxisZ, 9);
            Assert.Equal(-1.57 * 180 / Math.PI, shoulder.Lower!.Value, 6);
            Assert.Equal(WorldJoint.DrivePosition, shoulder.Drive);
            Assert.Equal(20, shoulder.DriveForce);
            Assert.Equal(2 * 180 / Math.PI, shoulder.DriveSpeed, 6);

            var elbow = JointTo(world, "fore");
            Assert.Equal(0.4, elbow.AnchorZ, 9);
            Assert.Equal(1.0, elbow.AxisY, 9);

            // The world rests a model on its lowest point, centred: the part's pose makes up for it, so the forearm's box
            // (centred 0.1 m along the link's X, the link 0.4 m up) is where the URDF puts it.
            var fore = world.Objects[2];
            Assert.Equal(0.1, fore.Pose.X, 9);
            Assert.Equal(0.4 - 0.015, fore.Pose.Z, 9);
        }

        [Fact]
        public void ThePlacementMovesAndTurnsTheWholeRobot()
        {
            var world = new WorldDocument();
            UrdfImport.Import(Write("arm.urdf", TwoAxisArm), world, new UrdfImportOptions { Placement = new WorldPose { X = 1, Y = 2, Yaw = 90 } });
            var elbow = JointTo(world, "fore");
            Assert.Equal(1.0, elbow.AnchorX, 9);
            Assert.Equal(2.0, elbow.AnchorY, 9);
            Assert.Equal(0.4, elbow.AnchorZ, 9);
            Assert.Equal(-1.0, elbow.AxisX, 9);   // +Y turned 90° about Z
            Assert.Equal(0.0, elbow.AxisY, 9);
        }

        [Fact]
        public void XacroPropertiesMacrosBlocksIncludesConditionsAndArgsExpand()
        {
            Write("parts.xacro", @"<robot xmlns:xacro='http://www.ros.org/wiki/xacro'>
  <xacro:macro name='segment' params='name len:=0.1 *joint_origin'>
    <link name='${name}'><visual><origin xyz='0 0 ${len/2}'/><geometry><box size='0.02 0.02 ${len}'/></geometry></visual></link>
    <joint name='${name}_joint' type='revolute'><parent link='base'/><child link='${name}'/><xacro:insert_block name='joint_origin'/>
      <axis xyz='0 0 1'/><limit lower='${-pi/2}' upper='${radians(90)}' effort='5' velocity='1'/></joint>
  </xacro:macro>
</robot>");
            string main = Write("robot.urdf.xacro", @"<robot name='x' xmlns:xacro='http://www.ros.org/wiki/xacro'>
  <xacro:arg name='with_tool' default='true'/>
  <xacro:property name='L' value='0.25'/>
  <xacro:property name='twice' value='${L * 2}'/>
  <xacro:include filename='parts.xacro'/>
  <link name='base'><visual><geometry><box size='0.1 0.1 0.02'/></geometry></visual></link>
  <xacro:segment name='a' len='${L}'><origin xyz='0 0 ${twice - 0.3}'/></xacro:segment>
  <xacro:segment name='b'><origin xyz='0.1 0 0'/></xacro:segment>
  <xacro:if value='$(arg with_tool)'><link name='tool'><visual><geometry><sphere radius='0.01'/></geometry></visual></link></xacro:if>
  <xacro:unless value='${L &gt; 1}'><link name='small'/></xacro:unless>
</robot>");
            var robot = UrdfRobot.Parse(Xacro.Expand(main, null, out var notes));
            foreach (var n in notes) _out.WriteLine(n);
            Assert.Equal(new[] { "base", "a", "b", "tool", "small" }, robot.Links.Select(l => l.Name));
            var ja = robot.Joints.Single(j => j.Name == "a_joint");
            Assert.Equal(0.2, ja.Origin.Xyz.Z, 12);
            Assert.Equal(-Math.PI / 2, ja.Lower!.Value, 12);
            Assert.Equal(Math.PI / 2, ja.Upper!.Value, 12);
            Assert.Equal(0.25, robot.Link("a")!.Visuals[0].Geometry.Size.Z, 12);
            Assert.Equal(0.1, robot.Link("b")!.Visuals[0].Geometry.Size.Z, 12);   // the parameter's default
            Assert.Equal(0.1, robot.Joints.Single(j => j.Name == "b_joint").Origin.Xyz.X, 12);

            var without = UrdfRobot.Parse(Xacro.Expand(main, new Dictionary<string, string> { ["with_tool"] = "false" }, out _));
            Assert.DoesNotContain(without.Links, l => l.Name == "tool");
        }

        [Fact]
        public void PackageMeshesAreFoundAndReadInMetresAndAMissingOneFallsBackToItsCollisionShape()
        {
            string pkg = Path.Combine(_dir, "ws", "src", "demo_description");
            Write(Path.Combine(pkg, "package.xml"), "<package><name>demo_description</name></package>");
            // A 50 mm cube as an ASCII STL, in metres as URDF meshes are.
            var cube = MeshModel.CreateBox(new Vec3(0.05, 0.05, 0.05));
            var stl = new System.Text.StringBuilder("solid c\n");
            for (int t = 0; t < cube.TriangleCount; t++)
            {
                var (a, b, c) = cube.Triangle(t);
                stl.Append("facet normal 0 0 0\nouter loop\n");
                foreach (var v in new[] { a, b, c }) stl.Append(FormattableString.Invariant($"vertex {v.X} {v.Y} {v.Z}\n"));
                stl.Append("endloop\nendfacet\n");
            }

            stl.Append("endsolid c\n");
            Write(Path.Combine(pkg, "meshes", "cube.stl"), stl.ToString());
            string urdf = Write(Path.Combine(pkg, "urdf", "demo.urdf"), @"<robot name='demo'>
  <link name='base'><visual><geometry><mesh filename='package://demo_description/meshes/cube.stl' scale='2 2 2'/></geometry></visual></link>
  <link name='arm'>
    <visual><geometry><mesh filename='package://demo_description/meshes/missing.dae'/></geometry></visual>
    <collision><geometry><box size='0.3 0.02 0.02'/></geometry></collision>
  </link>
  <joint name='j' type='continuous'><parent link='base'/><child link='arm'/><origin xyz='0 0 0.1'/><axis xyz='0 0 1'/></joint>
</robot>");
            var world = new WorldDocument();
            var r = UrdfImport.Import(urdf, world);
            foreach (var n in r.Notes) _out.WriteLine(n);
            var baseMesh = MeshModel.FromEmbedded(world.Objects[0].Mesh!);
            Assert.Equal(0.1, baseMesh.Bounds.Max.X - baseMesh.Bounds.Min.X, 6);   // 50 mm × 2, read as metres
            var armMesh = MeshModel.FromEmbedded(world.Objects[1].Mesh!);
            Assert.Equal(0.3, armMesh.Bounds.Max.X - armMesh.Bounds.Min.X, 6);   // its collision box
            Assert.Contains(r.Notes, n => n.Contains("missing.dae"));
            Assert.Equal(WorldJoint.DriveSpeedMode, world.Joints.Single().Drive);
        }

        [Fact]
        public void TheFourAxisXacroArmImportsWithAWorldLinkBoltingItsBase()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rw_arm4.urdf.xacro");
            var world = new WorldDocument();
            var r = UrdfImport.Import(path, world);
            foreach (var n in r.Notes) _out.WriteLine(n);
            _out.WriteLine(r.Summary);
            Assert.Empty(r.Notes);
            Assert.Equal(new[] { "base_link", "turret", "upper_arm", "forearm", "gripper" }, world.Objects.Select(o => o.Name));   // no ball for "world"
            Assert.True(r.FixedBase);
            Assert.True(world.Objects[0].IsStatic);
            Assert.Equal(4, r.MovingJoints);
            Assert.All(world.Joints, j => Assert.Equal(WorldJoint.DrivePosition, j.Drive));
            Assert.Equal(-170, world.Joints[0].Lower!.Value, 6);
            // The elbow's frame is turned 90° about Y: its Y axis stays Y; the forearm points along +X from 0.32 m up.
            var elbow = JointTo(world, "forearm");
            Assert.Equal(0.06 + 0.04 + 0.22, elbow.AnchorZ, 9);
            Assert.Equal(1.0, elbow.AxisY, 9);
            var wrist = JointTo(world, "gripper");
            Assert.Equal(0.18, wrist.AnchorX, 9);
            Assert.Equal(1.0, wrist.AxisX, 9);
            // Two materials on one link: the arm orange, its hub graphite.
            var upper = MeshModel.FromEmbedded(world.Objects[2].Mesh!);
            Assert.Equal(2, upper.TriangleColors!.Distinct().Count());
        }

        [Fact]
        public void ADriveIsSavedWithTheWorldAndAJointWithoutOneWritesNone()
        {
            var world = new WorldDocument();
            UrdfImport.Import(Write("arm.urdf", TwoAxisArm), world);
            string elbowId = JointTo(world, "fore").Id;
            world.Joints.Add(new WorldJoint { Id = "J99", Kind = WorldJointKind.Hinge, ObjectA = world.Objects[1].Id });
            string json = world.ToJson();
            var back = WorldDocument.FromJson(json);
            var elbow = back.Joints.Single(j => j.Id == elbowId);
            Assert.Equal(WorldJoint.DrivePosition, elbow.Drive);
            Assert.Equal(10, elbow.DriveForce);
            Assert.True(elbow.Driven);
            var plain = back.Joints.Single(j => j.Id == "J99");
            Assert.Null(plain.Drive);
            Assert.False(plain.Driven);
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(json, "\"drive\"").Count);   // the two motors only
        }

        [Fact]
        public void InverseKinematicsSolvesTheMotorisedAxesAndThePhysicsTakesTheTipThere()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rw_arm4.urdf.xacro");
            var world = new WorldDocument();
            UrdfImport.Import(path, world);
            var geometry = new ApproximateGeometry(world, Design.PartLibrary.Default);
            var tip = world.Objects.Single(o => o.Name == "gripper");
            var arm = Robotics.WorldArm.Build(world, geometry.GeometryOf, tip.Id)!;
            Assert.Equal(4, arm.ActuatedCount);   // every motorised joint is an axis inverse kinematics may set
            Assert.All(arm.Axes, a => Assert.True(a.Motor));

            // A point the arm can reach (from a pose inside its limits), solved from the pose as built.
            var target = arm.Tip(new[] { 0.6, 0.4, -0.7, 0.0 });
            var s = arm.Solve(target, new double[4]);
            _out.WriteLine($"çözüm: {string.Join(", ", s.Q.Select(q => (q * 180 / Math.PI).ToString("0.0")))}°, hata {s.Error * 1000:0.000} mm");
            Assert.True(s.Success);
            Assert.True(s.Error < 1e-4);

            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false }, geometry);
            var physics = runner.Physics!;
            for (int i = 0; i < arm.Axes.Count; i++) Assert.True(physics.SetJointTarget(arm.Axes[i].JointId!, s.Q[i] * 180 / Math.PI));
            runner.Step(3.0);
            var measured = arm.MeasuredTip(physics)!.Value;
            double miss = (measured - target).Length;
            _out.WriteLine($"fizikte uç hedeften {miss * 1000:0.00} mm uzakta");
            Assert.True(miss < 0.003, $"{miss * 1000:0.00} mm");
        }

        [Fact]
        public void ARobotOnWheelsKeepsAFreeBase()
        {
            var world = new WorldDocument();
            var r = UrdfImport.Import(Write("rover.urdf", @"<robot name='rover'>
  <link name='chassis'><visual><geometry><box size='0.3 0.2 0.05'/></geometry></visual></link>
  <link name='left_wheel'><visual><geometry><cylinder radius='0.05' length='0.02'/></geometry></visual></link>
  <joint name='lw' type='continuous'><parent link='chassis'/><child link='left_wheel'/><origin xyz='0 0.12 0' rpy='1.5708 0 0'/><axis xyz='0 0 1'/></joint>
</robot>"), world);
            Assert.False(r.FixedBase);
            Assert.False(world.Objects[0].IsStatic);
        }

        [Fact]
        public void ImportedArmHoldsItsPoseUnderGravityAndGoesWhereItIsSent()
        {
            var world = new WorldDocument();
            UrdfImport.Import(Write("arm.urdf", TwoAxisArm), world, new UrdfImportOptions { Placement = new WorldPose { Z = 0.001 } });
            var elbow = JointTo(world, "fore");
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            runner.Step(1.0);
            var physics = runner.Physics!;
            var held = physics.JointDriveState(elbow.Id)!.Value;
            _out.WriteLine($"tutuyor: dirsek {held.Now:F2}°");
            Assert.InRange(held.Now, -1, 1);   // a horizontal forearm on a 10 N·m drive does not sag

            Assert.True(physics.SetJointTarget(elbow.Id, 30));
            runner.Step(1.0);
            var moved = physics.JointDriveState(elbow.Id)!.Value;
            _out.WriteLine($"30°'ye gönderildi: {moved.Now:F2}°");
            Assert.InRange(moved.Now, 29, 31);
        }
    }
}
