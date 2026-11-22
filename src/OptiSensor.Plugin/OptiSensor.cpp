#include "optiscaler_plugin.h"

namespace
{
const OptiPluginApi* g_api = nullptr;
OptiPlugin* g_plugin = nullptr;
const char* g_saveStatus = nullptr;

bool HasRequiredApi(const OptiPluginApi* api)
{
    return OPTI_API_HAS(api, RegisterTab) &&
        OPTI_API_HAS(api, Section) &&
        OPTI_API_HAS(api, Text) &&
        OPTI_API_HAS(api, Checkbox) &&
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

void DrawTab(void*)
{
    g_api->Section("OptiSensor");
    g_api->Text("Plugin SDK connected");

    bool showSampleText = g_api->GetBool(g_plugin, "ShowSampleText", true);
    if (g_api->Checkbox("ShowSampleText", &showSampleText))
    {
        g_api->SetBool(g_plugin, "ShowSampleText", showSampleText);
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

    if (g_api->GetBool(g_plugin, "ShowSampleText", true))
    {
        g_api->Text("Sample text from OptiSensor");
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

    if (!api->RegisterTab(plugin, "OptiSensor", "OptiSensor plugin SDK proof of concept", DrawTab, nullptr))
    {
        Log(OptiLog_Error, "Failed to register the OptiSensor tab");
        g_api = nullptr;
        g_plugin = nullptr;
        return false;
    }

    Log(OptiLog_Info, "OptiSensor plugin initialized");
    return true;
}
