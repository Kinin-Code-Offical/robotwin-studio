using System.Linq;
using RobotWin.Studio.Firmware;
using Xunit;

namespace RobotWin.Studio.Tests
{
    /// <summary>[crossprobe] The pin a sketch means where the caret is.</summary>
    public class PinProbeTests
    {
        private const string Sketch = "#define LED 13\nconst int TRIG = 9;\nint speed = 9;\nServo s;\nvoid setup() {\n  pinMode(LED, OUTPUT);\n  pinMode(TRIG, OUTPUT);\n  s.attach(5);\n  int v = analogRead(A0);\n  digitalWrite(12, HIGH);\n  analogWrite(speed2, 3);\n}\n";

        private static int At(string token, int nth = 0)
        {
            int i = -1;
            for (int k = 0; k <= nth; k++) i = Sketch.IndexOf(token, i + 1, System.StringComparison.Ordinal);
            return i + 1;   // the caret inside the token
        }

        [Fact]
        public void NamesGivenToPinCallsResolveWhereverTheCaretIsOnThem()
        {
            Assert.Equal("13", PinProbe.PinAt(Sketch, At("LED")));        // its #define
            Assert.Equal("13", PinProbe.PinAt(Sketch, At("LED", 1)));     // its use in pinMode
            Assert.Equal("9", PinProbe.PinAt(Sketch, At("TRIG")));        // its constant
            Assert.Null(PinProbe.PinAt(Sketch, At("speed")));               // never given to a pin call: not a pin
        }

        [Fact]
        public void LiteralsCountOnlyAsAPinCallsArgument()
        {
            Assert.Equal("5", PinProbe.PinAt(Sketch, At("5)")));
            Assert.Equal("A0", PinProbe.PinAt(Sketch, At("A0")));
            Assert.Equal("12", PinProbe.PinAt(Sketch, At("12")));
            Assert.Null(PinProbe.PinAt(Sketch, At("3)")));                 // analogWrite's value, not its pin
            Assert.Null(PinProbe.PinAt(Sketch, At("9;", 1)));              // int speed = 9
        }

        [Fact]
        public void PinNumbersMapToTheBoardsNames()
        {
            Assert.Equal("D9", PinProbe.BoardPinNames("9").First());
            Assert.Contains("IO25", PinProbe.BoardPinNames("25"));
            Assert.Contains("A0", PinProbe.BoardPinNames("14"));
            Assert.Equal(new[] { "A3" }, PinProbe.BoardPinNames("A3"));
        }
    }
}
