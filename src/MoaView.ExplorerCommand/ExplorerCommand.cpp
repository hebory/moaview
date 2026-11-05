#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <wrl/client.h>
#include <atomic>
#include <new>
#include <string>

namespace
{
    constexpr CLSID CommandId = { 0xbecbf110, 0xb45c, 0x4c72, { 0xa5, 0xca, 0x67, 0xe0, 0xba, 0xfb, 0xd2, 0x99 } };
    HMODULE module;
    std::atomic_ulong objectCount{};
    std::atomic_ulong serverLocks{};

    HRESULT GetApplicationPath(std::wstring& path)
    {
        path.resize(32768);
        const DWORD length = GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
        if (!length) return HRESULT_FROM_WIN32(GetLastError());
        if (length == path.size()) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
        path.resize(length);
        const auto separator = path.find_last_of(L'\\');
        if (separator == std::wstring::npos) return E_FAIL;
        path.replace(separator + 1, std::wstring::npos, L"MoaView.exe");
        return S_OK;
    }

    class Command final : public IExplorerCommand
    {
        std::atomic_ulong references{1};

    public:
        Command() { ++objectCount; }
        ~Command() { --objectCount; }

        IFACEMETHODIMP QueryInterface(REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (iid != IID_IUnknown && iid != __uuidof(IExplorerCommand)) return E_NOINTERFACE;
            *result = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override { return ++references; }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            const ULONG remaining = --references;
            if (!remaining) delete this;
            return remaining;
        }

        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* title) override
        {
            if (!title) return E_POINTER;
            const bool korean = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_KOREAN;
            return SHStrDupW(korean ? L"MoaView로 미리보기" : L"Preview with MoaView", title);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            if (!icon) return E_POINTER;
            *icon = nullptr;
            try
            {
                std::wstring path;
                const HRESULT hr = GetApplicationPath(path);
                if (FAILED(hr)) return hr;
                return SHStrDupW((path + L",0").c_str(), icon);
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
        {
            if (tip) *tip = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetCanonicalName(GUID* name) override
        {
            if (!name) return E_POINTER;
            *name = CommandId;
            return S_OK;
        }

        IFACEMETHODIMP GetState(IShellItemArray* selection, BOOL, EXPCMDSTATE* state) override
        {
            if (!state) return E_POINTER;
            DWORD count = 0;
            *state = selection && SUCCEEDED(selection->GetCount(&count)) && count == 1 ? ECS_ENABLED : ECS_HIDDEN;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray* selection, IBindCtx*) override
        {
            EXPCMDSTATE state;
            GetState(selection, FALSE, &state);
            if (state != ECS_ENABLED) return E_INVALIDARG;

            Microsoft::WRL::ComPtr<IShellItem> item;
            HRESULT hr = selection->GetItemAt(0, &item);
            if (FAILED(hr)) return hr;
            SFGAOF attributes = 0;
            hr = item->GetAttributes(SFGAO_FILESYSTEM | SFGAO_FOLDER, &attributes);
            if (FAILED(hr)) return hr;
            if (attributes != SFGAO_FILESYSTEM) return E_INVALIDARG;

            PWSTR selectedPath = nullptr;
            hr = item->GetDisplayName(SIGDN_FILESYSPATH, &selectedPath);
            if (FAILED(hr)) return hr;
            try
            {
                std::wstring applicationPath;
                hr = GetApplicationPath(applicationPath);
                if (SUCCEEDED(hr))
                {
                    // Both values are Windows file paths. Pass them directly to CreateProcess,
                    // so metacharacters in a filename never become shell commands.
                    std::wstring arguments = L"\"" + applicationPath + L"\" --preview \"" + selectedPath + L"\"";
                    STARTUPINFOW startup{};
                    startup.cb = sizeof(startup);
                    PROCESS_INFORMATION process{};
                    if (!CreateProcessW(applicationPath.c_str(), arguments.data(), nullptr, nullptr, FALSE,
                        0, nullptr, nullptr, &startup, &process))
                    {
                        hr = HRESULT_FROM_WIN32(GetLastError());
                    }
                    else
                    {
                        CloseHandle(process.hThread);
                        CloseHandle(process.hProcess);
                    }
                }
            }
            catch (const std::bad_alloc&) { hr = E_OUTOFMEMORY; }
            CoTaskMemFree(selectedPath);
            return hr;
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            if (!flags) return E_POINTER;
            *flags = ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (commands) *commands = nullptr;
            return E_NOTIMPL;
        }
    };

    class CommandFactory final : public IClassFactory
    {
        std::atomic_ulong references{1};

    public:
        CommandFactory() { ++objectCount; }
        ~CommandFactory() { --objectCount; }

        IFACEMETHODIMP QueryInterface(REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
            *result = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override { return ++references; }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            const ULONG remaining = --references;
            if (!remaining) delete this;
            return remaining;
        }

        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (outer) return CLASS_E_NOAGGREGATION;
            const auto command = new (std::nothrow) Command;
            if (!command) return E_OUTOFMEMORY;
            const HRESULT hr = command->QueryInterface(iid, result);
            command->Release();
            return hr;
        }

        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            if (lock) ++serverLocks;
            else --serverLocks;
            return S_OK;
        }
    };
}

BOOL WINAPI DllMain(HMODULE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) module = instance;
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** result)
{
    if (!result) return E_POINTER;
    *result = nullptr;
    if (clsid != CommandId) return CLASS_E_CLASSNOTAVAILABLE;
    const auto factory = new (std::nothrow) CommandFactory;
    if (!factory) return E_OUTOFMEMORY;
    const HRESULT hr = factory->QueryInterface(iid, result);
    factory->Release();
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return objectCount == 0 && serverLocks == 0 ? S_OK : S_FALSE;
}
