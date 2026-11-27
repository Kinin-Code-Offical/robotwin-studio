/*
 * [isolation] The ESP32 fast mode's sketch host: runs one compiled sketch DLL in a process of its own, so a sketch that
 * crashes, hangs or misbehaves cannot take the app down with it and, started by the app in an AppContainer inside a job,
 * cannot reach the user's files, the network, other processes or the desktop.
 *
 * Everything the sketch does goes through rw_host_api; this program hands it to the app over one shared section
 * (Esp32SketchProcess.cs mirrors the layout below):
 *   - writes (pin modes and levels, PWM, DAC, UART, logs, system events, the end of an I2C transfer) are queued with the
 *     sketch's time and the app takes them, in order, at the next exchange;
 *   - pin reads (levels, voltages, touch) come from the snapshot the app writes before each command: the circuit does
 *     not move while the sketch runs its slice, so they are exactly what the app would answer;
 *   - I2C bytes and radio / SPI / LED strip requests wait for the app's answer.
 * The app sends one command at a time (init, start, run until, stop, pin change, serial input …) and answers the
 * host's requests until the command is done.
 *
 * Usage: rw_sketch_host.exe <sketch.dll> <section handle> <app event handle> <host event handle>
 * (handles inherited from the app, as decimal numbers).
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include "rw_host_api.h"

namespace {

const uint32_t Magic = 0x31585752;   // "RWX1"
const uint32_t LayoutVersion = 1;
const int Gpios = 48;

// Offsets in the shared section: keep in sync with Esp32SketchProcess.cs.
const size_t OffDown = 64;           // Packet: app -> host
const size_t OffUp = 128;            // Packet: host -> app
const size_t OffLevel = 192;         // u8[48]   digitalRead
const size_t OffTouch = 256;         // i32[48]  touchRead
const size_t OffVolts = 448;         // f64[48]  analogRead
const size_t OffQueueUsed = 832;     // u32      bytes queued
const size_t OffDownData = 1024;
const size_t DataSize = 65536;
const size_t OffUpData = OffDownData + DataSize;
const size_t OffQueue = OffUpData + DataSize;
const size_t QueueSize = 262144;
const size_t SectionSize = OffQueue + QueueSize;

#pragma pack(push, 8)
struct Packet
{
    int32_t op, i0, i1, i2;
    int64_t l0, l1;
    uint64_t u0;   // the sketch's time (ns) when the host sends it; the command's time argument when the app does
    double d0, d1;
    int32_t len, cap;
};

struct Record
{
    int32_t op, i0, i1, len;
    int64_t l0;
    uint64_t t;
    double d0, d1;
};
#pragma pack(pop)
static_assert(sizeof(Packet) == 64, "Packet layout");
static_assert(sizeof(Record) == 48, "Record layout");

enum Command { CmdInit = 1, CmdStart, CmdRunUntil, CmdStop, CmdPinChanged, CmdSerialRx, CmdNow, CmdStatus, CmdMemory, CmdExit };
enum UpKind { UpDone = 100, UpDrain, UpI2cStart, UpI2cWrite, UpI2cRead, UpRadio, UpReady };
enum RecordKind { RecLog = 1, RecPinMode, RecDigitalWrite, RecPwm, RecDac, RecSerialBegin, RecSerialWrite, RecSystemEvent, RecI2cStop };

uint8_t* g_mem;
HANDLE g_app;    // set by the host: a packet is waiting in OffUp
HANDLE g_host;   // set by the app: a packet is waiting in OffDown
CRITICAL_SECTION g_cs;

typedef int (*AbiFn)(void);
typedef int (*InitFn)(const rw_host_api*, const rw_config*);
typedef int (*StartFn)(uint64_t);
typedef int (*RunUntilFn)(uint64_t);
typedef void (*StopFn)(void);
typedef void (*PinChangedFn)(int, int, uint64_t);
typedef int (*SerialRxFn)(int, const uint8_t*, int);
typedef uint64_t (*NowFn)(void);
typedef int (*StatusFn)(char*, int);
typedef void (*MemoryFn)(uint64_t*, uint64_t*);

AbiFn g_abi;
InitFn g_init;
StartFn g_start;
RunUntilFn g_run_until;
StopFn g_stop;
PinChangedFn g_pin_changed;
SerialRxFn g_serial_rx;
NowFn g_now;
StatusFn g_status;
MemoryFn g_memory;

Packet* Down() { return reinterpret_cast<Packet*>(g_mem + OffDown); }
Packet* Up() { return reinterpret_cast<Packet*>(g_mem + OffUp); }
uint32_t& QueueUsed() { return *reinterpret_cast<uint32_t*>(g_mem + OffQueueUsed); }
uint64_t Now() { return g_now ? g_now() : 0; }

void Post(Packet p)
{
    p.u0 = Now();
    *Up() = p;
    MemoryBarrier();
    SetEvent(g_app);
}

// [perf] The app's next command (or its answer) usually comes within microseconds: poll the event for up to 50 us before
// sleeping on it, since a sleep and a wake-up cost a thread switch each, thousands of times a simulated second.
bool WaitHost()
{
    static LONGLONG spin = 0;
    if (spin == 0)
    {
        LARGE_INTEGER f;
        QueryPerformanceFrequency(&f);
        SYSTEM_INFO si;
        GetSystemInfo(&si);
        spin = si.dwNumberOfProcessors > 1 ? f.QuadPart / 20000 : -1;
    }

    if (spin > 0)
    {
        LARGE_INTEGER t0, t;
        QueryPerformanceCounter(&t0);
        for (;;)
        {
            DWORD r = WaitForSingleObject(g_host, 0);
            if (r == WAIT_OBJECT_0) return true;
            if (r != WAIT_TIMEOUT) return false;
            QueryPerformanceCounter(&t);
            if (t.QuadPart - t0.QuadPart > spin) break;
            YieldProcessor();
        }
    }

    return WaitForSingleObject(g_host, INFINITE) == WAIT_OBJECT_0;
}

// One request to the app: it first takes the queue, then answers this packet.
void Exchange(Packet& p)
{
    Post(p);
    if (!WaitHost()) ExitProcess(3);
    MemoryBarrier();
    p = *Down();
}

void Queue(int32_t op, int32_t i0, int32_t i1, int64_t l0, double d0, double d1, const void* data, int len)
{
    if (len < 0 || !data) len = 0;
    size_t need = sizeof(Record) + ((static_cast<size_t>(len) + 7) & ~static_cast<size_t>(7));
    EnterCriticalSection(&g_cs);
    if (QueueUsed() + need > QueueSize)
    {
        Packet p = {};
        p.op = UpDrain;
        Exchange(p);
    }

    Record* r = reinterpret_cast<Record*>(g_mem + OffQueue + QueueUsed());
    r->op = op;
    r->i0 = i0;
    r->i1 = i1;
    r->len = len;
    r->l0 = l0;
    r->t = Now();
    r->d0 = d0;
    r->d1 = d1;
    if (len > 0) memcpy(r + 1, data, static_cast<size_t>(len));
    QueueUsed() += static_cast<uint32_t>(need);
    LeaveCriticalSection(&g_cs);
}

int Request(Packet& p)
{
    EnterCriticalSection(&g_cs);
    Exchange(p);
    LeaveCriticalSection(&g_cs);
    return static_cast<int>(p.l0);
}

// ---------------------------------------------------------------- rw_host_api for the sketch

void CbLog(void*, int level, const char* message)
{
    int n = message ? static_cast<int>(strnlen(message, 16000)) : 0;
    char empty = 0;
    Queue(RecLog, level, 0, 0, 0, 0, message ? message : &empty, n + 1);   // with its terminating zero
}

void CbPinMode(void*, int gpio, int mode) { Queue(RecPinMode, gpio, mode, 0, 0, 0, nullptr, 0); }
void CbDigitalWrite(void*, int gpio, int level) { Queue(RecDigitalWrite, gpio, level, 0, 0, 0, nullptr, 0); }
int CbDigitalRead(void*, int gpio) { return gpio >= 0 && gpio < Gpios ? g_mem[OffLevel + gpio] : 0; }
double CbAnalogRead(void*, int gpio) { return gpio >= 0 && gpio < Gpios ? reinterpret_cast<const double*>(g_mem + OffVolts)[gpio] : 0.0; }
void CbPwmWrite(void*, int gpio, double frequency, double duty) { Queue(RecPwm, gpio, 0, 0, frequency, duty, nullptr, 0); }
void CbDacWrite(void*, int gpio, double volts) { Queue(RecDac, gpio, 0, 0, volts, 0, nullptr, 0); }
int CbTouchRead(void*, int gpio) { return gpio >= 0 && gpio < Gpios ? reinterpret_cast<const int32_t*>(g_mem + OffTouch)[gpio] : 0; }
void CbSerialBegin(void*, int port, uint32_t baud) { Queue(RecSerialBegin, port, 0, static_cast<int64_t>(baud), 0, 0, nullptr, 0); }

void CbSerialWrite(void*, int port, const uint8_t* data, int length)
{
    while (data && length > 0)
    {
        int n = length < 16384 ? length : 16384;
        Queue(RecSerialWrite, port, 0, 0, 0, 0, data, n);
        data += n;
        length -= n;
    }
}

int CbI2cStart(void*, int bus, int address, int read)
{
    Packet p = {};
    p.op = UpI2cStart;
    p.i0 = bus;
    p.i1 = address;
    p.i2 = read;
    return Request(p);
}

int CbI2cWrite(void*, int bus, int value)
{
    Packet p = {};
    p.op = UpI2cWrite;
    p.i0 = bus;
    p.i1 = value;
    return Request(p);
}

int CbI2cRead(void*, int bus, int ack)
{
    Packet p = {};
    p.op = UpI2cRead;
    p.i0 = bus;
    p.i1 = ack;
    return Request(p);
}

void CbI2cStop(void*, int bus) { Queue(RecI2cStop, bus, 0, 0, 0, 0, nullptr, 0); }
void CbSystemEvent(void*, int kind, int64_t arg) { Queue(RecSystemEvent, kind, 0, arg, 0, 0, nullptr, 0); }

int CbRadioRequest(void*, int op, const void* in, int inLength, void* out, int outCapacity)
{
    if (inLength < 0 || !in) inLength = 0;
    if (outCapacity < 0 || !out) outCapacity = 0;
    if (inLength > static_cast<int>(DataSize)) inLength = static_cast<int>(DataSize);
    if (outCapacity > static_cast<int>(DataSize)) outCapacity = static_cast<int>(DataSize);
    EnterCriticalSection(&g_cs);
    if (inLength > 0) memcpy(g_mem + OffUpData, in, static_cast<size_t>(inLength));
    // The answer is written over what the caller's buffer held, as it is in the app's own process.
    if (outCapacity > 0) memcpy(g_mem + OffDownData, out, static_cast<size_t>(outCapacity));
    Packet p = {};
    p.op = UpRadio;
    p.i0 = op;
    p.len = inLength;
    p.cap = outCapacity;
    Exchange(p);
    if (outCapacity > 0) memcpy(out, g_mem + OffDownData, static_cast<size_t>(outCapacity));
    LeaveCriticalSection(&g_cs);
    return static_cast<int>(p.l0);
}

rw_host_api g_api;

template <typename T>
bool Bind(HMODULE dll, const char* name, T& fn)
{
    fn = reinterpret_cast<T>(GetProcAddress(dll, name));
    return fn != nullptr;
}

HANDLE HandleArg(const wchar_t* s) { return reinterpret_cast<HANDLE>(static_cast<uintptr_t>(_wcstoui64(s, nullptr, 10))); }

}   // namespace

int wmain(int argc, wchar_t** argv)
{
    if (argc < 5) return 2;
    // A crash is the app's to report (the sketch's own handler writes the Guru Meditation text): no Windows dialog.
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    HANDLE section = HandleArg(argv[2]);
    g_app = HandleArg(argv[3]);
    g_host = HandleArg(argv[4]);
    g_mem = static_cast<uint8_t*>(MapViewOfFile(section, FILE_MAP_ALL_ACCESS, 0, 0, SectionSize));
    if (!g_mem || *reinterpret_cast<uint32_t*>(g_mem) != Magic || reinterpret_cast<uint32_t*>(g_mem)[1] != LayoutVersion) return 3;
    InitializeCriticalSection(&g_cs);

    Packet ready = {};
    ready.op = UpReady;
    HMODULE dll = LoadLibraryW(argv[1]);
    if (!dll)
    {
        ready.l0 = -1;
        ready.l1 = static_cast<int64_t>(GetLastError());
        Post(ready);
        return 4;
    }

    bool ok = Bind(dll, "rw_abi_version", g_abi) && Bind(dll, "rw_init", g_init) && Bind(dll, "rw_start", g_start) &&
              Bind(dll, "rw_run_until", g_run_until) && Bind(dll, "rw_stop", g_stop) && Bind(dll, "rw_pin_changed", g_pin_changed) &&
              Bind(dll, "rw_serial_rx", g_serial_rx) && Bind(dll, "rw_now_ns", g_now) && Bind(dll, "rw_status_message", g_status);
    Bind(dll, "rw_memory", g_memory);   // absent in builds before it
    if (!ok)
    {
        ready.l0 = -2;
        Post(ready);
        return 5;
    }

    g_api.abi_version = RW_ABI_VERSION;
    g_api.struct_size = sizeof(rw_host_api);
    g_api.user = nullptr;
    g_api.log = CbLog;
    g_api.pin_mode = CbPinMode;
    g_api.digital_write = CbDigitalWrite;
    g_api.digital_read = CbDigitalRead;
    g_api.analog_read = CbAnalogRead;
    g_api.pwm_write = CbPwmWrite;
    g_api.dac_write = CbDacWrite;
    g_api.touch_read = CbTouchRead;
    g_api.serial_begin = CbSerialBegin;
    g_api.serial_write = CbSerialWrite;
    g_api.i2c_start = CbI2cStart;
    g_api.i2c_write = CbI2cWrite;
    g_api.i2c_read = CbI2cRead;
    g_api.i2c_stop = CbI2cStop;
    g_api.system_event = CbSystemEvent;
    g_api.radio_request = CbRadioRequest;

    ready.l0 = 0;
    ready.l1 = g_abi();
    Post(ready);

    static uint8_t rx[DataSize];
    for (;;)
    {
        if (!WaitHost()) return 6;
        MemoryBarrier();
        Packet c = *Down();
        Packet r = {};
        r.op = UpDone;
        switch (c.op)
        {
            case CmdInit:
            {
                rw_config cfg = {};
                size_t n = c.len > 0 ? static_cast<size_t>(c.len) : 0;
                memcpy(&cfg, g_mem + OffDownData, n < sizeof cfg ? n : sizeof cfg);
                r.l0 = g_init(&g_api, &cfg);
                break;
            }
            case CmdStart:
                r.l0 = g_start(c.u0);
                break;
            case CmdRunUntil:
                r.l0 = g_run_until(c.u0);
                break;
            case CmdStop:
                g_stop();
                break;
            case CmdPinChanged:
                g_pin_changed(c.i0, c.i1, c.u0);
                break;
            case CmdSerialRx:
            {
                int n = c.len < 0 ? 0 : c.len > static_cast<int>(DataSize) ? static_cast<int>(DataSize) : c.len;
                memcpy(rx, g_mem + OffDownData, static_cast<size_t>(n));
                r.l0 = g_serial_rx(c.i0, rx, n);
                break;
            }
            case CmdNow:
                break;
            case CmdStatus:
            {
                int cap = c.cap < 0 ? 0 : c.cap > static_cast<int>(DataSize) ? static_cast<int>(DataSize) : c.cap;
                r.l0 = cap > 0 ? g_status(reinterpret_cast<char*>(g_mem + OffUpData), cap) : 0;
                break;
            }
            case CmdMemory:
                if (g_memory)
                {
                    uint64_t heap = 0, stacks = 0;
                    g_memory(&heap, &stacks);
                    r.l0 = static_cast<int64_t>(heap);
                    r.l1 = static_cast<int64_t>(stacks);
                    r.i0 = 1;
                }

                break;
            case CmdExit:
                ExitProcess(0);
            default:
                r.l0 = -1;
                break;
        }

        Post(r);
    }
}
