#include <windows.h>
#include <tlhelp32.h>
#include <algorithm>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <string>
#include <thread>
#include <vector>

struct WindowInfo {
    HWND hwnd{};
    DWORD pid{};
    std::wstring process;
    std::wstring title;
    std::wstring className;
    RECT rect{};
};

static std::wstring ProcessName(DWORD pid) {
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return L"";
    PROCESSENTRY32W entry{sizeof(entry)};
    std::wstring result;
    if (Process32FirstW(snapshot, &entry)) {
        do {
            if (entry.th32ProcessID == pid) { result = entry.szExeFile; break; }
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return result;
}

static BOOL CALLBACK CollectWindow(HWND hwnd, LPARAM value) {
    if (!IsWindowVisible(hwnd)) return TRUE;
    wchar_t title[512]{};
    wchar_t className[256]{};
    GetWindowTextW(hwnd, title, 512);
    GetClassNameW(hwnd, className, 256);
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    RECT rect{};
    GetWindowRect(hwnd, &rect);
    auto* windows = reinterpret_cast<std::vector<WindowInfo>*>(value);
    windows->push_back({hwnd, pid, ProcessName(pid), title, className, rect});
    return TRUE;
}

static std::vector<WindowInfo> EnumerateWindows() {
    std::vector<WindowInfo> windows;
    EnumWindows(CollectWindow, reinterpret_cast<LPARAM>(&windows));
    return windows;
}

static std::string Utf8(const std::wstring& value) {
    if (value.empty()) return {};
    int size = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    std::string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), result.data(), size, nullptr, nullptr);
    return result;
}

static std::string JsonEscape(const std::wstring& value) {
    std::string input = Utf8(value);
    std::string output;
    for (char ch : input) {
        if (ch == '\\' || ch == '"') output.push_back('\\');
        if (ch == '\n') { output += "\\n"; continue; }
        if (ch == '\r') { output += "\\r"; continue; }
        output.push_back(ch);
    }
    return output;
}

static int WriteWindows(const std::wstring& outputPath) {
    std::filesystem::create_directories(std::filesystem::path(outputPath).parent_path());
    std::ofstream output(std::filesystem::path(outputPath), std::ios::binary);
    if (!output) return 4;
    auto windows = EnumerateWindows();
    output << "{\n  \"schemaVersion\": 1,\n  \"windows\": [\n";
    for (size_t i = 0; i < windows.size(); ++i) {
        const auto& item = windows[i];
        output << "    {\"hwnd\": " << reinterpret_cast<uintptr_t>(item.hwnd)
               << ", \"pid\": " << item.pid
               << ", \"process\": \"" << JsonEscape(item.process)
               << "\", \"title\": \"" << JsonEscape(item.title)
               << "\", \"className\": \"" << JsonEscape(item.className)
               << "\", \"x\": " << item.rect.left << ", \"y\": " << item.rect.top
               << ", \"width\": " << item.rect.right - item.rect.left
               << ", \"height\": " << item.rect.bottom - item.rect.top << "}";
        output << (i + 1 == windows.size() ? "\n" : ",\n");
    }
    output << "  ]\n}\n";
    return 0;
}

static int Launch(const std::wstring& executable, const std::wstring& outputPath) {
    STARTUPINFOW startup{sizeof(startup)};
    startup.lpDesktop = const_cast<wchar_t*>(L"winsta0\\default");
    PROCESS_INFORMATION process{};
    std::vector<wchar_t> command(executable.begin(), executable.end());
    command.push_back(L'\0');
    std::wstring directory = std::filesystem::path(executable).parent_path().wstring();
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, directory.c_str(), &startup, &process)) return 5;
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    std::this_thread::sleep_for(std::chrono::seconds(5));
    return WriteWindows(outputPath);
}

static std::wstring QuoteArgument(const std::wstring& value) {
    std::wstring quoted = L"\"";
    for (wchar_t ch : value) {
        if (ch == L'\"') quoted += L'\\';
        quoted += ch;
    }
    quoted += L"\"";
    return quoted;
}

static int LaunchCommand(int argc, wchar_t** argv) {
    const std::wstring executable = argv[2];
    std::wstring command = QuoteArgument(executable);
    for (int index = 3; index < argc; ++index) command += L" " + QuoteArgument(argv[index]);
    STARTUPINFOW startup{sizeof(startup)};
    startup.lpDesktop = const_cast<wchar_t*>(L"winsta0\\default");
    PROCESS_INFORMATION process{};
    std::vector<wchar_t> commandBuffer(command.begin(), command.end());
    commandBuffer.push_back(L'\0');
    std::wstring directory = std::filesystem::path(executable).parent_path().wstring();
    if (!CreateProcessW(executable.c_str(), commandBuffer.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
            nullptr, directory.c_str(), &startup, &process)) return 5;
    CloseHandle(process.hThread);
    DWORD wait = WaitForSingleObject(process.hProcess, 15000);
    if (wait == WAIT_TIMEOUT) {
        TerminateProcess(process.hProcess, 124);
        WaitForSingleObject(process.hProcess, 5000);
    }
    DWORD exitCode = 125;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hProcess);
    return static_cast<int>(exitCode);
}

static bool SaveWindowBmp(HWND hwnd, const std::wstring& outputPath) {
    RECT rect{};
    if (!GetWindowRect(hwnd, &rect)) return false;
    const int width = rect.right - rect.left;
    const int height = rect.bottom - rect.top;
    HDC screen = GetDC(nullptr);
    HDC memory = CreateCompatibleDC(screen);
    HBITMAP bitmap = CreateCompatibleBitmap(screen, width, height);
    HGDIOBJ previous = SelectObject(memory, bitmap);
    bool printed = PrintWindow(hwnd, memory, PW_RENDERFULLCONTENT) == TRUE;
    BITMAPINFOHEADER header{};
    header.biSize = sizeof(header);
    header.biWidth = width;
    header.biHeight = -height;
    header.biPlanes = 1;
    header.biBitCount = 32;
    header.biCompression = BI_RGB;
    std::vector<unsigned char> pixels(static_cast<size_t>(width) * height * 4);
    bool copied = GetDIBits(memory, bitmap, 0, height, pixels.data(), reinterpret_cast<BITMAPINFO*>(&header), DIB_RGB_COLORS) != 0;
    SelectObject(memory, previous);
    DeleteObject(bitmap);
    DeleteDC(memory);
    ReleaseDC(nullptr, screen);
    if (!printed || !copied) return false;
    std::filesystem::create_directories(std::filesystem::path(outputPath).parent_path());
    std::ofstream output(std::filesystem::path(outputPath), std::ios::binary);
    BITMAPFILEHEADER file{};
    file.bfType = 0x4D42;
    file.bfOffBits = sizeof(file) + sizeof(header);
    file.bfSize = file.bfOffBits + static_cast<DWORD>(pixels.size());
    output.write(reinterpret_cast<const char*>(&file), sizeof(file));
    output.write(reinterpret_cast<const char*>(&header), sizeof(header));
    output.write(reinterpret_cast<const char*>(pixels.data()), static_cast<std::streamsize>(pixels.size()));
    return output.good();
}

static bool SaveWindowScreenBmp(HWND hwnd, const std::wstring& outputPath, bool clientOnly = false) {
    RECT rect{};
    if (clientOnly) {
        if (!GetClientRect(hwnd, &rect)) return false;
        POINT topLeft{rect.left, rect.top};
        POINT bottomRight{rect.right, rect.bottom};
        if (!ClientToScreen(hwnd, &topLeft) || !ClientToScreen(hwnd, &bottomRight)) return false;
        rect = {topLeft.x, topLeft.y, bottomRight.x, bottomRight.y};
    } else if (!GetWindowRect(hwnd, &rect)) return false;
    const int width = rect.right - rect.left;
    const int height = rect.bottom - rect.top;
    HDC screen = GetDC(nullptr);
    HDC memory = CreateCompatibleDC(screen);
    HBITMAP bitmap = CreateCompatibleBitmap(screen, width, height);
    HGDIOBJ previous = SelectObject(memory, bitmap);
    bool copiedScreen = BitBlt(memory, 0, 0, width, height, screen, rect.left, rect.top, SRCCOPY | CAPTUREBLT) == TRUE;
    BITMAPINFOHEADER header{};
    header.biSize = sizeof(header);
    header.biWidth = width;
    header.biHeight = -height;
    header.biPlanes = 1;
    header.biBitCount = 32;
    header.biCompression = BI_RGB;
    std::vector<unsigned char> pixels(static_cast<size_t>(width) * height * 4);
    bool copiedBits = GetDIBits(memory, bitmap, 0, height, pixels.data(), reinterpret_cast<BITMAPINFO*>(&header), DIB_RGB_COLORS) != 0;
    SelectObject(memory, previous);
    DeleteObject(bitmap);
    DeleteDC(memory);
    ReleaseDC(nullptr, screen);
    if (!copiedScreen || !copiedBits) return false;
    std::filesystem::create_directories(std::filesystem::path(outputPath).parent_path());
    std::ofstream output(std::filesystem::path(outputPath), std::ios::binary);
    BITMAPFILEHEADER file{};
    file.bfType = 0x4D42;
    file.bfOffBits = sizeof(file) + sizeof(header);
    file.bfSize = file.bfOffBits + static_cast<DWORD>(pixels.size());
    output.write(reinterpret_cast<const char*>(&file), sizeof(file));
    output.write(reinterpret_cast<const char*>(&header), sizeof(header));
    output.write(reinterpret_cast<const char*>(pixels.data()), static_cast<std::streamsize>(pixels.size()));
    return output.good();
}

static int Capture(const std::wstring& processName, const std::wstring& outputPath) {
    auto windows = EnumerateWindows();
    auto found = std::find_if(windows.begin(), windows.end(), [&](const WindowInfo& item) {
        return _wcsicmp(item.process.c_str(), processName.c_str()) == 0;
    });
    if (found == windows.end()) return 6;
    return SaveWindowBmp(found->hwnd, outputPath) ? 0 : 7;
}

static int CaptureScreen(const std::wstring& processName, const std::wstring& outputPath) {
    auto windows = EnumerateWindows();
    auto found = std::find_if(windows.begin(), windows.end(), [&](const WindowInfo& item) {
        return _wcsicmp(item.process.c_str(), processName.c_str()) == 0;
    });
    if (found == windows.end()) return 6;
    return SaveWindowScreenBmp(found->hwnd, outputPath) ? 0 : 7;
}

static int CaptureClientScreen(const std::wstring& processName, const std::wstring& outputPath) {
    auto windows = EnumerateWindows();
    auto found = std::find_if(windows.begin(), windows.end(), [&](const WindowInfo& item) {
        return _wcsicmp(item.process.c_str(), processName.c_str()) == 0;
    });
    if (found == windows.end()) return 6;
    return SaveWindowScreenBmp(found->hwnd, outputPath, true) ? 0 : 7;
}

static int RaiseWindow(const std::wstring& processName) {
    auto windows = EnumerateWindows();
    auto found = std::find_if(windows.begin(), windows.end(), [&](const WindowInfo& item) {
        return _wcsicmp(item.process.c_str(), processName.c_str()) == 0;
    });
    if (found == windows.end()) return 6;
    ShowWindow(found->hwnd, SW_RESTORE);
    if (!SetWindowPos(found->hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)) return 8;
    if (!SetWindowPos(found->hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)) return 8;
    return 0;
}

static int MinimizeWindows(const std::wstring& processName) {
    auto windows = EnumerateWindows();
    int count = 0;
    for (const auto& item : windows) {
        if (_wcsicmp(item.process.c_str(), processName.c_str()) != 0) continue;
        if (PostMessageW(item.hwnd, WM_SYSCOMMAND, SC_MINIMIZE, 0)) ++count;
    }
    return count > 0 ? 0 : 6;
}

static int CloseByTitle(const std::wstring& titleFragment) {
    auto windows = EnumerateWindows();
    auto found = std::find_if(windows.begin(), windows.end(), [&](const WindowInfo& item) {
        return item.title.find(titleFragment) != std::wstring::npos;
    });
    if (found == windows.end()) return 6;
    return PostMessageW(found->hwnd, WM_CLOSE, 0, 0) ? 0 : 8;
}

static bool SendVirtualKey(WORD key, bool down) {
    INPUT input{};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = key;
    input.ki.dwFlags = down ? 0 : KEYEVENTF_KEYUP;
    return SendInput(1, &input, sizeof(input)) == 1;
}

static bool SendUnicodeText(const std::wstring& text) {
    for (wchar_t value : text) {
        INPUT inputs[2]{};
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wScan = value;
        inputs[0].ki.dwFlags = KEYEVENTF_UNICODE;
        inputs[1] = inputs[0];
        inputs[1].ki.dwFlags |= KEYEVENTF_KEYUP;
        if (SendInput(2, inputs, sizeof(INPUT)) != 2) return false;
    }
    return true;
}

static int LaunchThroughRunDialog(const std::wstring& executable) {
    if (!SendVirtualKey(VK_LWIN, true) || !SendVirtualKey('R', true)
        || !SendVirtualKey('R', false) || !SendVirtualKey(VK_LWIN, false)) return 9;
    std::this_thread::sleep_for(std::chrono::milliseconds(700));
    SendVirtualKey(VK_CONTROL, true);
    SendVirtualKey('A', true);
    SendVirtualKey('A', false);
    SendVirtualKey(VK_CONTROL, false);
    if (!SendUnicodeText(executable)) return 9;
    std::this_thread::sleep_for(std::chrono::milliseconds(300));
    if (!SendVirtualKey(VK_RETURN, true) || !SendVirtualKey(VK_RETURN, false)) return 9;
    return 0;
}

int wmain(int argc, wchar_t** argv) {
    if (argc < 3) return 1;
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    HDESK desktop = OpenInputDesktop(0, FALSE, DESKTOP_CREATEWINDOW | DESKTOP_ENUMERATE | DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP | DESKTOP_WRITEOBJECTS);
    if (!desktop) return 2;
    if (!SetThreadDesktop(desktop)) { CloseDesktop(desktop); return 3; }
    int result = 1;
    if (_wcsicmp(argv[1], L"list") == 0 && argc == 3) result = WriteWindows(argv[2]);
    else if (_wcsicmp(argv[1], L"launch") == 0 && argc == 4) result = Launch(argv[2], argv[3]);
    else if (_wcsicmp(argv[1], L"launch-command") == 0 && argc >= 4) result = LaunchCommand(argc, argv);
    else if (_wcsicmp(argv[1], L"capture") == 0 && argc == 4) result = Capture(argv[2], argv[3]);
    else if (_wcsicmp(argv[1], L"capture-screen") == 0 && argc == 4) result = CaptureScreen(argv[2], argv[3]);
    else if (_wcsicmp(argv[1], L"capture-client-screen") == 0 && argc == 4) result = CaptureClientScreen(argv[2], argv[3]);
    else if (_wcsicmp(argv[1], L"raise") == 0 && argc == 3) result = RaiseWindow(argv[2]);
    else if (_wcsicmp(argv[1], L"minimize") == 0 && argc == 3) result = MinimizeWindows(argv[2]);
    else if (_wcsicmp(argv[1], L"close-title") == 0 && argc == 3) result = CloseByTitle(argv[2]);
    else if (_wcsicmp(argv[1], L"run-dialog") == 0 && argc == 3) result = LaunchThroughRunDialog(argv[2]);
    CloseDesktop(desktop);
    return result;
}
