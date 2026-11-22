#include <Windows.h>
#include <winhttp.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <charconv>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include <string>
#include <string_view>

#include "optiscaler_plugin.h"
#include "../OptiSensor.Native/OptiSensorNative.h"

#if __has_include("OptiSensorBuildInfo.generated.h")
#include "OptiSensorBuildInfo.generated.h"
#else
#define OPTISENSOR_BUILD_RUN_ID 0ULL
#define OPTISENSOR_BUILD_SHA "local"
#endif

#pragma comment(lib, "winhttp.lib")

extern "C" __declspec(dllexport) bool OptiPluginInit(const OptiPluginApi* api, OptiPlugin* plugin);

namespace
{
constexpr std::uint64_t kBuildRunId = OPTISENSOR_BUILD_RUN_ID;
constexpr char kBuildSha[] = OPTISENSOR_BUILD_SHA;
constexpr std::size_t kMaxUpdateResponseBytes = 64 * 1024;

const OptiPluginApi* g_api = nullptr;
OptiPlugin* g_plugin = nullptr;
const char* g_saveStatus = nullptr;

using StartFn = int32_t(__cdecl*)();
using GetSnapshotFn = int32_t(__cdecl*)(OptiSensorReading*, int32_t, OptiSensorSnapshotInfo*);
using StopFn = int32_t(__cdecl*)();

HMODULE g_sensorModule = nullptr;
StartFn g_start = nullptr;
GetSnapshotFn g_getSnapshot = nullptr;
StopFn g_stop = nullptr;
std::array<OptiSensorReading, OPTISENSOR_MAX_READINGS> g_readings{};
OptiSensorSnapshotInfo g_snapshotInfo{};
int32_t g_readingCount = 0;
ULONGLONG g_lastSnapshotRead = 0;
std::string g_backendError;
std::string g_snapshotError;
std::atomic<bool> g_backendInitFinished{false};
enum class UpdateStatus : std::uint8_t
{
    Checking,
    UpToDate,
    UpdateAvailable,
    Unavailable,
    LocalBuild
};
std::atomic<UpdateStatus> g_updateStatus{UpdateStatus::Checking};

struct WinHttpHandle
{
    HINTERNET value = nullptr;

    ~WinHttpHandle()
    {
        if (value != nullptr)
        {
            WinHttpCloseHandle(value);
        }
    }
};

bool ReadReleaseResponse(std::string& response)
{
    WinHttpHandle session{WinHttpOpen(
        L"OptiSensorPlugin/1.0",
        WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
        WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS,
        0)};
    if (session.value == nullptr || !WinHttpSetTimeouts(session.value, 3000, 3000, 3000, 3000))
    {
        return false;
    }

    WinHttpHandle connection{WinHttpConnect(session.value, L"api.github.com", INTERNET_DEFAULT_HTTPS_PORT, 0)};
    if (connection.value == nullptr)
    {
        return false;
    }

    WinHttpHandle request{WinHttpOpenRequest(
        connection.value,
        L"GET",
        L"/repos/onehoon/OptiSensor/releases/tags/OptiSensor.Plugin",
        nullptr,
        WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES,
        WINHTTP_FLAG_SECURE)};
    if (request.value == nullptr)
    {
        return false;
    }

    constexpr wchar_t headers[] =
        L"Accept: application/vnd.github+json\r\n"
        L"X-GitHub-Api-Version: 2022-11-28\r\n";
    if (!WinHttpSendRequest(
            request.value,
            headers,
            static_cast<DWORD>(-1),
            WINHTTP_NO_REQUEST_DATA,
            0,
            0,
            0) ||
        !WinHttpReceiveResponse(request.value, nullptr))
    {
        return false;
    }

    DWORD status = 0;
    DWORD statusSize = sizeof(status);
    if (!WinHttpQueryHeaders(
            request.value,
            WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
            WINHTTP_HEADER_NAME_BY_INDEX,
            &status,
            &statusSize,
            WINHTTP_NO_HEADER_INDEX) ||
        status != 200)
    {
        return false;
    }

    std::array<char, 4096> buffer{};
    for (;;)
    {
        DWORD available = 0;
        if (!WinHttpQueryDataAvailable(request.value, &available))
        {
            return false;
        }
        if (available == 0)
        {
            return true;
        }
        if (available > kMaxUpdateResponseBytes - response.size())
        {
            return false;
        }

        while (available > 0)
        {
            const DWORD requested = std::min(available, static_cast<DWORD>(buffer.size()));
            DWORD received = 0;
            if (!WinHttpReadData(request.value, buffer.data(), requested, &received))
            {
                return false;
            }
            if (received == 0)
            {
                break;
            }

            response.append(buffer.data(), received);
            available -= received;
        }
    }
}

bool ExtractReleaseBody(const std::string& response, std::string& body)
{
    constexpr std::string_view field = "\"body\"";
    const std::size_t fieldPosition = response.find(field);
    if (fieldPosition == std::string::npos)
    {
        return false;
    }

    std::size_t position = fieldPosition + field.size();
    const auto skipWhitespace = [&response](std::size_t& index)
    {
        while (index < response.size() &&
               (response[index] == ' ' || response[index] == '\t' || response[index] == '\r' || response[index] == '\n'))
        {
            ++index;
        }
    };
    skipWhitespace(position);
    if (position >= response.size() || response[position++] != ':')
    {
        return false;
    }
    skipWhitespace(position);
    if (position >= response.size() || response[position++] != '"')
    {
        return false;
    }

    body.clear();
    while (position < response.size())
    {
        const char value = response[position++];
        if (value == '"')
        {
            return true;
        }
        if (static_cast<unsigned char>(value) < 0x20)
        {
            return false;
        }
        if (value != '\\')
        {
            body.push_back(value);
            continue;
        }
        if (position >= response.size())
        {
            return false;
        }

        switch (response[position++])
        {
        case '"': body.push_back('"'); break;
        case '\\': body.push_back('\\'); break;
        case '/': body.push_back('/'); break;
        case 'b': body.push_back('\b'); break;
        case 'f': body.push_back('\f'); break;
        case 'n': body.push_back('\n'); break;
        case 'r': body.push_back('\r'); break;
        case 't': body.push_back('\t'); break;
        case 'u':
        {
            if (position + 4 > response.size())
            {
                return false;
            }
            unsigned int codePoint = 0;
            const auto result = std::from_chars(
                response.data() + position,
                response.data() + position + 4,
                codePoint,
                16);
            if (result.ec != std::errc{} || result.ptr != response.data() + position + 4)
            {
                return false;
            }
            position += 4;
            body.push_back(codePoint <= 0x7f ? static_cast<char>(codePoint) : '?');
            break;
        }
        default:
            return false;
        }
    }

    return false;
}

bool ParseReleaseRunId(const std::string& body, std::uint64_t& runId)
{
    constexpr std::string_view marker = "Build-Run-ID:";
    bool found = false;
    std::size_t lineStart = 0;
    while (lineStart <= body.size())
    {
        const std::size_t lineEnd = body.find('\n', lineStart);
        const std::size_t length = lineEnd == std::string::npos ? body.size() - lineStart : lineEnd - lineStart;
        std::string_view line(body.data() + lineStart, length);
        if (!line.empty() && line.back() == '\r')
        {
            line.remove_suffix(1);
        }
        while (!line.empty() && (line.front() == ' ' || line.front() == '\t'))
        {
            line.remove_prefix(1);
        }
        while (!line.empty() && (line.back() == ' ' || line.back() == '\t'))
        {
            line.remove_suffix(1);
        }

        if (line.starts_with(marker))
        {
            if (found)
            {
                return false;
            }
            std::string_view digits = line.substr(marker.size());
            while (!digits.empty() && (digits.front() == ' ' || digits.front() == '\t'))
            {
                digits.remove_prefix(1);
            }
            std::uint64_t parsed = 0;
            const auto result = std::from_chars(digits.data(), digits.data() + digits.size(), parsed);
            if (digits.empty() || result.ec != std::errc{} || result.ptr != digits.data() + digits.size())
            {
                return false;
            }
            runId = parsed;
            found = true;
        }

        if (lineEnd == std::string::npos)
        {
            break;
        }
        lineStart = lineEnd + 1;
    }

    return found;
}

bool IsNewerRunId(std::uint64_t remoteRunId, std::uint64_t localRunId)
{
    return localRunId != 0 && remoteRunId > localRunId;
}

UpdateStatus CheckUpdateStatus()
{
    if (kBuildRunId == 0)
    {
        return UpdateStatus::LocalBuild;
    }

    std::string response;
    std::string releaseBody;
    std::uint64_t remoteRunId = 0;
    if (!ReadReleaseResponse(response) ||
        !ExtractReleaseBody(response, releaseBody) ||
        !ParseReleaseRunId(releaseBody, remoteRunId))
    {
        // The optional network check must never interrupt the game.
        return UpdateStatus::Unavailable;
    }

    return IsNewerRunId(remoteRunId, kBuildRunId)
        ? UpdateStatus::UpdateAvailable
        : UpdateStatus::UpToDate;
}

bool IsWineOrProton()
{
    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    return ntdll != nullptr && GetProcAddress(ntdll, "wine_get_version") != nullptr;
}

bool HasRequiredApi(const OptiPluginApi* api)
{
    return OPTI_API_HAS(api, RegisterTab) &&
        OPTI_API_HAS(api, Section) &&
        OPTI_API_HAS(api, Text) &&
        OPTI_API_HAS(api, Checkbox) &&
        OPTI_API_HAS(api, SameLine) &&
        OPTI_API_HAS(api, Button) &&
        OPTI_API_HAS(api, GetBool) &&
        OPTI_API_HAS(api, SetBool) &&
        OPTI_API_HAS(api, SaveSettings) &&
        OPTI_API_HAS(api, Log);
}

void Log(OptiLogLevel level, const char* message)
{
    if (g_api != nullptr && g_plugin != nullptr && OPTI_API_HAS(g_api, Log))
    {
        g_api->Log(g_plugin, level, message);
    }
}

void SetBackendUnavailable()
{
    g_backendError = "Sensor monitoring unavailable.";
}

std::wstring GetPluginDirectory()
{
    HMODULE pluginModule = nullptr;
    const auto pluginAddress = reinterpret_cast<LPCWSTR>(&OptiPluginInit);
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            pluginAddress,
            &pluginModule))
    {
        return {};
    }

    std::array<wchar_t, 32768> path{};
    const DWORD length = GetModuleFileNameW(pluginModule, path.data(), static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size())
    {
        return {};
    }

    std::wstring result(path.data(), length);
    const size_t separator = result.find_last_of(L"\\/");
    if (separator == std::wstring::npos)
    {
        return {};
    }

    result.resize(separator + 1);
    return result;
}

void InitializeBackend()
{
    const std::wstring pluginDirectory = GetPluginDirectory();
    if (pluginDirectory.empty())
    {
        SetBackendUnavailable();
        return;
    }

    const std::wstring helperPath = pluginDirectory + L"OptiSensor.dll";
    g_sensorModule = LoadLibraryW(helperPath.c_str());
    if (g_sensorModule == nullptr)
    {
        SetBackendUnavailable();
        return;
    }

    g_start = reinterpret_cast<StartFn>(GetProcAddress(g_sensorModule, "OptiSensor_Start"));
    g_getSnapshot = reinterpret_cast<GetSnapshotFn>(GetProcAddress(g_sensorModule, "OptiSensor_GetSnapshot"));
    g_stop = reinterpret_cast<StopFn>(GetProcAddress(g_sensorModule, "OptiSensor_Stop"));
    if (g_start == nullptr || g_getSnapshot == nullptr || g_stop == nullptr)
    {
        SetBackendUnavailable();
        return;
    }

    const int32_t result = g_start();
    if (result < 0)
    {
        SetBackendUnavailable();
        return;
    }

    g_backendError.clear();
}

DWORD WINAPI InitializeBackendWorker(LPVOID)
{
    try
    {
        InitializeBackend();
    }
    catch (...)
    {
        SetBackendUnavailable();
    }

    g_backendInitFinished.store(true, std::memory_order_release);
    try
    {
        g_updateStatus.store(CheckUpdateStatus(), std::memory_order_release);
    }
    catch (...)
    {
        // Update checks are optional and must not affect sensor initialization or polling.
        g_updateStatus.store(UpdateStatus::Unavailable, std::memory_order_release);
    }
    return 0;
}

void RefreshSnapshot()
{
    if (g_getSnapshot == nullptr)
    {
        return;
    }

    const ULONGLONG now = GetTickCount64();
    if (now - g_lastSnapshotRead < 500)
    {
        return;
    }

    g_lastSnapshotRead = now;
    const int32_t result = g_getSnapshot(
        g_readings.data(), static_cast<int32_t>(g_readings.size()), &g_snapshotInfo);
    if (result < 0)
    {
        g_readingCount = 0;
        g_snapshotError = "Sensor monitoring unavailable.";
        return;
    }

    g_snapshotError.clear();
    g_readingCount = std::min(result, static_cast<int32_t>(g_readings.size()));
}

void DrawSensor(const OptiSensorReading& reading)
{
    bool visible = g_api->GetBool(g_plugin, reading.settingKey, true);
    std::string checkboxLabel = reading.label;
    checkboxLabel += " (";
    checkboxLabel += reading.sensorType;
    checkboxLabel += ")##";
    checkboxLabel += reading.settingKey;

    if (g_api->Checkbox(checkboxLabel.c_str(), &visible))
    {
        g_api->SetBool(g_plugin, reading.settingKey, visible);
        g_saveStatus = nullptr;
    }

    g_api->SameLine();
    char value[64]{};
    if (reading.available != 0)
    {
        if (reading.unit[0] == '\0')
        {
            std::snprintf(value, sizeof(value), "%.1f", reading.value);
        }
        else
        {
            std::snprintf(value, sizeof(value), "%.1f %s", reading.value, reading.unit);
        }
    }
    else
    {
        std::snprintf(value, sizeof(value), "N/A");
    }
    g_api->Text(value);
}

void DrawHardwareGroups()
{
    g_api->Section("Hardware sensors");
    if (g_readingCount == 0)
    {
        g_api->Text("No detected sensors");
        return;
    }

    int32_t first = 0;
    while (first < g_readingCount)
    {
        const OptiSensorReading& group = g_readings[static_cast<size_t>(first)];
        int32_t end = first + 1;
        while (end < g_readingCount &&
               std::strcmp(g_readings[static_cast<size_t>(end)].groupSettingKey, group.groupSettingKey) == 0)
        {
            ++end;
        }

        bool expanded = g_api->GetBool(g_plugin, group.groupSettingKey, group.groupDefaultExpanded != 0);
        std::string header = expanded ? "[-] " : "[+] ";
        header += group.device;
        header += " [";
        header.append(group.groupSettingKey + (sizeof("GroupExpanded_") - 1), 8);
        header += "] (";
        header += std::to_string(end - first);
        header += " sensors)##";
        header += group.groupSettingKey;
        if (g_api->Button(header.c_str()))
        {
            expanded = !expanded;
            g_api->SetBool(g_plugin, group.groupSettingKey, expanded);
            g_saveStatus = nullptr;
        }

        if (expanded)
        {
            for (int32_t index = first; index < end; ++index)
            {
                DrawSensor(g_readings[static_cast<size_t>(index)]);
            }
        }

        first = end;
    }
}

void DrawTab(void*)
{
    g_api->Section("Plugin update");
    const UpdateStatus updateStatus = g_updateStatus.load(std::memory_order_acquire);
    switch (updateStatus)
    {
    case UpdateStatus::Checking:
        g_api->Text("Checking for updates...");
        break;
    case UpdateStatus::UpToDate:
        g_api->Text("Up to date");
        break;
    case UpdateStatus::UpdateAvailable:
        if (OPTI_API_HAS(g_api, TextColored))
        {
            g_api->TextColored(1.0f, 0.72f, 0.20f, 1.0f, "Update Available");
        }
        else
        {
            g_api->Text("Update Available");
        }
        g_api->Text("https://github.com/onehoon/OptiSensor/releases/tag/OptiSensor.Plugin");
        break;
    case UpdateStatus::LocalBuild:
        g_api->Text("Local build");
        break;
    case UpdateStatus::Unavailable:
        // Silently ignore an optional network failure.
        break;
    }

    char buildLabel[96]{};
    if (kBuildRunId == 0)
    {
        std::snprintf(buildLabel, sizeof(buildLabel), "Build: local");
    }
    else
    {
        std::snprintf(buildLabel, sizeof(buildLabel), "Build: %llu (%.8s)",
                      static_cast<unsigned long long>(kBuildRunId), kBuildSha);
    }
    if (OPTI_API_HAS(g_api, TextDisabled))
    {
        g_api->TextDisabled(buildLabel);
    }
    else
    {
        g_api->Text(buildLabel);
    }

    if (!g_backendInitFinished.load(std::memory_order_acquire))
    {
        g_api->Text("Starting sensor backend...");
    }
    else
    {
        if (!g_backendError.empty())
        {
            g_api->Text("Sensor monitoring unavailable.");
        }
        else
        {
            RefreshSnapshot();
            if (!g_snapshotError.empty())
            {
                g_api->Text("Sensor monitoring unavailable.");
            }
            else if (g_snapshotInfo.status == OptiSensor_StatusLoading)
            {
                g_api->Text("Discovering sensors...");
            }
            else if (g_snapshotInfo.status == OptiSensor_StatusError || g_snapshotInfo.status == OptiSensor_StatusStopped)
            {
                g_api->Text("Sensor monitoring unavailable.");
            }
            else if (g_snapshotInfo.message[0] != '\0')
            {
                g_api->Text(g_snapshotInfo.message);
            }

            bool snapshotReady = g_snapshotError.empty() &&
                g_snapshotInfo.status == OptiSensor_StatusReady;
            if (snapshotReady && g_snapshotInfo.totalCount > g_readingCount)
            {
                char countMessage[96]{};
                std::snprintf(countMessage, sizeof(countMessage), "Showing %d / %d sensors; snapshot limit reached.",
                              g_readingCount, g_snapshotInfo.totalCount);
                g_api->Text(countMessage);
            }
            if (snapshotReady)
            {
                DrawHardwareGroups();
            }
        }
    }

    if (g_api->Button("Save"))
    {
        if (g_api->SaveSettings(g_plugin))
        {
            g_saveStatus = "Settings saved";
            Log(OptiLog_Info, "Settings saved");
        }
        else
        {
            g_saveStatus = "Failed to save settings";
            Log(OptiLog_Error, "Failed to save settings");
        }
    }

    if (g_saveStatus != nullptr)
    {
        g_api->Text(g_saveStatus);
    }
}
}

extern "C" __declspec(dllexport) bool OptiPluginInit(const OptiPluginApi* api, OptiPlugin* plugin)
{
    if (api == nullptr || plugin == nullptr)
    {
        return false;
    }

    if (IsWineOrProton())
    {
        return false;
    }

    g_api = api;
    g_plugin = plugin;
    g_saveStatus = nullptr;

    if (!HasRequiredApi(api))
    {
        Log(OptiLog_Error, "Plugin SDK is missing a required API function");
        g_api = nullptr;
        g_plugin = nullptr;
        return false;
    }

    if (!api->RegisterTab(plugin, "OptiSensor", "All detected hardware sensors", DrawTab, nullptr))
    {
        Log(OptiLog_Error, "Failed to register the OptiSensor tab");
        g_api = nullptr;
        g_plugin = nullptr;
        return false;
    }

    g_backendInitFinished.store(false, std::memory_order_relaxed);
    // OptiPluginInit may run under DllMain's loader lock; defer nested LoadLibrary and Native AOT startup.
    HANDLE initThread = CreateThread(nullptr, 0, InitializeBackendWorker, nullptr, 0, nullptr);
    if (initThread != nullptr)
    {
        CloseHandle(initThread);
    }
    else
    {
        SetBackendUnavailable();
        g_backendInitFinished.store(true, std::memory_order_release);
    }

    const std::string buildInfo = "OptiSensor plugin initialized (Build-Run-ID " +
        std::to_string(kBuildRunId) + ", Build-SHA " + kBuildSha + ")";
    Log(OptiLog_Info, buildInfo.c_str());
    return true;
}
