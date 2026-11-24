using RobotWin.Studio.Editor.Completion;

namespace RobotWin.Studio.Tests;

public class CompletionTests
{
    private const string ArduinoH = @"
#ifndef Arduino_h
#define Arduino_h
#include ""Print.h""
#include ""HardwareSerial.h""
#define HIGH 0x1
#define LOW  0x0
#define INPUT 0x0
#define OUTPUT 0x1
#define INPUT_PULLUP 0x2
#define constrain(amt,low,high) ((amt)<(low)?(low):((amt)>(high)?(high):(amt)))
#ifdef __cplusplus
extern ""C""{
#endif
void pinMode(uint8_t pin, uint8_t mode);
void digitalWrite(uint8_t pin, uint8_t val);
int digitalRead(uint8_t pin);
unsigned long millis(void);
void delay(unsigned long ms);
#ifdef __cplusplus
} // extern ""C""
#endif
long map(long, long, long, long);
#endif";

    private const string PrintH = @"
class Print
{
  private:
    int write_error;
  public:
    Print() : write_error(0) {}
    virtual size_t write(uint8_t) = 0;
    size_t print(const char[]);
    size_t print(int, int = DEC);
    size_t println(const char[]);
    size_t println(void);
};";

    private const string SerialH = @"
#include ""Print.h""
class HardwareSerial : public Print
{
  protected:
    volatile uint8_t _rx_buffer_head;
  public:
    inline HardwareSerial(volatile uint8_t *ubrrh);
    void begin(unsigned long baud) { begin(baud, SERIAL_8N1); }
    void begin(unsigned long, uint8_t);
    virtual int available(void);
    virtual int read(void);
    operator bool() { return true; }
};
extern HardwareSerial Serial;";

    private const string ServoH = @"
/* A servo library */
#define MIN_PULSE_WIDTH 544
class Servo
{
public:
  Servo();
  uint8_t attach(int pin);           // attach the given pin
  uint8_t attach(int pin, int min, int max);
  void detach();
  void write(int value);
  int read();
  bool attached();
private:
   uint8_t servoIndex;
};";

    private static readonly Dictionary<string, string> Files = new()
    {
        ["Arduino.h"] = ArduinoH,
        ["Print.h"] = PrintH,
        ["HardwareSerial.h"] = SerialH,
        ["Servo.h"] = ServoH,
    };

    private static ApiIndex Api(params string[] roots) =>
        ApiIndex.Build(roots, n => Files.TryGetValue(n, out var t) ? CppHeaderScanner.Scan(t, n) : null);

    private static List<string> Labels(CompletionResult? r) => r?.Items.Select(i => i.Label).ToList() ?? new List<string>();

    [Fact]
    public void ScannerReadsPublicMembersBasesFunctionsMacrosAndGlobals()
    {
        var h = CppHeaderScanner.Scan(SerialH, "HardwareSerial.h");
        var c = h.Classes["HardwareSerial"];
        Assert.Contains("Print", c.Bases);
        Assert.Contains(c.Members, m => m.Name == "begin" && m.Signature == "void begin(unsigned long baud)");
        Assert.Contains(c.Members, m => m.Name == "available");
        Assert.DoesNotContain(c.Members, m => m.Name == "_rx_buffer_head");   // protected
        Assert.DoesNotContain(c.Members, m => m.Name == "HardwareSerial");    // constructor
        Assert.Contains(h.Symbols, s => s.Name == "Serial" && s.Kind == ApiKind.Variable && s.Type == "HardwareSerial");

        var a = CppHeaderScanner.Scan(ArduinoH, "Arduino.h");
        Assert.Contains(a.Symbols, s => s.Name == "pinMode" && s.Signature == "void pinMode(uint8_t pin, uint8_t mode)");
        Assert.Contains(a.Symbols, s => s.Name == "HIGH" && s.Kind == ApiKind.Constant);
        Assert.Contains(a.Symbols, s => s.Name == "constrain" && s.Kind == ApiKind.Function);
        Assert.Contains(a.Symbols, s => s.Name == "map");
        Assert.Contains("HardwareSerial.h", a.Includes);

        var s2 = CppHeaderScanner.Scan(ServoH, "Servo.h");
        Assert.Equal(2, s2.Classes["Servo"].Members.Count(m => m.Name == "attach"));
        Assert.DoesNotContain(s2.Classes["Servo"].Members, m => m.Name == "servoIndex");
    }

    [Fact]
    public void MembersOfAGlobalObjectIncludeTheBaseClass()
    {
        var api = Api("Arduino.h");
        string text = "void setup() {\n  Serial.pr";
        var r = CompletionEngine.Complete(text, text.Length, api, Array.Empty<string>(), false);
        var labels = Labels(r);
        Assert.Contains("print", labels);
        Assert.Contains("println", labels);
        Assert.DoesNotContain("begin", labels);
        Assert.Equal(text.Length - 2, r!.Start);
        var print = r.Items.First(i => i.Label == "print");
        Assert.Contains("size_t print(int, int = DEC)", print.Detail);
        Assert.Equal("print()", print.Insert);
        Assert.Equal(6, print.Caret);   // between the brackets: it takes arguments
    }

    [Fact]
    public void ASketchObjectGetsItsClassMembers()
    {
        var api = Api("Arduino.h", "Servo.h");
        string text = "#include <Servo.h>\nServo kol, el;\nvoid setup() {\n  el.";
        var labels = Labels(CompletionEngine.Complete(text, text.Length, api, Array.Empty<string>(), false));
        Assert.Contains("attach", labels);
        Assert.Contains("write", labels);
        Assert.DoesNotContain("pinMode", labels);
    }

    [Fact]
    public void IdentifiersComeFromTheSketchTheCoreKeywordsAndSnippets()
    {
        var api = Api("Arduino.h");
        string text = "const int LED_PIN = 13;\nint okuSensor(int pin) { return 0; }\nvoid loop() {\n  digi";
        var r = CompletionEngine.Complete(text, text.Length, api, Array.Empty<string>(), false);
        Assert.Equal(new[] { "digitalRead", "digitalWrite" }, Labels(r).Take(2).OrderBy(x => x));
        var w = r!.Items.First(i => i.Label == "digitalWrite");
        Assert.NotNull(w.Doc);   // Turkish explanation

        text = "const int LED_PIN = 13;\nint okuSensor(int pin) { return 0; }\nvoid loop() {\n  oku";
        Assert.Equal("okuSensor", Labels(CompletionEngine.Complete(text, text.Length, api, Array.Empty<string>(), false)).First());
        text = "const int LED_PIN = 13;\nvoid loop() {\n  LED";
        Assert.Contains("LED_PIN", Labels(CompletionEngine.Complete(text, text.Length, api, Array.Empty<string>(), false)));
        text = "void loop() {\n  fo";
        Assert.Contains("for", Labels(CompletionEngine.Complete(text, text.Length, api, Array.Empty<string>(), false)));
        // Word starts: "dW" finds digitalWrite.
        text = "void loop() {\n  dW";
        Assert.Equal("digitalWrite", Labels(CompletionEngine.Complete(text, text.Length, api, Array.Empty<string>(), false)).First());
    }

    [Fact]
    public void NothingInCommentsOrStringsAndHeadersAfterInclude()
    {
        var api = Api("Arduino.h");
        string text = "// digi";
        Assert.Null(CompletionEngine.Complete(text, text.Length, api, Array.Empty<string>(), false));
        text = "Serial.println(\"digi";
        Assert.Null(CompletionEngine.Complete(text, text.Length, api, Array.Empty<string>(), false));
        text = "#include <Se";
        var r = CompletionEngine.Complete(text, text.Length, api, new[] { "Servo.h", "SPI.h", "SoftwareSerial.h", "Wire.h" }, false);
        Assert.Equal(new[] { "Servo.h", "SoftwareSerial.h" }, Labels(r).OrderBy(x => x));
        Assert.Equal("Servo.h>", r!.Items.First(i => i.Label == "Servo.h").Insert);
    }

    [Fact]
    public void SignatureHelpShowsOverloadsAndTheCurrentArgument()
    {
        var api = Api("Arduino.h", "Servo.h");
        string text = "Servo s;\nvoid setup() {\n  s.attach(9, ";
        var sig = CompletionEngine.Signature(text, text.Length, api);
        Assert.NotNull(sig);
        Assert.Equal("attach", sig!.Name);
        Assert.Equal(2, sig.Signatures.Count);
        Assert.Equal(1, sig.Argument);
        Assert.NotNull(sig.Doc);
        text = "void loop() {\n  digitalWrite(13, map(x, 0, ";
        sig = CompletionEngine.Signature(text, text.Length, api);
        Assert.Equal("map", sig!.Name);
        Assert.Equal(2, sig.Argument);
        text = "void loop() {\n  digitalWrite(13, map(x, 0, 10, 0, 1), ";
        sig = CompletionEngine.Signature(text, text.Length, api);
        Assert.Equal("digitalWrite", sig!.Name);
        Assert.Equal(2, sig.Argument);
    }
}

public class RealHeaderTests
{
    /// <summary>On a machine with arduino-cli's AVR core (the developer's, not CI): the real headers give the real API.</summary>
    [Fact]
    public void TheInstalledAvrCoreGivesSerialWireAndTheCoreFunctions()
    {
        var headers = new ArduinoHeaders(false, Array.Empty<string>());
        if (!headers.CoreFound) return;
        var api = headers.IndexFor(new[] { "Wire.h", "EEPROM.h", "SPI.h" });
        var serial = api.MembersOf("HardwareSerial").Select(m => m.Name).ToHashSet();
        foreach (var n in new[] { "begin", "available", "read", "print", "println", "write", "parseInt", "readStringUntil", "setTimeout" })
            Assert.Contains(n, serial);
        var wire = api.MembersOf("TwoWire").Select(m => m.Name).ToHashSet();
        foreach (var n in new[] { "begin", "beginTransmission", "endTransmission", "requestFrom", "write", "read", "setClock" })
            Assert.Contains(n, wire);
        foreach (var n in new[] { "pinMode", "digitalWrite", "analogRead", "millis", "delay", "tone", "pulseIn", "attachInterrupt", "map", "constrain" })
            Assert.Contains(api.Symbols, s => s.Name == n);
        foreach (var n in new[] { "HIGH", "LOW", "INPUT_PULLUP", "LED_BUILTIN", "A0" })
            Assert.Contains(api.Symbols, s => s.Name == n);
        Assert.Contains(api.Symbols, s => s.Name == "Wire" && s.Type == "TwoWire");
        Assert.Contains(api.Symbols, s => s.Name == "EEPROM");
        Assert.Contains("Wire.h", headers.Includable);
        string text = "#include <Wire.h>\nvoid setup() {\n  Wire.beginT";
        var r = CompletionEngine.Complete(text, text.Length, api, headers.Includable, false);
        Assert.Equal("beginTransmission", r!.Items[0].Label);

        text = "void setup()\n{\n  Serial.begin(9600);\n}\n\nvoid loop()\n{\n  Serial.pr";
        var core = headers.IndexFor(Array.Empty<string>());
        r = CompletionEngine.Complete(text, text.Length, core, headers.Includable, true);
        Assert.NotNull(r);
        Assert.Contains(r!.Items, i => i.Label == "println");
        text = "void loop()\n{\n  analogWrite(9, ";
        Assert.NotNull(CompletionEngine.Signature(text, text.Length, core));
    }
}

public class LibraryIndexTests
{
    private const string Index = @"{
  ""libraries"": [
    { ""name"": ""Servo"", ""version"": ""1.1.8"", ""author"": ""Michael Margolis, Arduino"", ""sentence"": ""Allows Arduino boards to control a variety of servo motors."", ""paragraph"": """", ""category"": ""Device Control"", ""architectures"": [""avr"", ""megaavr"", ""sam""], ""providesIncludes"": [""Servo.h""] },
    { ""name"": ""Servo"", ""version"": ""1.2.10"", ""author"": ""Michael Margolis, Arduino"", ""sentence"": ""Allows Arduino boards to control a variety of servo motors."", ""paragraph"": """", ""category"": ""Device Control"", ""architectures"": [""avr"", ""megaavr"", ""sam""], ""providesIncludes"": [""Servo.h""] },
    { ""name"": ""Servo"", ""version"": ""1.2.9"", ""author"": ""Michael Margolis, Arduino"", ""sentence"": ""old"", ""architectures"": [""avr""] },
    { ""name"": ""ESP32Servo"", ""version"": ""3.0.5"", ""author"": ""Kevin Harrington"", ""sentence"": ""Allows ESP32 boards to control servo, tone and analogWrite motors using Arduino semantics."", ""architectures"": [""esp32""], ""providesIncludes"": [""ESP32Servo.h""] },
    { ""name"": ""LiquidCrystal I2C"", ""version"": ""1.1.2"", ""author"": ""Frank de Brabander"", ""sentence"": ""A library for I2C LCD displays."", ""architectures"": [""avr""] },
    { ""name"": ""ArduinoJson"", ""version"": ""7.2.1"", ""author"": ""Benoit Blanchon"", ""sentence"": ""A simple and efficient JSON library for embedded C++."", ""architectures"": [""*""], ""providesIncludes"": [""ArduinoJson.h""] }
  ]
}";

    private static ArduinoLibraryIndex Load() => ArduinoLibraryIndex.Parse(System.Text.Encoding.UTF8.GetBytes(Index), DateTime.Now);

    [Fact]
    public void KeepsTheNewestReleaseAndSearchesByNameHeaderAndText()
    {
        var idx = Load();
        Assert.Equal(4, idx.Libraries.Count);
        Assert.Equal("1.2.10", idx.Libraries.Single(l => l.Name == "Servo").Version);   // 1.2.10 is newer than 1.2.9
        Assert.Equal("Servo", idx.Search("servo", "avr")[0].Name);
        Assert.DoesNotContain(idx.Search("servo", "avr"), l => l.Name == "ESP32Servo");   // ESP32 only
        Assert.Equal("ESP32Servo", idx.Search("servo", "esp32")[0].Name);
        Assert.Equal("LiquidCrystal I2C", idx.Search("lcd i2c", "avr")[0].Name);
        Assert.Equal("ArduinoJson", idx.Search("json", "esp32")[0].Name);   // all architectures
        Assert.Empty(idx.Search("json servo", "avr"));
        Assert.Equal("LiquidCrystal_I2C.h", idx.Libraries.Single(l => l.Name == "LiquidCrystal I2C").MainHeader);
        Assert.Equal("Servo", idx.Providing("Servo.h", "avr")[0].Name);
    }

    [Fact]
    public void TheRealIndexLoadsWhenArduinoCliHasOne()
    {
        var idx = ArduinoLibraryIndex.Load(ArduinoLibraryIndex.DefaultPath);
        if (idx == null) return;
        Assert.True(idx.Libraries.Count > 1000);
        Assert.Contains(idx.Search("Servo", "avr").Take(3), l => l.Name == "Servo");
    }
}
