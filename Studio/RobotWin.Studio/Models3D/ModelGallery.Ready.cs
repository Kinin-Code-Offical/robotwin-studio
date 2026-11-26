using System.Collections.Generic;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Models3D
{
    /// <summary>[objs] Gallery sheets "8-ready" (wheels, casters, propellers), "8-drive" (gears, bearings, shafts, screws) and "9-lights".</summary>
    public static partial class ModelGallery
    {
        private static IEnumerable<Item> ReadyItems()
        {
            const string R = "8-ready", L = "9-lights";
            foreach (var s in ReadyParts.All)
            {
                var spec = s;
                yield return new Item(R, spec.Name, () => World.ReadyModels.Instance(spec), false);
            }

            foreach (var s in DriveParts.All)   // [drive] gears, pulleys, bearings, shafts, screws
            {
                var spec = s;
                yield return new Item("8-drive", spec.Name, () => World.DriveModels.Instance(spec), false);
            }

            foreach (var (id, name) in new[] { (RobotTwin.CoreSim.Design.MotionParts.A4988Id, "A4988"), (RobotTwin.CoreSim.Design.MotionParts.Drv8825Id, "DRV8825"), (RobotTwin.CoreSim.Design.MotionParts.Tmc2209Id, "TMC2209") })
            {
                string part = id;
                yield return new Item("8-drive", name, () => World.MotionModels.CarrierInstance(part), false);   // [stepper] the driver carriers
            }

            foreach (var model in new[] { "LM2596", "MP1584EN", "XL4015", "MT3608", "XL6009" })
            {
                string m = model;
                yield return new Item("8-power", m, () => World.PowerModels.Instance(m), false);   // [smps] regulator modules
            }

            yield return new Item(L, "RGB LED 5 mm", () => Part("led-rgb", "led-rgb-5mm"));
            yield return new Item(L, "RGB LED lit", () =>
            {
                var i = Part("led-rgb", "led-rgb-5mm");
                i.SetBrightness(1);
                return i;
            });
            yield return new Item(L, "Power LED 1 W", () => Part("led-power", "led-1w-white"), false);
            yield return new Item(L, "Power LED 3 W + heat sink, lit", () =>
            {
                var i = Part("led-power", "led-3w-white");
                i.SetBrightness(1);
                return i;
            }, false);
            yield return new Item(L, "LED strip 12 V 150 mm, lit", () =>
            {
                var i = Part("led-strip", "led-strip-12v");
                i.SetBrightness(1);
                return i;
            }, false);
            yield return new Item(L, "LED strip red", () => Part("led-strip", "led-strip-12v-red"), false);
            yield return new Item(L, "Flashlight bulb 3.8 V", () => Part("lamp", "lamp-3v8-0w3"), false);
        }
    }
}
