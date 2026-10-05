#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <imm.h>
#include <string>

namespace {
struct Session {
    HWND window = nullptr;
    std::wstring text;
    bool composing = false;
    DWORD lastCommit = 0, lastStatus = 0;
    unsigned messages = 0, commits = 0;
    HWND previousWindow = nullptr;
    UINT previousMessage = 0;
    WPARAM previousParam = 0;
    LPARAM previousFlags = 0;
    DWORD previousTick = 0;
};
thread_local Session state;
std::wstring Escape(const std::wstring& value) {
    std::wstring output;
    for (wchar_t c : value) {
        if (c == L'"' || c == L'\\') { output += L'\\'; output += c; }
        else if (c == L'\r') output += L"\\r";
        else if (c == L'\n') output += L"\\n";
        else if (c == L'\t') output += L"\\t";
        else if (c >= 32) output += c;
    }
    return output;
}
void Notify(HWND window, bool status = false) {
    HWND receiver = FindWindowExW(HWND_MESSAGE, nullptr, nullptr, L"EnglishLearningAssistant.CommitReceiver");
    if (!receiver) return;
    RECT box{};
    GUITHREADINFO info{ sizeof(info) };
    if (GetGUIThreadInfo(GetCurrentThreadId(), &info) && info.hwndCaret) {
        POINT point{ info.rcCaret.left, info.rcCaret.top }; ClientToScreen(info.hwndCaret, &point);
        box = { point.x, point.y, point.x + 2, point.y + 24 };
    } else { GetWindowRect(window, &box); box.bottom = box.top + 32; }
    std::wstring payload = L"{\"command\":\"" + std::wstring(status ? L"windows-bridge-status" : L"windows-commit") + L"\",\"typed_only\":true,\"scope\":\""
        + std::to_wstring(GetCurrentProcessId()) + L":imm:" + std::to_wstring(GetCurrentThreadId()) + L":" + std::to_wstring(reinterpret_cast<UINT_PTR>(window))
        + L"\",\"text\":\"" + (status ? std::wstring() : Escape(state.text)) + L"\",\"composing\":" + (state.composing ? L"true" : L"false")
        + L",\"bridge_messages\":" + std::to_wstring(state.messages) + L",\"bridge_commits\":" + std::to_wstring(state.commits)
        + L",\"rect\":{\"left\":" + std::to_wstring(box.left) + L",\"top\":" + std::to_wstring(box.top)
        + L",\"right\":" + std::to_wstring(box.right) + L",\"bottom\":" + std::to_wstring(box.bottom) + L"}}";
    COPYDATASTRUCT data{ 0x454C494D, static_cast<DWORD>((payload.size() + 1) * sizeof(wchar_t)), const_cast<wchar_t*>(payload.c_str()) };
    DWORD_PTR ignored = 0;
    // 不做网络或磁盘操作；助手繁忙时最多等 10 ms，始终继续应用原消息。
    SendMessageTimeoutW(receiver, WM_COPYDATA, reinterpret_cast<WPARAM>(window), reinterpret_cast<LPARAM>(&data), SMTO_ABORTIFHUNG | SMTO_BLOCK, 10, &ignored);
}
void Append(HWND window, const std::wstring& text) {
    state.text += text;
    if (state.text.size() > 512) {
        size_t start = state.text.size() - 512;
        if (state.text[start] >= 0xDC00 && state.text[start] <= 0xDFFF) start++;
        state.text.erase(0, start);
    }
    Notify(window);
}
void Observe(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (!window || (GetWindowLongPtrW(window, GWL_STYLE) & ES_PASSWORD)) return;
    state.messages++;
    bool input = message == WM_IME_STARTCOMPOSITION || message == WM_IME_COMPOSITION || message == WM_IME_ENDCOMPOSITION
        || message == WM_KEYDOWN || message == WM_CHAR || message == WM_KILLFOCUS || message == WM_SETFOCUS || message == WM_LBUTTONDOWN;
    if (!input) {
        if (GetTickCount() - state.lastStatus > 1000) { state.lastStatus = GetTickCount(); Notify(window, true); }
        return;
    }
    DWORD tick = GetTickCount();
    if (window == state.previousWindow && message == state.previousMessage && wParam == state.previousParam && lParam == state.previousFlags && tick - state.previousTick < 10) return;
    state.previousWindow = window; state.previousMessage = message; state.previousParam = wParam; state.previousFlags = lParam; state.previousTick = tick;
    if (window != state.window) { state.window = window; state.text.clear(); state.composing = false; }
    if (message == WM_IME_STARTCOMPOSITION) { state.composing = true; Notify(window); }
    else if (message == WM_IME_COMPOSITION) {
        if (lParam & GCS_RESULTSTR) {
            HIMC context = ImmGetContext(window);
            if (context) {
                wchar_t buffer[513]{};
                LONG count = ImmGetCompositionStringW(context, GCS_RESULTSTR, buffer, 512 * sizeof(wchar_t));
                ImmReleaseContext(window, context);
                if (count > 0 && count <= 1024) {
                    state.composing = false; state.commits++; state.lastCommit = GetTickCount();
                    Append(window, std::wstring(buffer, count / sizeof(wchar_t)));
                }
            }
        } else { state.composing = true; Notify(window); }
    } else if (message == WM_IME_ENDCOMPOSITION) { state.composing = false; Notify(window); }
    else if (message == WM_KILLFOCUS || message == WM_SETFOCUS || message == WM_LBUTTONDOWN) {
        state.text.clear(); state.composing = false; Notify(window);
    } else if (message == WM_KEYDOWN && !state.composing) {
        bool control = GetKeyState(VK_CONTROL) < 0;
        if (control || wParam == VK_DELETE || (wParam >= VK_PRIOR && wParam <= VK_DOWN) || wParam == VK_ESCAPE || wParam == VK_TAB) { state.text.clear(); Notify(window); }
        else if (wParam == VK_BACK && !state.text.empty()) {
            if (state.text.back() >= 0xDC00 && state.text.back() <= 0xDFFF) state.text.pop_back();
            if (!state.text.empty()) state.text.pop_back(); Notify(window);
        } else if (wParam == VK_RETURN) {
            if (GetKeyState(VK_SHIFT) < 0) Append(window, L"\n");
            else { state.text.clear(); Notify(window); }
        }
    } else if (message == WM_CHAR && !state.composing) {
        wchar_t c = static_cast<wchar_t>(wParam);
        if ((c >= 32 && c < 127) || (c >= 0x3000 && c <= 0x303F) || (c >= 0xFF00 && c <= 0xFFEF)) {
            if (GetTickCount() - state.lastCommit > 40) Append(window, std::wstring(1, c));
        }
    }
    if (GetTickCount() - state.lastStatus > 1000) { state.lastStatus = GetTickCount(); Notify(window, true); }
}
}
extern "C" __declspec(dllexport) LRESULT CALLBACK ObserveSent(int code, WPARAM wParam, LPARAM lParam) {
    if (code >= 0) { auto message = reinterpret_cast<CWPSTRUCT*>(lParam); Observe(message->hwnd, message->message, message->wParam, message->lParam); }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}
extern "C" __declspec(dllexport) LRESULT CALLBACK ObservePosted(int code, WPARAM wParam, LPARAM lParam) {
    if (code >= 0 && wParam == PM_REMOVE) { auto message = reinterpret_cast<MSG*>(lParam); Observe(message->hwnd, message->message, message->wParam, message->lParam); }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
