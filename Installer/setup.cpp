// RobotWin Studio Setup — native Win32 installer (no runtime dependencies).
//
//   setup.exe --pack <publishDir> <out.exe>   build time: appends the compressed app to a copy of this exe
//   setup.exe                                  wizard install (per user, or all users with elevation)
//   setup.exe /S [/D=<dir>] [/ALLUSERS]        silent install
//   setup.exe /S /PORTABLE /D=<dir>            unpack only (USB stick, no shortcuts/registry)
//   Uninstall.exe                              (written into the install folder) removes everything again
//
// Payload: the stub exe, then [u32 count] { [u16 pathLen][wchar path][u64 size][u64 csize][bytes] }*,
// then a footer { u64 payloadOffset, char magic[8] = "RWSETUP1" }. Files are compressed with the
// Windows Compression API (LZMS, cabinet.dll) — nothing to download.

#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <commctrl.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <objbase.h>
#include <shellapi.h>
#include <compressapi.h>
#include <urlmon.h>
#include <string>
#include <vector>
#include <fstream>
#include <filesystem>

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "cabinet.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "uuid.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "urlmon.lib")
#pragma comment(linker, "\"/manifestdependency:type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")

namespace fs = std::filesystem;

static const wchar_t* AppName = L"RobotWin Studio";
static const wchar_t* AppVersion = L"2.0.0";
static const wchar_t* Publisher = L"Kinin Code";
static const wchar_t* ExeName = L"RobotWinStudio.exe";
static const wchar_t* UninstallKey = L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\RobotWinStudio";
static const char Magic[8] = { 'R', 'W', 'S', 'E', 'T', 'U', 'P', '1' };

// ============================================================================ helpers

static std::wstring SelfPath()
{
    wchar_t buf[MAX_PATH * 4];
    GetModuleFileNameW(nullptr, buf, (DWORD)std::size(buf));
    return buf;
}

static std::wstring KnownFolder(REFKNOWNFOLDERID id)
{
    PWSTR p = nullptr;
    std::wstring r;
    if (SUCCEEDED(SHGetKnownFolderPath(id, 0, nullptr, &p))) r = p;
    CoTaskMemFree(p);
    return r;
}

static bool ReadAll(const std::wstring& path, std::vector<char>& out)
{
    std::ifstream f(fs::path(path), std::ios::binary);
    if (!f) return false;
    out.assign(std::istreambuf_iterator<char>(f), {});
    return true;
}

struct PayloadFile
{
    std::wstring path;
    uint64_t size = 0;
    std::vector<char> data;   // compressed
};

// ============================================================================ pack (build time)

static int Pack(const std::wstring& dir, const std::wstring& outExe)
{
    std::vector<char> stub;
    if (!ReadAll(SelfPath(), stub)) return 2;
    // When the stub itself already carries a payload, keep only the stub part.
    if (stub.size() > 16 && memcmp(stub.data() + stub.size() - 8, Magic, 8) == 0)
    {
        uint64_t off;
        memcpy(&off, stub.data() + stub.size() - 16, 8);
        stub.resize((size_t)off);
    }

    COMPRESSOR_HANDLE c = nullptr;
    if (!CreateCompressor(COMPRESS_ALGORITHM_LZMS, nullptr, &c)) return 3;
    std::vector<PayloadFile> files;
    for (auto& e : fs::recursive_directory_iterator(dir))
    {
        if (!e.is_regular_file()) continue;
        PayloadFile pf;
        pf.path = fs::relative(e.path(), dir).wstring();
        std::vector<char> raw;
        ReadAll(e.path().wstring(), raw);
        pf.size = raw.size();
        if (raw.empty())
        {
            files.push_back(std::move(pf));
            continue;
        }

        SIZE_T need = 0;
        Compress(c, raw.data(), raw.size(), nullptr, 0, &need);
        pf.data.resize(need + 64);
        SIZE_T got = 0;
        if (!Compress(c, raw.data(), raw.size(), pf.data.data(), pf.data.size(), &got)) return 4;
        pf.data.resize(got);
        files.push_back(std::move(pf));
        wprintf(L"  %-60ls %10llu -> %10llu\n", files.back().path.c_str(), (unsigned long long)files.back().size, (unsigned long long)got);
    }

    CloseCompressor(c);
    std::ofstream o(fs::path(outExe), std::ios::binary);
    o.write(stub.data(), stub.size());
    uint64_t offset = stub.size();
    uint32_t count = (uint32_t)files.size();
    o.write((char*)&count, 4);
    for (auto& f : files)
    {
        uint16_t n = (uint16_t)f.path.size();
        o.write((char*)&n, 2);
        o.write((char*)f.path.data(), n * 2);
        o.write((char*)&f.size, 8);
        uint64_t cs = f.data.size();
        o.write((char*)&cs, 8);
        o.write(f.data.data(), f.data.size());
    }

    o.write((char*)&offset, 8);
    o.write(Magic, 8);
    wprintf(L"%u files packed into %ls\n", count, outExe.c_str());
    return 0;
}

// ============================================================================ install / uninstall

struct Options
{
    std::wstring dir;
    bool allUsers = false;
    bool desktop = true;
    bool fileTypes = true;
    bool launch = true;
    bool silent = false;
    bool portable = false;   // files only: no shortcuts, no registry, no uninstaller
};

static HWND g_progress = nullptr, g_status = nullptr;

static void Status(const std::wstring& text, int percent)
{
    if (g_status) SetWindowTextW(g_status, text.c_str());
    if (g_progress) SendMessageW(g_progress, PBM_SETPOS, percent, 0);
    MSG msg;
    while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
}

static bool Shortcut(const std::wstring& lnk, const std::wstring& target, const std::wstring& dir, const std::wstring& desc)
{
    IShellLinkW* sl = nullptr;
    if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&sl)))) return false;
    sl->SetPath(target.c_str());
    sl->SetWorkingDirectory(dir.c_str());
    sl->SetDescription(desc.c_str());
    sl->SetIconLocation(target.c_str(), 0);
    IPersistFile* pf = nullptr;
    bool ok = SUCCEEDED(sl->QueryInterface(IID_PPV_ARGS(&pf))) && SUCCEEDED(pf->Save(lnk.c_str(), TRUE));
    if (pf) pf->Release();
    sl->Release();
    return ok;
}

static bool DotNetDesktop9Present()
{
    std::wstring pf = KnownFolder(FOLDERID_ProgramFiles);
    fs::path shared = fs::path(pf) / L"dotnet" / L"shared" / L"Microsoft.WindowsDesktop.App";
    std::error_code ec;
    if (!fs::exists(shared, ec)) return false;
    for (auto& e : fs::directory_iterator(shared, ec))
    {
        auto name = e.path().filename().wstring();
        if (name.rfind(L"9.", 0) == 0) return true;
    }

    return false;
}

static void RunAndWait(const std::wstring& exe, const std::wstring& args, bool hidden = true)
{
    SHELLEXECUTEINFOW sei{ sizeof(sei) };
    sei.fMask = SEE_MASK_NOCLOSEPROCESS;
    sei.lpFile = exe.c_str();
    sei.lpParameters = args.c_str();
    sei.nShow = hidden ? SW_HIDE : SW_SHOWNORMAL;
    if (ShellExecuteExW(&sei) && sei.hProcess)
    {
        WaitForSingleObject(sei.hProcess, 120000);
        CloseHandle(sei.hProcess);
    }
}

static std::wstring Install(const Options& o)
{
    std::vector<char> self;
    if (!ReadAll(SelfPath(), self) || self.size() < 16 || memcmp(self.data() + self.size() - 8, Magic, 8) != 0)
        return L"Kurulum dosyası bozuk: içinde uygulama yok.";
    uint64_t off;
    memcpy(&off, self.data() + self.size() - 16, 8);
    const char* p = self.data() + off;
    uint32_t count;
    memcpy(&count, p, 4);
    p += 4;

    // Close a running copy first.
    Status(L"Çalışan RobotWin Studio aranıyor…", 2);
    std::error_code ec;
    fs::create_directories(o.dir, ec);
    if (ec) return L"Klasör oluşturulamadı: " + o.dir;

    DECOMPRESSOR_HANDLE d = nullptr;
    if (!CreateDecompressor(COMPRESS_ALGORITHM_LZMS, nullptr, &d)) return L"Sıkıştırma kitaplığı açılamadı.";
    std::wstring log;
    for (uint32_t i = 0; i < count; i++)
    {
        uint16_t n;
        memcpy(&n, p, 2);
        p += 2;
        std::wstring rel((const wchar_t*)p, n);
        p += n * 2;
        uint64_t size, cs;
        memcpy(&size, p, 8);
        memcpy(&cs, p + 8, 8);
        p += 16;
        std::vector<char> raw((size_t)size);
        SIZE_T got = 0;
        if (size > 0 && !Decompress(d, p, (SIZE_T)cs, raw.data(), raw.size(), &got))
        {
            CloseDecompressor(d);
            return L"Açılamadı: " + rel;
        }

        p += cs;
        fs::path target = fs::path(o.dir) / rel;
        fs::create_directories(target.parent_path(), ec);
        std::ofstream f(target, std::ios::binary | std::ios::trunc);
        if (!f)
        {
            CloseDecompressor(d);
            return L"Yazılamadı (uygulama açık olabilir): " + target.wstring();
        }

        f.write(raw.data(), raw.size());
        log += rel + L"\n";
        Status(L"Kopyalanıyor: " + rel, 5 + (int)(80.0 * (i + 1) / count));
    }

    CloseDecompressor(d);

    // Uninstaller = this exe without its payload.
    {
        std::ofstream u(fs::path(o.dir) / L"Uninstall.exe", std::ios::binary | std::ios::trunc);
        u.write(self.data(), (std::streamsize)off);
        log += L"Uninstall.exe\n";
    }

    std::wstring exe = (fs::path(o.dir) / ExeName).wstring();
    if (o.portable)
    {
        Status(L"Taşınabilir kurulum tamamlandı.", 100);
        return L"";
    }

    Status(L"Kısayollar oluşturuluyor…", 88);
    std::wstring programs = KnownFolder(o.allUsers ? FOLDERID_CommonPrograms : FOLDERID_Programs);
    Shortcut(programs + L"\\RobotWin Studio.lnk", exe, o.dir, L"Devre, PCB, robot ve dünya simülatörü");
    if (o.desktop) Shortcut(KnownFolder(o.allUsers ? FOLDERID_PublicDesktop : FOLDERID_Desktop) + L"\\RobotWin Studio.lnk", exe, o.dir, L"RobotWin Studio");

    Status(L"Programlar ve Özellikler kaydı…", 92);
    HKEY root = o.allUsers ? HKEY_LOCAL_MACHINE : HKEY_CURRENT_USER, key;
    if (RegCreateKeyExW(root, UninstallKey, 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) == ERROR_SUCCESS)
    {
        auto S = [&](const wchar_t* name, const std::wstring& v) { RegSetValueExW(key, name, 0, REG_SZ, (const BYTE*)v.c_str(), (DWORD)((v.size() + 1) * 2)); };
        auto D = [&](const wchar_t* name, DWORD v) { RegSetValueExW(key, name, 0, REG_DWORD, (const BYTE*)&v, 4); };
        std::wstring un = L"\"" + (fs::path(o.dir) / L"Uninstall.exe").wstring() + L"\"";
        S(L"DisplayName", AppName);
        S(L"DisplayVersion", AppVersion);
        S(L"Publisher", Publisher);
        S(L"DisplayIcon", exe);
        S(L"InstallLocation", o.dir);
        S(L"UninstallString", un);
        S(L"QuietUninstallString", un + L" /S");
        S(L"URLInfoAbout", L"https://github.com/Kinin-Code-Offical/robotwin-studio");
        D(L"NoModify", 1);
        D(L"NoRepair", 1);
        uint64_t bytes = 0;
        for (auto& e : fs::recursive_directory_iterator(o.dir, ec)) if (e.is_regular_file(ec)) bytes += e.file_size(ec);
        D(L"EstimatedSize", (DWORD)(bytes / 1024));
        RegCloseKey(key);
    }

    {
        std::wofstream l(fs::path(o.dir) / L"install.log", std::ios::trunc);
        l << log;
    }

    if (o.fileTypes)
    {
        Status(L"Dosya türleri kaydediliyor (.rwproj, .rwworld …)…", 96);
        RunAndWait(exe, L"--register-file-types");
    }

    Status(L"Kurulum tamamlandı.", 100);
    return L"";
}

static void ScheduleSelfDelete(const std::wstring& dir)
{
    // cmd waits for this process to exit, then removes the folder (Uninstall.exe included).
    std::wstring args = L"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + dir + L"\"";
    ShellExecuteW(nullptr, L"open", L"cmd.exe", args.c_str(), nullptr, SW_HIDE);
}

static int Uninstall(bool silent)
{
    fs::path dir = fs::path(SelfPath()).parent_path();
    if (!silent && MessageBoxW(nullptr, L"RobotWin Studio bilgisayarınızdan kaldırılsın mı?\n\nProjeleriniz ve dünya dosyalarınız silinmez.", L"RobotWin Studio Kaldır", MB_YESNO | MB_ICONQUESTION) != IDYES)
        return 1;
    std::wstring exe = (dir / ExeName).wstring();
    if (fs::exists(exe)) RunAndWait(exe, L"--unregister-file-types");
    for (bool all : { false, true })
    {
        std::wstring programs = KnownFolder(all ? FOLDERID_CommonPrograms : FOLDERID_Programs);
        std::wstring desktop = KnownFolder(all ? FOLDERID_PublicDesktop : FOLDERID_Desktop);
        DeleteFileW((programs + L"\\RobotWin Studio.lnk").c_str());
        DeleteFileW((desktop + L"\\RobotWin Studio.lnk").c_str());
        RegDeleteTreeW(all ? HKEY_LOCAL_MACHINE : HKEY_CURRENT_USER, UninstallKey);
    }

    ScheduleSelfDelete(dir.wstring());
    if (!silent) MessageBoxW(nullptr, L"RobotWin Studio kaldırıldı.", L"RobotWin Studio", MB_ICONINFORMATION);
    return 0;
}

// ============================================================================ wizard UI

enum { IdDir = 100, IdBrowse, IdAllUsers, IdDesktop, IdTypes, IdLaunch, IdInstall, IdCancel, IdDotnet };
static Options g_opt;
static HFONT g_font, g_title;
static HICON g_icon;
static HWND g_dirEdit;

static std::wstring DefaultDir(bool allUsers)
{
    return allUsers ? KnownFolder(FOLDERID_ProgramFiles) + L"\\RobotWin Studio"
                    : KnownFolder(FOLDERID_LocalAppData) + L"\\Programs\\RobotWin Studio";
}

static HWND Ctl(HWND parent, const wchar_t* cls, const wchar_t* text, DWORD style, int x, int y, int w, int h, int id)
{
    HWND c = CreateWindowExW(0, cls, text, WS_CHILD | WS_VISIBLE | style, x, y, w, h, parent, (HMENU)(INT_PTR)id, nullptr, nullptr);
    SendMessageW(c, WM_SETFONT, (WPARAM)g_font, TRUE);
    return c;
}

static LRESULT CALLBACK WndProc(HWND h, UINT m, WPARAM w, LPARAM l)
{
    switch (m)
    {
    case WM_CREATE:
    {
        Ctl(h, L"STATIC", L"Kurulum klasörü", 0, 32, 120, 400, 20, 0);
        g_dirEdit = Ctl(h, L"EDIT", g_opt.dir.c_str(), WS_BORDER | ES_AUTOHSCROLL, 32, 142, 420, 26, IdDir);
        Ctl(h, L"BUTTON", L"Gözat…", BS_PUSHBUTTON, 460, 141, 88, 28, IdBrowse);
        Ctl(h, L"BUTTON", L"Tüm kullanıcılar için kur (yönetici izni ister)", BS_AUTOCHECKBOX, 32, 182, 500, 22, IdAllUsers);
        HWND d = Ctl(h, L"BUTTON", L"Masaüstü kısayolu oluştur", BS_AUTOCHECKBOX, 32, 208, 500, 22, IdDesktop);
        SendMessageW(d, BM_SETCHECK, BST_CHECKED, 0);
        HWND t = Ctl(h, L"BUTTON", L".rwproj, .rwworld, .rwpart, .rwlib, .rwsim dosyalarını RobotWin ile aç", BS_AUTOCHECKBOX, 32, 234, 520, 22, IdTypes);
        SendMessageW(t, BM_SETCHECK, BST_CHECKED, 0);
        HWND r = Ctl(h, L"BUTTON", L"Kurulumdan sonra RobotWin Studio'yu başlat", BS_AUTOCHECKBOX, 32, 260, 500, 22, IdLaunch);
        SendMessageW(r, BM_SETCHECK, BST_CHECKED, 0);
        if (!DotNetDesktop9Present())
        {
            Ctl(h, L"STATIC", L"Bu bilgisayarda .NET 9 Masaüstü Çalışma Zamanı yok; RobotWin Studio buna ihtiyaç duyar.", 0, 32, 292, 520, 20, 0);
            Ctl(h, L"BUTTON", L".NET 9'u indir ve kur (Microsoft)", BS_PUSHBUTTON, 32, 314, 250, 28, IdDotnet);
        }

        g_status = Ctl(h, L"STATIC", L"", 0, 32, 356, 520, 20, 0);
        g_progress = Ctl(h, PROGRESS_CLASSW, L"", PBS_SMOOTH, 32, 380, 516, 14, 0);
        Ctl(h, L"BUTTON", L"Kur", BS_DEFPUSHBUTTON, 356, 412, 92, 32, IdInstall);
        Ctl(h, L"BUTTON", L"Vazgeç", BS_PUSHBUTTON, 456, 412, 92, 32, IdCancel);
        return 0;
    }

    case WM_PAINT:
    {
        PAINTSTRUCT ps;
        HDC dc = BeginPaint(h, &ps);
        RECT rc;
        GetClientRect(h, &rc);
        // Header: flat near-black band with the icon and title.
        RECT band{ 0, 0, rc.right, 96 };
        HBRUSH bg = CreateSolidBrush(RGB(0x1C, 0x1C, 0x1F));
        FillRect(dc, &band, bg);
        DeleteObject(bg);
        DrawIconEx(dc, 28, 18, g_icon, 60, 60, 0, nullptr, DI_NORMAL);
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, RGB(255, 255, 255));
        SelectObject(dc, g_title);
        TextOutW(dc, 104, 20, L"RobotWin Studio", 15);
        SelectObject(dc, g_font);
        std::wstring sub = std::wstring(L"Sürüm ") + AppVersion + L" · devre, PCB, robot ve dünya simülatörü";
        TextOutW(dc, 106, 58, sub.c_str(), (int)sub.size());
        EndPaint(h, &ps);
        return 0;
    }

    case WM_CTLCOLORSTATIC:
        SetBkMode((HDC)w, TRANSPARENT);
        return (LRESULT)GetSysColorBrush(COLOR_WINDOW);

    case WM_COMMAND:
        switch (LOWORD(w))
        {
        case IdBrowse:
        {
            IFileOpenDialog* dlg = nullptr;
            if (SUCCEEDED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dlg))))
            {
                DWORD opts;
                dlg->GetOptions(&opts);
                dlg->SetOptions(opts | FOS_PICKFOLDERS);
                if (SUCCEEDED(dlg->Show(h)))
                {
                    IShellItem* it = nullptr;
                    PWSTR p = nullptr;
                    if (SUCCEEDED(dlg->GetResult(&it)) && SUCCEEDED(it->GetDisplayName(SIGDN_FILESYSPATH, &p)))
                    {
                        SetWindowTextW(g_dirEdit, (std::wstring(p) + L"\\RobotWin Studio").c_str());
                        CoTaskMemFree(p);
                    }

                    if (it) it->Release();
                }

                dlg->Release();
            }

            return 0;
        }

        case IdAllUsers:
            SetWindowTextW(g_dirEdit, DefaultDir(IsDlgButtonChecked(h, IdAllUsers) == BST_CHECKED).c_str());
            return 0;

        case IdDotnet:
        {
            Status(L".NET 9 Masaüstü Çalışma Zamanı indiriliyor (yaklaşık 55 MB)…", 0);
            wchar_t tmp[MAX_PATH];
            GetTempPathW(MAX_PATH, tmp);
            std::wstring file = std::wstring(tmp) + L"windowsdesktop-runtime-9-win-x64.exe";
            if (URLDownloadToFileW(nullptr, L"https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe", file.c_str(), 0, nullptr) == S_OK)
            {
                Status(L".NET 9 kuruluyor…", 50);
                RunAndWait(file, L"/install /passive /norestart", false);
                Status(DotNetDesktop9Present() ? L".NET 9 kuruldu." : L".NET 9 kurulamadı; daha sonra dotnet.microsoft.com adresinden kurabilirsiniz.", 100);
            }
            else
            {
                Status(L"İndirilemedi. İnternet bağlantısını kontrol edin.", 0);
            }

            return 0;
        }

        case IdInstall:
        {
            wchar_t buf[MAX_PATH * 2];
            GetWindowTextW(g_dirEdit, buf, (int)std::size(buf));
            g_opt.dir = buf;
            g_opt.allUsers = IsDlgButtonChecked(h, IdAllUsers) == BST_CHECKED;
            g_opt.desktop = IsDlgButtonChecked(h, IdDesktop) == BST_CHECKED;
            g_opt.fileTypes = IsDlgButtonChecked(h, IdTypes) == BST_CHECKED;
            g_opt.launch = IsDlgButtonChecked(h, IdLaunch) == BST_CHECKED;
            if (g_opt.allUsers && !IsUserAnAdmin())
            {
                // Re-run elevated, silently, with the chosen options.
                std::wstring args = L"/S /ALLUSERS /D=\"" + g_opt.dir + L"\"" + (g_opt.desktop ? L"" : L" /NODESKTOP") + (g_opt.fileTypes ? L"" : L" /NOTYPES") + (g_opt.launch ? L" /LAUNCH" : L"");
                SHELLEXECUTEINFOW sei{ sizeof(sei) };
                sei.fMask = SEE_MASK_NOCLOSEPROCESS;
                sei.lpVerb = L"runas";
                std::wstring self = SelfPath();
                sei.lpFile = self.c_str();
                sei.lpParameters = args.c_str();
                sei.nShow = SW_SHOWNORMAL;
                if (ShellExecuteExW(&sei) && sei.hProcess)
                {
                    Status(L"Yönetici olarak kuruluyor…", 50);
                    WaitForSingleObject(sei.hProcess, INFINITE);
                    CloseHandle(sei.hProcess);
                    Status(L"Kurulum tamamlandı.", 100);
                    MessageBoxW(h, L"RobotWin Studio kuruldu.", AppName, MB_ICONINFORMATION);
                    DestroyWindow(h);
                }

                return 0;
            }

            EnableWindow(GetDlgItem(h, IdInstall), FALSE);
            std::wstring err = Install(g_opt);
            if (!err.empty())
            {
                MessageBoxW(h, err.c_str(), L"Kurulum başarısız", MB_ICONERROR);
                EnableWindow(GetDlgItem(h, IdInstall), TRUE);
                return 0;
            }

            if (g_opt.launch) ShellExecuteW(nullptr, L"open", (fs::path(g_opt.dir) / ExeName).c_str(), nullptr, g_opt.dir.c_str(), SW_SHOWNORMAL);
            else MessageBoxW(h, L"RobotWin Studio kuruldu. Başlat menüsünden açabilirsiniz.", AppName, MB_ICONINFORMATION);
            DestroyWindow(h);
            return 0;
        }

        case IdCancel:
            DestroyWindow(h);
            return 0;
        }

        break;

    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }

    return DefWindowProcW(h, m, w, l);
}

static std::wstring ArgValue(int argc, wchar_t** argv, const wchar_t* prefix)
{
    size_t n = wcslen(prefix);
    for (int i = 1; i < argc; i++)
        if (_wcsnicmp(argv[i], prefix, n) == 0) return std::wstring(argv[i] + n);
    return L"";
}

static bool HasArg(int argc, wchar_t** argv, const wchar_t* a)
{
    for (int i = 1; i < argc; i++) if (_wcsicmp(argv[i], a) == 0) return true;
    return false;
}

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE, PWSTR, int show)
{
    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    if (argc >= 4 && _wcsicmp(argv[1], L"--pack") == 0)
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        FILE* f;
        _wfreopen_s(&f, L"CONOUT$", L"w", stdout);
        return Pack(argv[2], argv[3]);
    }

    bool silent = HasArg(argc, argv, L"/S");
    std::wstring self = fs::path(SelfPath()).filename().wstring();
    if (_wcsicmp(self.c_str(), L"Uninstall.exe") == 0 || HasArg(argc, argv, L"/uninstall")) return Uninstall(silent);

    g_opt.allUsers = HasArg(argc, argv, L"/ALLUSERS");
    g_opt.dir = ArgValue(argc, argv, L"/D=");
    if (g_opt.dir.empty()) g_opt.dir = DefaultDir(g_opt.allUsers);
    if (silent)
    {
        g_opt.desktop = !HasArg(argc, argv, L"/NODESKTOP");
        g_opt.fileTypes = !HasArg(argc, argv, L"/NOTYPES");
        g_opt.launch = HasArg(argc, argv, L"/LAUNCH");
        g_opt.portable = HasArg(argc, argv, L"/PORTABLE");
        std::wstring err = Install(g_opt);
        if (!err.empty()) return 1;
        if (g_opt.launch) ShellExecuteW(nullptr, L"open", (fs::path(g_opt.dir) / ExeName).c_str(), nullptr, g_opt.dir.c_str(), SW_SHOWNORMAL);
        return 0;
    }

    INITCOMMONCONTROLSEX icc{ sizeof(icc), ICC_PROGRESS_CLASS | ICC_STANDARD_CLASSES };
    InitCommonControlsEx(&icc);
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    g_font = CreateFontW(-15, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, L"Segoe UI");
    g_title = CreateFontW(-28, 0, 0, 0, FW_SEMIBOLD, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, L"Segoe UI");
    g_icon = (HICON)LoadImageW(inst, MAKEINTRESOURCEW(1), IMAGE_ICON, 64, 64, LR_DEFAULTCOLOR);

    WNDCLASSW wc{};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wc.hbrBackground = GetSysColorBrush(COLOR_WINDOW);
    wc.hIcon = g_icon;
    wc.lpszClassName = L"RobotWinSetup";
    RegisterClassW(&wc);
    RECT r{ 0, 0, 580, 464 };
    AdjustWindowRect(&r, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX, FALSE);
    int w = r.right - r.left, hgt = r.bottom - r.top;
    HWND h = CreateWindowW(L"RobotWinSetup", L"RobotWin Studio Kurulum", WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
        (GetSystemMetrics(SM_CXSCREEN) - w) / 2, (GetSystemMetrics(SM_CYSCREEN) - hgt) / 2, w, hgt, nullptr, nullptr, inst, nullptr);
    ShowWindow(h, show);
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0)
    {
        if (!IsDialogMessageW(h, &msg))
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }

    CoUninitialize();
    return 0;
}
