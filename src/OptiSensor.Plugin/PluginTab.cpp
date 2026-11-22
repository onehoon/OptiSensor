#include "PluginTab.h"

#include "OverlayLayout.h"
#include "PluginRuntime.h"

#include <Windows.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace OptiSensorPlugin::PluginTab
{
namespace LibreBridge = OptiSensorPlugin::LibreBridge;
namespace OverlayLayout = OptiSensorPlugin::OverlayLayout;
namespace UpdateChecker = OptiSensorPlugin::UpdateChecker;

namespace
{
constexpr int kMaximumOverlayGroups = 64;
PluginRuntime* g_runtime = nullptr;
struct TabState
{
    std::vector<OverlayLayout::Group> groups;
    std::unordered_map<std::string, std::array<char, 160>> groupNameEditBuffers;
    bool groupsLoaded = false;
    char newGroupName[96]{};
    const char* saveStatus = nullptr;
};
TabState g_tabState;

void Log(OptiLogLevel level, const char* message)
{
    if (g_runtime != nullptr && g_runtime->api != nullptr && g_runtime->plugin != nullptr &&
        OPTI_API_HAS(g_runtime->api, Log))
    {
        g_runtime->api->Log(g_runtime->plugin, level, message);
    }
}

bool HasOverlaySettingsApi()
{
    return OPTI_API_HAS(g_runtime->api, GetInt) && OPTI_API_HAS(g_runtime->api, GetString) &&
        OPTI_API_HAS(g_runtime->api, SetInt) && OPTI_API_HAS(g_runtime->api, SetString);
}

bool HasBoolSettingsApi()
{
    return OPTI_API_HAS(g_runtime->api, GetBool) && OPTI_API_HAS(g_runtime->api, SetBool);
}

bool HasOverlayEditorApi()
{
    return HasOverlaySettingsApi() && OPTI_API_HAS(g_runtime->api, InputText) && OPTI_API_HAS(g_runtime->api, Combo);
}

std::string OverlayGroupIdKey(int index)
{
    return "OverlayGroupId_" + std::to_string(index);
}

std::string OverlayGroupNameKey(const std::string& id)
{
    return "OverlayGroupName_" + id;
}

std::array<char, 160>& GroupNameEditBuffer(const OverlayLayout::Group& group)
{
    auto [buffer, inserted] = g_tabState.groupNameEditBuffers.try_emplace(group.id);
    if (inserted)
    {
        const std::size_t length = std::min(group.name.size(), buffer->second.size() - 1);
        std::memcpy(buffer->second.data(), group.name.data(), length);
        buffer->second[length] = '\0';
    }
    return buffer->second;
}

std::vector<OverlayLayout::Group> DefaultOverlayGroups()
{
    return {{"CPU", "CPU"}, {"GPU", "GPU"}, {"VRAM", "VRAM"}, {"RAM", "RAM"}};
}

bool FindOverlayGroup(const std::string& id)
{
    return std::any_of(g_tabState.groups.begin(), g_tabState.groups.end(), [&id](const OverlayLayout::Group& group)
    {
        return group.id == id;
    });
}

void SaveOverlayGroups()
{
    if (!HasOverlaySettingsApi()) return;
    g_runtime->api->SetInt(g_runtime->plugin, "OverlayGroupCount", static_cast<std::int32_t>(g_tabState.groups.size()));
    for (std::size_t index = 0; index < g_tabState.groups.size(); ++index)
    {
        const std::string indexKey = OverlayGroupIdKey(static_cast<int>(index));
        const std::string nameKey = OverlayGroupNameKey(g_tabState.groups[index].id);
        g_runtime->api->SetString(g_runtime->plugin, indexKey.c_str(), g_tabState.groups[index].id.c_str());
        g_runtime->api->SetString(g_runtime->plugin, nameKey.c_str(), g_tabState.groups[index].name.c_str());
    }
}

bool ReadSettingString(const std::string& key, std::string& value, const char* defaultValue = "")
{
    if (!HasOverlaySettingsApi()) return false;
    std::array<char, 160> buffer{};
    const bool exists = g_runtime->api->GetString(g_runtime->plugin, key.c_str(), buffer.data(), buffer.size(), defaultValue);
    value.assign(buffer.data());
    return exists;
}

void EnsureOverlayGroup(const std::string& id)
{
    if (id.empty() || FindOverlayGroup(id)) return;
    g_tabState.groups.push_back({id, id});
    SaveOverlayGroups();
    g_tabState.saveStatus = nullptr;
}

void LoadOverlayGroups()
{
    if (g_tabState.groupsLoaded) return;
    g_tabState.groupsLoaded = true;
    const std::vector<OverlayLayout::Group> defaults = DefaultOverlayGroups();
    g_tabState.groups = defaults;
    if (!HasOverlaySettingsApi()) return;

    const std::int32_t count = g_runtime->api->GetInt(g_runtime->plugin, "OverlayGroupCount", -1);
    if (count < 0 || count > kMaximumOverlayGroups)
    {
        SaveOverlayGroups();
        return;
    }

    std::vector<OverlayLayout::Group> loaded;
    bool repaired = false;
    for (std::int32_t index = 0; index < count; ++index)
    {
        std::string id;
        if (!ReadSettingString(OverlayGroupIdKey(index), id) || id.empty() ||
            std::any_of(loaded.begin(), loaded.end(), [&id](const OverlayLayout::Group& group) { return group.id == id; }))
        {
            repaired = true;
            continue;
        }
        std::string name;
        if (!ReadSettingString(OverlayGroupNameKey(id), name, id.c_str())) name = id;
        loaded.push_back({std::move(id), std::move(name)});
    }

    std::vector<OverlayLayout::Group> ordered;
    ordered.reserve(std::min<std::size_t>(kMaximumOverlayGroups, loaded.size() + defaults.size()));
    for (const OverlayLayout::Group& builtin : defaults)
    {
        const auto found = std::find_if(loaded.begin(), loaded.end(), [&builtin](const OverlayLayout::Group& group)
        {
            return group.id == builtin.id;
        });
        if (found == loaded.end())
        {
            ordered.push_back(builtin);
            repaired = true;
        }
        else
        {
            ordered.push_back(*found);
            if (found != loaded.begin() + static_cast<std::ptrdiff_t>(ordered.size() - 1)) repaired = true;
        }
    }
    for (const OverlayLayout::Group& group : loaded)
    {
        if (std::none_of(defaults.begin(), defaults.end(), [&group](const OverlayLayout::Group& builtin)
            { return builtin.id == group.id; }))
        {
            if (ordered.size() < kMaximumOverlayGroups) ordered.push_back(group);
            else repaired = true;
        }
    }
    if (ordered.size() != loaded.size()) repaired = true;
    g_tabState.groups = std::move(ordered);
    if (repaired) SaveOverlayGroups();
}

std::int32_t ReadSensorOrder(const LibreBridge::SensorReading& reading)
{
    if (!HasOverlaySettingsApi()) return OverlayLayout::MissingSensorOrder;
    return g_runtime->api->GetInt(g_runtime->plugin, OverlayLayout::GetSensorOrderSettingKey(reading).c_str(), OverlayLayout::MissingSensorOrder);
}

void SaveSensorOrder(const LibreBridge::SensorReading& reading, std::int32_t order)
{
    if (HasOverlaySettingsApi())
    {
        const std::string key = OverlayLayout::GetSensorOrderSettingKey(reading);
        g_runtime->api->SetInt(g_runtime->plugin, key.c_str(), order);
        g_tabState.saveStatus = nullptr;
    }
}

std::vector<OverlayLayout::SensorState> ReadSensorStates(
    const LibreBridge::SensorSnapshot* snapshot,
    std::vector<OverlayLayout::Recommendation>& recommendations)
{
    LoadOverlayGroups();
    if (snapshot == nullptr || !snapshot->hasValidSnapshot) return {};

    recommendations = OverlayLayout::GetDefaultRecommendations(*snapshot);
    std::unordered_map<std::string, OverlayLayout::Recommendation> recommended;
    recommended.reserve(recommendations.size());
    for (const auto& item : recommendations) recommended.emplace(item.settingKey, item);

    std::vector<OverlayLayout::SensorState> states;
    states.reserve(snapshot->readings.size());
    for (const LibreBridge::SensorReading& reading : snapshot->readings)
    {
        OverlayLayout::SensorState state;
        state.settingKey = reading.settingKey;
        const auto recommendedSensor = recommended.find(reading.settingKey);
        const bool firstUseRecommended = recommendedSensor != recommended.end();
        state.selected = g_runtime->api->GetBool(g_runtime->plugin, reading.settingKey.c_str(), firstUseRecommended);
        const std::string fanAverageKey = OverlayLayout::GetSensorFanAverageSettingKey(reading);
        if (!fanAverageKey.empty())
            state.averageFan = g_runtime->api->GetBool(g_runtime->plugin, fanAverageKey.c_str(), true);
        state.order = ReadSensorOrder(reading);

        std::string storedGroup;
        if (ReadSettingString(OverlayLayout::GetSensorGroupSettingKey(reading), storedGroup) && !storedGroup.empty())
        {
            EnsureOverlayGroup(storedGroup);
            state.groupId = storedGroup;
        }
        else if (state.selected)
        {
            state.groupId = firstUseRecommended ? recommendedSensor->second.groupId : OverlayLayout::GetDefaultGroupId(reading);
            if (state.groupId.empty())
            {
                state.groupId = "Ungrouped";
                EnsureOverlayGroup(state.groupId);
            }
            if (HasOverlaySettingsApi())
            {
                const std::string key = OverlayLayout::GetSensorGroupSettingKey(reading);
                g_runtime->api->SetString(g_runtime->plugin, key.c_str(), state.groupId.c_str());
                g_tabState.saveStatus = nullptr;
            }
        }
        states.push_back(std::move(state));
    }

    std::unordered_map<std::string, std::size_t> stateIndexes;
    stateIndexes.reserve(states.size());
    for (std::size_t index = 0; index < states.size(); ++index)
    {
        stateIndexes.emplace(states[index].settingKey, index);
    }
    for (const OverlayLayout::Recommendation& item : recommendations)
    {
        const auto stateIndex = stateIndexes.find(item.settingKey);
        if (stateIndex == stateIndexes.end()) continue;
        OverlayLayout::SensorState& state = states[stateIndex->second];
        if (!state.selected || state.groupId != item.groupId || state.order != OverlayLayout::MissingSensorOrder)
        {
            continue;
        }
        state.order = OverlayLayout::NextSensorOrder(states, state.groupId);
        SaveSensorOrder(snapshot->readings[stateIndex->second], state.order);
    }
    for (std::size_t index = 0; index < states.size(); ++index)
    {
        OverlayLayout::SensorState& state = states[index];
        if (!state.selected || state.groupId.empty() || state.order != OverlayLayout::MissingSensorOrder) continue;
        state.order = OverlayLayout::NextSensorOrder(states, state.groupId, state.settingKey);
        SaveSensorOrder(snapshot->readings[index], state.order);
    }
    return states;
}

void SaveGroupSensorOrders(
    const LibreBridge::SensorSnapshot& snapshot,
    const std::vector<OverlayLayout::SensorState>& states,
    const std::string& groupId)
{
    const std::size_t count = std::min(snapshot.readings.size(), states.size());
    for (std::size_t index = 0; index < count; ++index)
    {
        if (states[index].groupId == groupId && states[index].order != OverlayLayout::MissingSensorOrder)
        {
            SaveSensorOrder(snapshot.readings[index], states[index].order);
        }
    }
}

std::string TrimGroupName(const char* value)
{
    std::string result = value == nullptr ? std::string{} : std::string(value);
    const auto whitespace = [](unsigned char character)
    {
        return character == ' ' || character == '\t' || character == '\r' || character == '\n';
    };
    while (!result.empty() && whitespace(static_cast<unsigned char>(result.front()))) result.erase(result.begin());
    while (!result.empty() && whitespace(static_cast<unsigned char>(result.back()))) result.pop_back();
    return result;
}

void AddOverlayGroup()
{
    const std::string name = TrimGroupName(g_tabState.newGroupName);
    if (name.empty() || g_tabState.groups.size() >= kMaximumOverlayGroups) return;
    std::int32_t nextId = std::max<std::int32_t>(1, g_runtime->api->GetInt(g_runtime->plugin, "OverlayNextGroupId", 1));
    std::string id;
    do
    {
        id = "Custom_" + std::to_string(nextId++);
    } while (FindOverlayGroup(id));
    g_runtime->api->SetInt(g_runtime->plugin, "OverlayNextGroupId", nextId);
    g_tabState.groups.push_back({id, name});
    SaveOverlayGroups();
    g_tabState.newGroupName[0] = '\0';
    g_tabState.saveStatus = nullptr;
}

void DrawSelectedSensors(
    const LibreBridge::SensorSnapshot* snapshot,
    std::vector<OverlayLayout::SensorState>& states)
{
    g_runtime->api->Section("Selected Sensors");
    if (!HasOverlayEditorApi())
    {
        g_runtime->api->Text("Editable groups require the OptiScaler plugin settings API.");
        return;
    }

    g_runtime->api->Text("New group:");
    g_runtime->api->SameLine();
    g_runtime->api->InputText("##OptiSensorNewGroup", g_tabState.newGroupName, sizeof(g_tabState.newGroupName));
    g_runtime->api->SameLine();
    if (g_runtime->api->Button("Add Group")) AddOverlayGroup();
    if (OPTI_API_HAS(g_runtime->api, Separator)) g_runtime->api->Separator();
    if (OPTI_API_HAS(g_runtime->api, Spacing)) g_runtime->api->Spacing();

    bool firstGroup = true;
    for (OverlayLayout::Group& group : g_tabState.groups)
    {
        if (!firstGroup)
        {
            if (OPTI_API_HAS(g_runtime->api, Separator)) g_runtime->api->Separator();
            if (OPTI_API_HAS(g_runtime->api, Spacing)) g_runtime->api->Spacing();
        }
        firstGroup = false;
        std::array<char, 160>& nameBuffer = GroupNameEditBuffer(group);
        const std::string inputId = "##OverlayGroupName_" + group.id;
        if (g_runtime->api->InputText(inputId.c_str(), nameBuffer.data(), nameBuffer.size()))
        {
            group.name.assign(nameBuffer.data());
            const std::string nameKey = OverlayGroupNameKey(group.id);
            g_runtime->api->SetString(g_runtime->plugin, nameKey.c_str(), group.name.c_str());
            g_tabState.saveStatus = nullptr;
        }

        std::vector<std::size_t> rows;
        for (std::size_t index = 0; index < states.size(); ++index)
        {
            if (states[index].selected && states[index].groupId == group.id) rows.push_back(index);
        }
        std::sort(rows.begin(), rows.end(), [&states](std::size_t left, std::size_t right)
        {
            if (states[left].order != states[right].order) return states[left].order < states[right].order;
            return states[left].settingKey < states[right].settingKey;
        });

        for (std::size_t row = 0; row < rows.size(); ++row)
        {
            OverlayLayout::SensorState& state = states[rows[row]];
            if (snapshot != nullptr)
            {
                const auto reading = std::find_if(snapshot->readings.begin(), snapshot->readings.end(), [&state](const LibreBridge::SensorReading& value)
                {
                    return value.settingKey == state.settingKey;
                });
                if (reading != snapshot->readings.end())
                {
                    const std::string identity = state.settingKey;
                    std::vector<const char*> groupNames;
                    groupNames.reserve(g_tabState.groups.size());
                    std::int32_t selectedGroup = 0;
                    for (std::size_t groupIndex = 0; groupIndex < g_tabState.groups.size(); ++groupIndex)
                    {
                        groupNames.push_back(g_tabState.groups[groupIndex].name.c_str());
                        if (g_tabState.groups[groupIndex].id == state.groupId) selectedGroup = static_cast<std::int32_t>(groupIndex);
                    }
                    const std::string comboId = "##OverlayMove_" + identity;
                    if (g_runtime->api->Combo(comboId.c_str(), &selectedGroup, groupNames.data(),
                                     static_cast<std::int32_t>(groupNames.size()), nullptr) &&
                        selectedGroup >= 0 && static_cast<std::size_t>(selectedGroup) < g_tabState.groups.size())
                    {
                        const std::string targetGroup = g_tabState.groups[static_cast<std::size_t>(selectedGroup)].id;
                        if (targetGroup != state.groupId)
                        {
                            state.groupId = targetGroup;
                            state.order = OverlayLayout::NextSensorOrder(states, targetGroup, state.settingKey);
                            const std::string groupKey = OverlayLayout::GetSensorGroupSettingKey(*reading);
                            g_runtime->api->SetString(g_runtime->plugin, groupKey.c_str(), state.groupId.c_str());
                            SaveSensorOrder(*reading, state.order);
                            g_tabState.saveStatus = nullptr;
                            return;
                        }
                    }

                    g_runtime->api->SameLine();
                    const std::string up = "Up##OverlayUp_" + identity;
                    if (g_runtime->api->Button(up.c_str()) &&
                        OverlayLayout::MoveSelectedSensor(states, identity, -1))
                    {
                        SaveGroupSensorOrders(*snapshot, states, group.id);
                        g_tabState.saveStatus = nullptr;
                        return;
                    }
                    g_runtime->api->SameLine();
                    const std::string down = "Down##OverlayDown_" + identity;
                    if (g_runtime->api->Button(down.c_str()) &&
                        OverlayLayout::MoveSelectedSensor(states, identity, 1))
                    {
                        SaveGroupSensorOrders(*snapshot, states, group.id);
                        g_tabState.saveStatus = nullptr;
                        return;
                    }
                    g_runtime->api->SameLine();
                    const std::string label = reading->label + "  " + reading->value;
                    g_runtime->api->Text(label.c_str());

                    const std::string fanAverageKey = OverlayLayout::GetSensorFanAverageSettingKey(*reading);
                    if (!fanAverageKey.empty() && OPTI_API_HAS(g_runtime->api, Checkbox) && HasBoolSettingsApi())
                    {
                        g_runtime->api->SameLine();
                        const std::string averageLabel = "Average##" + fanAverageKey;
                        if (g_runtime->api->Checkbox(averageLabel.c_str(), &state.averageFan))
                        {
                            g_runtime->api->SetBool(g_runtime->plugin, fanAverageKey.c_str(), state.averageFan);
                            g_tabState.saveStatus = nullptr;
                        }
                    }
                }
            }
        }
    }
}

void DrawSensor(
    const LibreBridge::SensorReading& reading,
    const std::unordered_set<std::string>& recommended)
{
    bool visible = g_runtime->api->GetBool(g_runtime->plugin, reading.settingKey.c_str(), recommended.contains(reading.settingKey));
    std::string checkboxLabel = reading.label;
    checkboxLabel += " (";
    checkboxLabel += reading.sensorType;
    checkboxLabel += ")##";
    checkboxLabel += reading.settingKey;

    if (g_runtime->api->Checkbox(checkboxLabel.c_str(), &visible))
    {
        g_runtime->api->SetBool(g_runtime->plugin, reading.settingKey.c_str(), visible);
        g_tabState.saveStatus = nullptr;
    }

    g_runtime->api->SameLine();
    g_runtime->api->Text(reading.value.c_str());
}

void DrawHardwareGroups(
    const LibreBridge::SensorSnapshot& snapshot,
    const std::unordered_set<std::string>& recommended)
{
    if (snapshot.readings.empty())
    {
        g_runtime->api->Text("No detected sensors");
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

        bool expanded = g_runtime->api->GetBool(g_runtime->plugin, group.groupSettingKey.c_str(), group.groupDefaultExpanded);
        std::string header = expanded ? "[-] " : "[+] ";
        header += group.device;
        header += " [";
        header.append(group.groupSettingKey.data() + (sizeof("GroupExpanded_") - 1), 8);
        header += "] (";
        header += std::to_string(end - first);
        header += " sensors)##";
        header += group.groupSettingKey;
        if (g_runtime->api->Button(header.c_str()))
        {
            expanded = !expanded;
            g_runtime->api->SetBool(g_runtime->plugin, group.groupSettingKey.c_str(), expanded);
            g_tabState.saveStatus = nullptr;
        }

        if (expanded)
        {
            for (std::size_t index = first; index < end; ++index)
            {
                DrawSensor(snapshot.readings[index], recommended);
            }
        }

        first = end;
    }
}

bool DrawInfoCard(const LibreBridge::SensorSnapshot* snapshot)
{
    g_runtime->api->Section("OptiSensor Info");
    std::string buildIdentity;
    if (g_runtime->buildRunId == 0)
    {
        buildIdentity = "Build: local";
    }
    else
    {
        char buffer[96]{};
        std::snprintf(buffer, sizeof(buffer), "Build: %llu (%.8s)",
                      static_cast<unsigned long long>(g_runtime->buildRunId), g_runtime->buildSha);
        buildIdentity = buffer;
    }
    g_runtime->api->Text(buildIdentity.c_str());

    UpdateChecker::Status updateStatus = g_runtime->updateStatus.load(std::memory_order_acquire);
    if (g_runtime->buildRunId == 0) updateStatus = UpdateChecker::Status::LocalBuild;
    const char* statusText = nullptr;
    switch (updateStatus)
    {
    case UpdateChecker::Status::Checking: statusText = "Checking for updates..."; break;
    case UpdateChecker::Status::UpToDate: statusText = "Up to date"; break;
    case UpdateChecker::Status::UpdateAvailable: statusText = "Update Available"; break;
    case UpdateChecker::Status::LocalBuild: statusText = "Local build"; break;
    case UpdateChecker::Status::Unavailable: break;
    }
    if (statusText != nullptr)
    {
        g_runtime->api->SameLine();
        if (updateStatus == UpdateChecker::Status::UpdateAvailable && OPTI_API_HAS(g_runtime->api, TextColored))
        {
            const std::string text = "\xE2\x80\x94 " + std::string(statusText);
            g_runtime->api->TextColored(1.0f, 0.72f, 0.20f, 1.0f, text.c_str());
        }
        else
        {
            const std::string text = "\xE2\x80\x94 " + std::string(statusText);
            g_runtime->api->Text(text.c_str());
        }
    }

    const bool connecting = snapshot == nullptr;
    const bool connected = !connecting && snapshot->hasValidSnapshot && !snapshot->unavailable;
    g_runtime->api->Text("Libre HW Monitor:");
    g_runtime->api->SameLine();
    if (connecting)
    {
        if (OPTI_API_HAS(g_runtime->api, TextDisabled))
            g_runtime->api->TextDisabled("Connecting...");
        else
            g_runtime->api->Text("Connecting...");
    }
    else if (connected && OPTI_API_HAS(g_runtime->api, TextColored))
    {
        g_runtime->api->TextColored(0.20f, 0.82f, 0.36f, 1.0f, "OK");
    }
    else if (!connected && OPTI_API_HAS(g_runtime->api, TextColored))
    {
        g_runtime->api->TextColored(0.92f, 0.24f, 0.24f, 1.0f, "Not connected");
    }
    else
    {
        g_runtime->api->Text(connected ? "OK" : "Not connected");
    }
    if (!connecting && !connected)
    {
        if (OPTI_API_HAS(g_runtime->api, TextDisabled))
            g_runtime->api->TextDisabled("Install/run LibreHardwareMonitor + PawnIO. Enable Web Server (8085).");
        else
            g_runtime->api->Text("Install/run LibreHardwareMonitor + PawnIO. Enable Web Server (8085).");
    }

    if (OPTI_API_HAS(g_runtime->api, Separator)) g_runtime->api->Separator();
    g_runtime->api->Text("Options");
    bool averageGpuFanRpm = true;
    if (HasBoolSettingsApi())
    {
        averageGpuFanRpm = g_runtime->api->GetBool(g_runtime->plugin, "AverageGpuFanRpm", true);
        if (OPTI_API_HAS(g_runtime->api, Checkbox) &&
            g_runtime->api->Checkbox("Average GPU Fan RPM", &averageGpuFanRpm))
        {
            g_runtime->api->SetBool(g_runtime->plugin, "AverageGpuFanRpm", averageGpuFanRpm);
            g_tabState.saveStatus = nullptr;
        }
    }
    return averageGpuFanRpm;
}

}

void Draw(void* userData)
{
    auto* runtime = static_cast<PluginRuntime*>(userData);
    if (runtime == nullptr || runtime->api == nullptr || runtime->plugin == nullptr) return;
    g_runtime = runtime;
    g_runtime->lastTabVisit.store(GetTickCount64(), std::memory_order_release);
    const auto snapshot = g_runtime->sensorSnapshot.load(std::memory_order_acquire);
    const bool averageGpuFanRpm = DrawInfoCard(snapshot.get());

    std::vector<OverlayLayout::Recommendation> recommendations;
    std::vector<OverlayLayout::SensorState> states = ReadSensorStates(snapshot.get(), recommendations);
    std::unordered_set<std::string> recommendedKeys;
    recommendedKeys.reserve(recommendations.size());
    for (const auto& item : recommendations) recommendedKeys.insert(item.settingKey);

    LibreBridge::SensorSnapshot emptySnapshot;
    const LibreBridge::SensorSnapshot* previewSnapshot =
        snapshot != nullptr && snapshot->hasValidSnapshot ? snapshot.get() : &emptySnapshot;
    const std::string preview = OverlayLayout::ComposePreview(
        *previewSnapshot,
        g_tabState.groups,
        states,
        averageGpuFanRpm);
    g_runtime->api->Section("Overlay Preview");
    g_runtime->api->Text(preview.c_str());

    DrawSelectedSensors(previewSnapshot, states);

    g_runtime->api->Section("Hardware Sensors");
    if (snapshot == nullptr || !snapshot->hasValidSnapshot)
    {
        g_runtime->api->Text("No detected sensors");
    }
    else
    {
        if (snapshot->totalSensorCount > snapshot->readings.size())
        {
            const std::string countMessage = "Showing " + std::to_string(snapshot->readings.size()) +
                " / " + std::to_string(snapshot->totalSensorCount) + " sensors; snapshot limit reached.";
            g_runtime->api->Text(countMessage.c_str());
        }
        DrawHardwareGroups(*snapshot, recommendedKeys);
    }

    if (g_runtime->api->Button("Save"))
    {
        if (g_runtime->api->SaveSettings(g_runtime->plugin))
        {
            g_tabState.saveStatus = "Settings saved";
            Log(OptiLog_Info, "Settings saved");
        }
        else
        {
            g_tabState.saveStatus = "Failed to save settings";
            Log(OptiLog_Error, "Failed to save settings");
        }
    }

    if (g_tabState.saveStatus != nullptr)
    {
        g_runtime->api->Text(g_tabState.saveStatus);
    }
}
}
