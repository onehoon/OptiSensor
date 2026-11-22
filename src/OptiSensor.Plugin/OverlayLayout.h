#pragma once

#include "LibreBridge.h"

#include <cstdint>
#include <limits>
#include <string>
#include <string_view>
#include <vector>

namespace OptiSensorPlugin::OverlayLayout
{
inline constexpr std::int32_t MissingSensorOrder = std::numeric_limits<std::int32_t>::min();

struct Group
{
    std::string id;
    std::string name;
};

struct SensorState
{
    std::string settingKey;
    std::string groupId;
    std::int32_t order = MissingSensorOrder;
    bool selected = false;
};

struct Recommendation
{
    std::string settingKey;
    std::string groupId;
    std::int32_t order = 0;
};

std::vector<Recommendation> GetDefaultRecommendations(const LibreBridge::SensorSnapshot& snapshot);
std::string GetDefaultGroupId(const LibreBridge::SensorReading& reading);
std::string GetGpuHardwareId(const LibreBridge::SensorSnapshot& snapshot);
std::string GetSensorGroupSettingKey(const LibreBridge::SensorReading& reading);
std::string GetSensorOrderSettingKey(const LibreBridge::SensorReading& reading);
std::int32_t NextSensorOrder(
    const std::vector<SensorState>& states,
    std::string_view groupId,
    std::string_view excludedKey = {});
void NormalizeGroupOrder(std::vector<SensorState>& states, std::string_view groupId);
bool MoveSelectedSensor(std::vector<SensorState>& states, std::string_view settingKey, int direction);
std::string ComposePreview(
    const LibreBridge::SensorSnapshot& snapshot,
    const std::vector<Group>& groups,
    const std::vector<SensorState>& states,
    const std::string& gpuHardwareId);
}
