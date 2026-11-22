#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace OptiSensorPlugin::LibreBridge
{
inline constexpr std::size_t MaxVisibleSensors = 512;

struct SensorReading
{
    std::string rootHardwareId;
    std::string hardwareId;
    std::string sensorId;
    std::string identity;
    std::string settingKey;
    std::string groupSettingKey;
    std::string device;
    std::string text;
    std::string label;
    std::string sensorType;
    std::string value;
    bool groupDefaultExpanded = false;
};

struct SensorSnapshot
{
    std::vector<SensorReading> readings;
    std::size_t totalSensorCount = 0;
    bool hasValidSnapshot = false;
    bool unavailable = false;
};

bool ParseSnapshot(std::string_view json, SensorSnapshot& snapshot);
bool ReadLocalResponse(std::string& response);
SensorSnapshot MakeFailedPollSnapshot(const SensorSnapshot* lastGood, std::uint32_t failedPollCount);
}
