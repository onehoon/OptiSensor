#include <Windows.h>

#include <algorithm>
#include <cstdint>
#include <memory>
#include <optional>
#include <string>

#include "LibreBridge.h"
#include "PluginRuntime.h"
#include "PluginTab.h"
#include "UpdateChecker.h"
#include "optiscaler_plugin.h"

#if __has_include("OptiSensorBuildInfo.generated.h")
#include "OptiSensorBuildInfo.generated.h"
#else
#define OPTISENSOR_BUILD_RUN_ID 0ULL
#define OPTISENSOR_BUILD_SHA "local"
#endif

extern "C" __declspec(dllexport) bool OptiPluginInit(const OptiPluginApi* api, OptiPlugin* plugin);

namespace LibreBridge = OptiSensorPlugin::LibreBridge;
namespace PluginTab = OptiSensorPlugin::PluginTab;
namespace UpdateChecker = OptiSensorPlugin::UpdateChecker;

namespace
{
constexpr std::uint64_t kBuildRunId = OPTISENSOR_BUILD_RUN_ID;
constexpr char kBuildSha[] = OPTISENSOR_BUILD_SHA;
constexpr ULONGLONG kLibrePollIntervalMs = 1500;
constexpr ULONGLONG kTabActiveWindowMs = 3000;

OptiSensorPlugin::PluginRuntime g_runtime{};

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
    if (g_runtime.api != nullptr && g_runtime.plugin != nullptr && OPTI_API_HAS(g_runtime.api, Log))
    {
        g_runtime.api->Log(g_runtime.plugin, level, message);
    }
}

DWORD WINAPI SensorPollingWorker(LPVOID parameter)
{
    auto* runtime = static_cast<OptiSensorPlugin::PluginRuntime*>(parameter);
    if (runtime == nullptr)
    {
        return 0;
    }

    std::optional<LibreBridge::SensorSnapshot> lastGood;
    std::uint32_t failedPollCount = 0;
    ULONGLONG nextPoll = 0;
    bool updateCheckComplete = false;

    for (;;)
    {
        const ULONGLONG now = GetTickCount64();
        const std::uint64_t lastTabVisit = runtime->lastTabVisit.load(std::memory_order_acquire);
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
                    runtime->sensorSnapshot.store(
                        std::make_shared<const LibreBridge::SensorSnapshot>(*lastGood),
                        std::memory_order_release);
                }
                else
                {
                    failedPollCount = std::min<std::uint32_t>(failedPollCount + 1, 3);
                    const auto* previous = lastGood.has_value() ? &*lastGood : nullptr;
                    runtime->sensorSnapshot.store(
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
                    runtime->sensorSnapshot.store(
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
                runtime->updateStatus.store(UpdateChecker::CheckStatus(runtime->buildRunId), std::memory_order_release);
            }
            catch (...)
            {
                runtime->updateStatus.store(UpdateChecker::Status::Unavailable, std::memory_order_release);
            }
            if (tabActive)
            {
                nextPoll = GetTickCount64() + kLibrePollIntervalMs;
            }
        }

        Sleep(tabActive ? 200 : 1000);
    }
}
}

extern "C" __declspec(dllexport) bool OptiPluginInit(const OptiPluginApi* api, OptiPlugin* plugin)
{
    if (api == nullptr || plugin == nullptr || IsWineOrProton())
    {
        return false;
    }

    g_runtime.api = api;
    g_runtime.plugin = plugin;
    g_runtime.buildRunId = kBuildRunId;
    g_runtime.buildSha = kBuildSha;

    if (!HasRequiredApi(api))
    {
        Log(OptiLog_Error, "Plugin SDK is missing a required API function");
        g_runtime.api = nullptr;
        g_runtime.plugin = nullptr;
        return false;
    }

    if (!api->RegisterTab(plugin, "OptiSensor", "All detected hardware sensors", PluginTab::Draw, &g_runtime))
    {
        Log(OptiLog_Error, "Failed to register the OptiSensor tab");
        g_runtime.api = nullptr;
        g_runtime.plugin = nullptr;
        return false;
    }

    // Keep polling, parsing, and the optional update request off the render thread.
    HANDLE sensorThread = CreateThread(nullptr, 0, SensorPollingWorker, &g_runtime, 0, nullptr);
    if (sensorThread != nullptr)
    {
        CloseHandle(sensorThread);
    }
    else
    {
        try
        {
            g_runtime.sensorSnapshot.store(
                std::make_shared<const LibreBridge::SensorSnapshot>(
                    LibreBridge::MakeFailedPollSnapshot(nullptr, 1)),
                std::memory_order_release);
        }
        catch (...)
        {
        }
        g_runtime.updateStatus.store(UpdateChecker::Status::Unavailable, std::memory_order_release);
    }

    const std::string buildInfo = "OptiSensor plugin initialized (Build-Run-ID " +
        std::to_string(kBuildRunId) + ", Build-SHA " + kBuildSha + ")";
    Log(OptiLog_Info, buildInfo.c_str());
    return true;
}
