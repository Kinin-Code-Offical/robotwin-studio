using System;
using RobotWin.Studio.World;
using Xunit;

namespace RobotWin.Studio.Tests
{
    /// <summary>[grid] A turned grid: parts snap to its own points, along its own lines.</summary>
    public class GridFrameTests
    {
        [Fact]
        public void UnturnedGridRoundsEachAxis()
        {
            var (x, y) = GridFrame.Nearest(0.0123, -0.0071, 0, 0, 0, 0.005, 0.01);
            Assert.Equal(0.010, x, 12);
            Assert.Equal(-0.010, y, 12);
        }

        [Fact]
        public void TurnedGridSnapsAlongItsOwnLines()
        {
            // a grid turned 30° about (0.1, 0.2) with 10 mm steps: the point 3 steps along its X and 2 across lands on itself
            double a = 30 * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            double px = 0.1 + c * 0.03 - s * 0.02, py = 0.2 + s * 0.03 + c * 0.02;
            var (x, y) = GridFrame.Nearest(px + 0.003, py - 0.002, 0.1, 0.2, 30, 0.01, 0.01);
            Assert.Equal(px, x, 9);
            Assert.Equal(py, y, 9);

            // the grid's position is always on it, and its points stay the step apart along the turned line
            var (ox, oy) = GridFrame.Nearest(0.1004, 0.1997, 0.1, 0.2, 30, 0.01, 0.01);
            Assert.Equal(0.1, ox, 9);
            Assert.Equal(0.2, oy, 9);
            var (nx, ny) = GridFrame.Nearest(0.1 + c * 0.0101, 0.2 + s * 0.0101, 0.1, 0.2, 30, 0.01, 0.01);
            Assert.Equal(0.01, Math.Sqrt((nx - 0.1) * (nx - 0.1) + (ny - 0.2) * (ny - 0.2)), 9);
        }
    }
}
