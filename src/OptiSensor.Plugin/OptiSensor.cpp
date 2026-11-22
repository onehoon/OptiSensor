#include <Windows.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstdio>
#include <string>

#include "optiscaler_plugin.h"
#include "../OptiSensor.Native/OptiSensorNative.h"

extern "C" __declspec(dllexport) bool OptiPluginInit(const OptiPluginApi* api, OptiPlugin* plugin);

namespace
{
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

void SetBackendError(const std::string& message)
{
    g_backendError = message;
    Log(OptiLog_Error, g_backendError.c_str());
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
        SetBackendError("Could not determine the OptiSensor.asi directory");
        return;
    }

    const std::wstring helperPath = pluginDirectory + L"OptiSensor.dll";
    g_sensorModule = LoadLibraryW(helperPath.c_str());
    if (g_sensorModule == nullptr)
    {
        const DWORD error = GetLastError();
        SetBackendError("Could not load adjacent OptiSensor.dll (Win32 error " + std::to_string(error) + ")");
        return;
    }

    g_start = reinterpret_cast<StartFn>(GetProcAddress(g_sensorModule, "OptiSensor_Start"));
    g_getSnapshot = reinterpret_cast<GetSnapshotFn>(GetProcAddress(g_sensorModule, "OptiSensor_GetSnapshot"));
    g_stop = reinterpret_cast<StopFn>(GetProcAddress(g_sensorModule, "OptiSensor_Stop"));
    if (g_start == nullptr || g_getSnapshot == nullptr || g_stop == nullptr)
    {
        SetBackendError("OptiSensor.dll is missing one or more required C ABI exports");
        return;
    }

    const int32_t result = g_start();
    if (result < 0)
    {
        SetBackendError("LibreHardwareMonitor worker could not be started");
        return;
    }

    g_backendError.clear();
    Log(OptiLog_Info, "LibreHardwareMonitor sensor worker started");
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
        g_snapshotError = "Reading the cached sensor snapshot failed";
        return;
    }

    g_snapshotError.clear();
    g_readingCount = std::min(result, static_cast<int32_t>(g_readings.size()));
}

const char* UnitFor(int32_t kind)
{
    switch (kind)
    {
    case OptiSensor_CpuUsage:
    case OptiSensor_GpuUsage:
        return "%";
    case OptiSensor_CpuTemperature:
    case OptiSensor_GpuTemperature:
        return "\xC2\xB0" "C";
    case OptiSensor_GpuPower:
        return "W";
    case OptiSensor_FanRpm:
        return "RPM";
    default:
        return "";
    }
}

void DrawSensor(const OptiSensorReading& reading)
{
    const bool defaultVisible = true;
    bool visible = g_api->GetBool(g_plugin, reading.settingKey, defaultVisible);
    std::string checkboxLabel = reading.device;
    checkboxLabel += " / ";
    checkboxLabel += reading.label;
    checkboxLabel += "##";
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
        std::snprintf(value, sizeof(value), "%.1f %s", reading.value, UnitFor(reading.kind));
    }
    else
    {
        std::snprintf(value, sizeof(value), "N/A");
    }
    g_api->Text(value);
}

void DrawGroup(const char* title, int32_t firstKind, int32_t lastKind)
{
    g_api->Section(title);
    int32_t rendered = 0;
    for (int32_t index = 0; index < g_readingCount; ++index)
    {
        const OptiSensorReading& reading = g_readings[static_cast<size_t>(index)];
        if (reading.kind < firstKind || reading.kind > lastKind)
        {
            continue;
        }

        DrawSensor(reading);
        ++rendered;
    }

    if (rendered == 0)
    {
        g_api->Text("No detected sensors");
    }
}

void DrawTab(void*)
{
    RefreshSnapshot();
    g_api->Section("OptiSensor");

    if (!g_backendError.empty())
    {
        g_api->Text(g_backendError.c_str());
    }
    else if (!g_snapshotError.empty())
    {
        g_api->Text(g_snapshotError.c_str());
    }
    else if (g_snapshotInfo.status == OptiSensor_StatusLoading)
    {
        g_api->Text("Discovering sensors...");
    }
    else if (g_snapshotInfo.status == OptiSensor_StatusError || g_snapshotInfo.status == OptiSensor_StatusStopped)
    {
        g_api->Text(g_snapshotInfo.message);
    }
    else if (g_snapshotInfo.message[0] != '\0')
    {
        g_api->Text(g_snapshotInfo.message);
    }

    DrawGroup("CPU", OptiSensor_CpuUsage, OptiSensor_CpuTemperature);
    DrawGroup("GPU", OptiSensor_GpuUsage, OptiSensor_GpuPower);
    DrawGroup("FAN", OptiSensor_FanRpm, OptiSensor_FanRpm);

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

    if (!api->RegisterTab(plugin, "OptiSensor", "CPU, GPU, and fan sensors", DrawTab, nullptr))
    {
        Log(OptiLog_Error, "Failed to register the OptiSensor tab");
        g_api = nullptr;
        g_plugin = nullptr;
        return false;
    }

    InitializeBackend();
    Log(OptiLog_Info, "OptiSensor plugin initialized");
    return true;
}
