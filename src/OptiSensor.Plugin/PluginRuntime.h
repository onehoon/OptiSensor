#pragma once

#include "LibreBridge.h"
#include "UpdateChecker.h"
#include "optiscaler_plugin.h"

#include <atomic>
#include <cstdint>
#include <memory>

namespace OptiSensorPlugin
{
struct PluginRuntime
{
    const OptiPluginApi* api = nullptr;
    OptiPlugin* plugin = nullptr;
    std::uint64_t buildRunId = 0;
    const char* buildSha = "local";
    std::atomic<std::uint64_t> lastTabVisit{0};
    std::atomic<std::shared_ptr<const LibreBridge::SensorSnapshot>> sensorSnapshot;
    std::atomic<UpdateChecker::Status> updateStatus{UpdateChecker::Status::Checking};
};
}
