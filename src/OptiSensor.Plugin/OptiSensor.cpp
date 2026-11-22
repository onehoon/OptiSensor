#include <Windows.h>
#include <winhttp.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <charconv>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include <memory>
#include <optional>
#include <string>
#include <string_view>

#include "optiscaler_plugin.h"
#include "LibreBridge.h"

#if __has_include("OptiSensorBuildInfo.generated.h")
#include "OptiSensorBuildInfo.generated.h"
#else
#define OPTISENSOR_BUILD_RUN_ID 0ULL
#define OPTISENSOR_BUILD_SHA "local"
#endif

#pragma comment(lib, "winhttp.lib")

extern "C" __declspec(dllexport) bool OptiPluginInit(const OptiPluginApi* api, OptiPlugin* plugin);

namespace LibreBridge = OptiSensorPlugin::LibreBridge;

namespace
{
constexpr std::uint64_t kBuildRunId = OPTISENSOR_BUILD_RUN_ID;
constexpr char kBuildSha[] = OPTISENSOR_BUILD_SHA;
constexpr std::size_t kMaxUpdateResponseBytes = 64 * 1024;
constexpr ULONGLONG kLibrePollIntervalMs = 1500;
constexpr ULONGLONG kTabActiveWindowMs = 3000;

const OptiPluginApi* g_api = nullptr;
OptiPlugin* g_plugin = nullptr;
const char* g_saveStatus = nullptr;
std::atomic<std::uint64_t> g_lastTabVisit{0};
std::atomic<std::shared_ptr<const LibreBridge::SensorSnapshot>> g_sensorSnapshot;
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

DWORD WINAPI SensorPollingWorker(LPVOID)
{
    std::optional<LibreBridge::SensorSnapshot> lastGood;
    std::uint32_t failedPollCount = 0;
    ULONGLONG nextPoll = 0;
    bool updateCheckComplete = false;

    for (;;)
    {
        const ULONGLONG now = GetTickCount64();
        const std::uint64_t lastTabVisit = g_lastTabVisit.load(std::memory_order_acquire);
        const bool tabActive = lastTabVisit != 0 && now - lastTabVisit <= kTabActiveWindowMs;
        bool pollAttempted = false;
        if (!tabActive)
        {
            nextPoll = 0;
        }
        else if (nextPoll == 0 || now >= nextPoll)
        {
            try
            {
                std::string response;
                LibreBridge::SensorSnapshot parsed;
                pollAttempted = true;
                if (LibreBridge::ReadLocalResponse(response) && LibreBridge::ParseSnapshot(response, parsed))
                {
                    failedPollCount = 0;
                    lastGood = std::move(parsed);
                    g_sensorSnapshot.store(
                        std::make_shared<const LibreBridge::SensorSnapshot>(*lastGood),
                        std::memory_order_release);
                }
                else
                {
                    failedPollCount = std::min<std::uint32_t>(failedPollCount + 1, 3);
                    const auto* previous = lastGood.has_value() ? &*lastGood : nullptr;
                    g_sensorSnapshot.store(
                        std::make_shared<const LibreBridge::SensorSnapshot>(
                            LibreBridge::MakeFailedPollSnapshot(previous, failedPollCount)),
                        std::memory_order_release);
                }
            }
            catch (...)
            {
                failedPollCount = std::min<std::uint32_t>(failedPollCount + 1, 3);
                try
                {
                    const auto* previous = lastGood.has_value() ? &*lastGood : nullptr;
                    g_sensorSnapshot.store(
                        std::make_shared<const LibreBridge::SensorSnapshot>(
                            LibreBridge::MakeFailedPollSnapshot(previous, failedPollCount)),
                        std::memory_order_release);
                }
                catch (...)
                {
                }
            }
            nextPoll = GetTickCount64() + kLibrePollIntervalMs;
        }

        if (!updateCheckComplete && (pollAttempted || !tabActive))
        {
            updateCheckComplete = true;
            try
            {
                g_updateStatus.store(CheckUpdateStatus(), std::memory_order_release);
            }
            catch (...)
            {
                g_updateStatus.store(UpdateStatus::Unavailable, std::memory_order_release);
            }
            if (tabActive)
            {
                nextPoll = GetTickCount64() + kLibrePollIntervalMs;
            }
        }

        Sleep(tabActive ? 200 : 1000);
    }
}

void DrawSensor(const LibreBridge::SensorReading& reading)
{
    bool visible = g_api->GetBool(g_plugin, reading.settingKey.c_str(), true);
    std::string checkboxLabel = reading.label;
    checkboxLabel += " (";
    checkboxLabel += reading.sensorType;
    checkboxLabel += ")##";
    checkboxLabel += reading.settingKey;

    if (g_api->Checkbox(checkboxLabel.c_str(), &visible))
    {
        g_api->SetBool(g_plugin, reading.settingKey.c_str(), visible);
        g_saveStatus = nullptr;
    }

    g_api->SameLine();
    g_api->Text(reading.value.c_str());
}

void DrawHardwareGroups(const LibreBridge::SensorSnapshot& snapshot)
{
    g_api->Section("Hardware sensors");
    if (snapshot.readings.empty())
    {
        g_api->Text("No detected sensors");
        return;
    }

    std::size_t first = 0;
    while (first < snapshot.readings.size())
    {
        const LibreBridge::SensorReading& group = snapshot.readings[first];
        std::size_t end = first + 1;
        while (end < snapshot.readings.size() &&
               snapshot.readings[end].groupSettingKey == group.groupSettingKey)
        {
            ++end;
        }

        bool expanded = g_api->GetBool(g_plugin, group.groupSettingKey.c_str(), group.groupDefaultExpanded);
        std::string header = expanded ? "[-] " : "[+] ";
        header += group.device;
        header += " [";
        header.append(group.groupSettingKey.data() + (sizeof("GroupExpanded_") - 1), 8);
        header += "] (";
        header += std::to_string(end - first);
        header += " sensors)##";
        header += group.groupSettingKey;
        if (g_api->Button(header.c_str()))
        {
            expanded = !expanded;
            g_api->SetBool(g_plugin, group.groupSettingKey.c_str(), expanded);
            g_saveStatus = nullptr;
        }

        if (expanded)
        {
            for (std::size_t index = first; index < end; ++index)
            {
                DrawSensor(snapshot.readings[index]);
            }
        }

        first = end;
    }
}

void DrawTab(void*)
{
    g_lastTabVisit.store(GetTickCount64(), std::memory_order_release);
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

    const auto snapshot = g_sensorSnapshot.load(std::memory_order_acquire);
    if (snapshot == nullptr || !snapshot->hasValidSnapshot)
    {
        g_api->Text("LibreHardwareMonitor web server unavailable");
    }
    else
    {
        if (snapshot->unavailable)
        {
            g_api->Text("LibreHardwareMonitor web server unavailable");
        }
        else if (!snapshot->readings.empty())
        {
            if (snapshot->totalSensorCount > snapshot->readings.size())
            {
                const std::string countMessage = "Showing " + std::to_string(snapshot->readings.size()) +
                    " / " + std::to_string(snapshot->totalSensorCount) + " sensors; snapshot limit reached.";
                g_api->Text(countMessage.c_str());
            }
            DrawHardwareGroups(*snapshot);
        }
        else
        {
            g_api->Section("Hardware sensors");
            g_api->Text("No detected sensors");
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

    // Keep all polling, parsing, and the optional update request away from the render thread.
    HANDLE sensorThread = CreateThread(nullptr, 0, SensorPollingWorker, nullptr, 0, nullptr);
    if (sensorThread != nullptr)
    {
        CloseHandle(sensorThread);
    }
    else
    {
        try
        {
            g_sensorSnapshot.store(
                std::make_shared<const LibreBridge::SensorSnapshot>(
                    LibreBridge::MakeFailedPollSnapshot(nullptr, 1)),
                std::memory_order_release);
        }
        catch (...)
        {
        }
        g_updateStatus.store(UpdateStatus::Unavailable, std::memory_order_release);
    }

    const std::string buildInfo = "OptiSensor plugin initialized (Build-Run-ID " +
        std::to_string(kBuildRunId) + ", Build-SHA " + kBuildSha + ")";
    Log(OptiLog_Info, buildInfo.c_str());
    return true;
}
