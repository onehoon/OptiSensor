#include "OverlayLayout.h"

#include <algorithm>
#include <charconv>
#include <cmath>
#include <cctype>
#include <limits>
#include <optional>
#include <string_view>
#include <unordered_map>
#include <unordered_set>

namespace OptiSensorPlugin::OverlayLayout
{
namespace
{
std::string Normalize(std::string_view text)
{
    std::string result;
    result.reserve(text.size());
    bool pendingSpace = false;
    for (const unsigned char value : text)
    {
        if (std::isalnum(value))
        {
            if (pendingSpace && !result.empty())
            {
                result.push_back(' ');
            }
            result.push_back(static_cast<char>(std::tolower(value)));
            pendingSpace = false;
        }
        else
        {
            pendingSpace = true;
        }
    }
    return result;
}

bool StartsWith(std::string_view value, std::string_view prefix)
{
    return value.size() >= prefix.size() && value.substr(0, prefix.size()) == prefix;
}

bool IsCpu(const LibreBridge::SensorReading& reading)
{
    return StartsWith(reading.rootHardwareId, "/amdcpu/") ||
        StartsWith(reading.rootHardwareId, "/intelcpu/") ||
        StartsWith(reading.rootHardwareId, "/cpu/");
}

bool IsGpu(const LibreBridge::SensorReading& reading)
{
    return StartsWith(reading.rootHardwareId, "/gpu-nvidia/") ||
        StartsWith(reading.rootHardwareId, "/gpu-amd/") ||
        StartsWith(reading.rootHardwareId, "/gpu-intel/") ||
        StartsWith(reading.rootHardwareId, "/gpu-intel-integrated/");
}

bool IsType(const LibreBridge::SensorReading& reading, std::string_view type)
{
    return Normalize(reading.sensorType) == Normalize(type);
}

bool Contains(std::string_view text, std::string_view part)
{
    return text.find(part) != std::string_view::npos;
}

template <typename Score>
const LibreBridge::SensorReading* BestCandidate(
    const LibreBridge::SensorSnapshot& snapshot,
    std::string_view rootHardwareId,
    std::string_view type,
    Score score)
{
    const LibreBridge::SensorReading* best = nullptr;
    int bestScore = 1000;
    for (const LibreBridge::SensorReading& reading : snapshot.readings)
    {
        if (reading.rootHardwareId != rootHardwareId || !IsType(reading, type))
        {
            continue;
        }
        const int candidateScore = score(Normalize(reading.text));
        if (candidateScore < bestScore ||
            (candidateScore == bestScore && best != nullptr && reading.settingKey < best->settingKey))
        {
            best = &reading;
            bestScore = candidateScore;
        }
    }
    return bestScore < 1000 ? best : nullptr;
}

const LibreBridge::SensorReading* ExactSensor(
    const LibreBridge::SensorSnapshot& snapshot,
    std::string_view rootHardwareId,
    std::string_view type,
    std::string_view name)
{
    const std::string normalizedName = Normalize(name);
    const LibreBridge::SensorReading* best = nullptr;
    for (const LibreBridge::SensorReading& reading : snapshot.readings)
    {
        if (reading.rootHardwareId == rootHardwareId && IsType(reading, type) && Normalize(reading.text) == normalizedName &&
            (best == nullptr || reading.settingKey < best->settingKey))
        {
            best = &reading;
        }
    }
    return best;
}

const LibreBridge::SensorReading* ExactSmallData(
    const LibreBridge::SensorSnapshot& snapshot,
    std::string_view rootHardwareId,
    std::string_view name)
{
    return ExactSensor(snapshot, rootHardwareId, "SmallData", name);
}

std::string CompactMegabytes(std::string_view value)
{
    std::size_t end = value.size();
    while (end > 0 && std::isspace(static_cast<unsigned char>(value[end - 1])))
    {
        --end;
    }
    if (end < 2 || std::tolower(static_cast<unsigned char>(value[end - 2])) != 'm' ||
        std::tolower(static_cast<unsigned char>(value[end - 1])) != 'b')
    {
        return std::string(value);
    }

    std::size_t numberEnd = end - 2;
    while (numberEnd > 0 && std::isspace(static_cast<unsigned char>(value[numberEnd - 1])))
    {
        --numberEnd;
    }
    double megabytes = 0;
    const char* first = value.data();
    const char* last = value.data() + numberEnd;
    while (first < last && std::isspace(static_cast<unsigned char>(*first)))
    {
        ++first;
    }
    const auto parsed = std::from_chars(first, last, megabytes);
    if (first == last || parsed.ec != std::errc{} || parsed.ptr != last || !std::isfinite(megabytes) || megabytes < 0 ||
        megabytes > static_cast<double>(std::numeric_limits<long long>::max()))
    {
        return std::string(value);
    }

    return std::to_string(static_cast<std::uint64_t>(std::llround(megabytes))) + "MB";
}

std::optional<double> ParseFormattedUnit(std::string_view value, std::string_view unit)
{
    std::size_t end = value.size();
    while (end > 0 && std::isspace(static_cast<unsigned char>(value[end - 1]))) --end;
    if (end < unit.size()) return std::nullopt;
    for (std::size_t index = 0; index < unit.size(); ++index)
    {
        if (std::tolower(static_cast<unsigned char>(value[end - unit.size() + index])) !=
            std::tolower(static_cast<unsigned char>(unit[index])))
        {
            return std::nullopt;
        }
    }
    std::size_t numberEnd = end - unit.size();
    while (numberEnd > 0 && std::isspace(static_cast<unsigned char>(value[numberEnd - 1]))) --numberEnd;
    const char* first = value.data();
    const char* last = value.data() + numberEnd;
    while (first < last && std::isspace(static_cast<unsigned char>(*first))) ++first;
    double parsedValue = 0;
    const auto parsed = std::from_chars(first, last, parsedValue);
    if (first == last || parsed.ec != std::errc{} || parsed.ptr != last ||
        !std::isfinite(parsedValue) || parsedValue < 0)
    {
        return std::nullopt;
    }
    return parsedValue;
}

std::string CompactGigabytes(std::string_view value)
{
    const auto parsed = ParseFormattedUnit(value, "GB");
    if (!parsed.has_value()) return std::string(value);
    std::size_t end = value.size();
    while (end > 0 && std::isspace(static_cast<unsigned char>(value[end - 1]))) --end;
    std::size_t numberEnd = end - 2;
    while (numberEnd > 0 && std::isspace(static_cast<unsigned char>(value[numberEnd - 1]))) --numberEnd;
    std::size_t numberStart = 0;
    while (numberStart < numberEnd && std::isspace(static_cast<unsigned char>(value[numberStart]))) ++numberStart;
    std::string number(value.substr(numberStart, numberEnd - numberStart));
    if (const std::size_t decimal = number.find('.'); decimal != std::string::npos)
    {
        while (!number.empty() && number.back() == '0') number.pop_back();
        if (!number.empty() && number.back() == '.') number.pop_back();
    }
    return number + "GB";
}

std::string FormatGigabytes(double value)
{
    const double rounded = std::round(value * 10.0) / 10.0;
    char buffer[64]{};
    const auto formatted = std::to_chars(buffer, buffer + sizeof(buffer), rounded, std::chars_format::fixed, 1);
    if (formatted.ec != std::errc{}) return "N/A";
    std::string number(buffer, formatted.ptr);
    if (number.ends_with(".0")) number.resize(number.size() - 2);
    return number + "GB";
}

std::optional<std::string> FormatFanAverage(double value)
{
    if (!std::isfinite(value) || value < 0 || value >= static_cast<double>(std::numeric_limits<long long>::max()))
    {
        return std::nullopt;
    }
    return std::to_string(std::llround(value)) + "RPM";
}

std::string DisplayValue(const LibreBridge::SensorReading& reading)
{
    return reading.value.empty() ? "N/A" : reading.value;
}
}

std::vector<Recommendation> GetDefaultRecommendations(const LibreBridge::SensorSnapshot& snapshot)
{
    std::vector<Recommendation> result;
    std::unordered_set<std::string> cpuRoots;
    std::string cpuRoot;
    for (const LibreBridge::SensorReading& reading : snapshot.readings)
    {
        if (IsCpu(reading))
        {
            cpuRoots.insert(reading.rootHardwareId);
        }
    }
    if (!cpuRoots.empty())
    {
        cpuRoot = *std::min_element(cpuRoots.begin(), cpuRoots.end());
        const auto* temperature = BestCandidate(snapshot, cpuRoot, "Temperature", [](const std::string& text)
        {
            if (Contains(text, "distance") || Contains(text, "tjmax") || Contains(text, "hotspot") ||
                Contains(text, "hot spot") || Contains(text, "junction")) return 1000;
            if (Contains(text, "core tctl tdie") || Contains(text, "tctl tdie")) return 0;
            if (text == "cpu package") return 1;
            if (text == "package") return 2;
            if (text == "core average" || text == "cores average") return 3;
            if (text == "core max") return 4;
            if (Contains(text, "core")) return 5;
            return 6;
        });
        const auto* load = BestCandidate(snapshot, cpuRoot, "Load", [](const std::string& text)
        {
            if (Contains(text, "cpu total") || Contains(text, "total cpu")) return 0;
            if (text == "total" || Contains(text, "overall cpu") || Contains(text, "overall total")) return 1;
            return 1000;
        });
        const auto* power = BestCandidate(snapshot, cpuRoot, "Power", [](const std::string& text)
        {
            if (Contains(text, "package")) return 0;
            if (Contains(text, "cpu ppt") || text == "ppt") return 1;
            return 1000;
        });
        if (temperature != nullptr) result.push_back({temperature->settingKey, "CPU", 100});
        if (load != nullptr) result.push_back({load->settingKey, "CPU", 200});
        if (power != nullptr) result.push_back({power->settingKey, "CPU", 300});
    }

    const std::string gpuRoot = GetGpuHardwareId(snapshot);
    if (!gpuRoot.empty())
    {
        const auto* temperature = BestCandidate(snapshot, gpuRoot, "Temperature", [](const std::string& text)
        {
            if (Contains(text, "hotspot") || Contains(text, "hot spot") || Contains(text, "junction") ||
                Contains(text, "memory") || Contains(text, "vrm")) return 1000;
            if (Contains(text, "gpu core")) return 0;
            if (Contains(text, "gpu temperature")) return 1;
            return 2;
        });
        const auto* load = BestCandidate(snapshot, gpuRoot, "Load", [](const std::string& text)
        {
            if (Contains(text, "memory") || Contains(text, "bus") || Contains(text, "encoder") ||
                Contains(text, "video")) return 1000;
            if (Contains(text, "gpu core")) return 0;
            if (Contains(text, "gpu total") || Contains(text, "gpu load") || text == "gpu") return 1;
            return 1000;
        });
        const auto* power = BestCandidate(snapshot, gpuRoot, "Power", [](const std::string& text)
        {
            if (Contains(text, "gpu power") || Contains(text, "package") || Contains(text, "total")) return 0;
            return 1000;
        });
        if (temperature != nullptr) result.push_back({temperature->settingKey, "GPU", 100});
        if (load != nullptr) result.push_back({load->settingKey, "GPU", 200});
        if (power != nullptr) result.push_back({power->settingKey, "GPU", 300});

        std::vector<const LibreBridge::SensorReading*> fans;
        for (const LibreBridge::SensorReading& reading : snapshot.readings)
        {
            if (reading.rootHardwareId == gpuRoot && IsType(reading, "Fan")) fans.push_back(&reading);
        }
        std::sort(fans.begin(), fans.end(), [](const auto* left, const auto* right)
        {
            const std::string leftName = Normalize(left->text);
            const std::string rightName = Normalize(right->text);
            return leftName == rightName ? left->settingKey < right->settingKey : leftName < rightName;
        });
        for (std::size_t index = 0; index < fans.size(); ++index)
        {
            result.push_back({fans[index]->settingKey, "GPU", static_cast<std::int32_t>(400 + index * 100)});
        }

        const auto* memoryUsed = ExactSmallData(snapshot, gpuRoot, "GPU Memory Used");
        const auto* memoryTotal = ExactSmallData(snapshot, gpuRoot, "GPU Memory Total");
        if (memoryUsed != nullptr) result.push_back({memoryUsed->settingKey, "VRAM", 100});
        if (memoryTotal != nullptr) result.push_back({memoryTotal->settingKey, "VRAM", 200});
    }

    const auto* ramUsed = ExactSensor(snapshot, "/ram", "Data", "Memory Used");
    if (ramUsed != nullptr) result.push_back({ramUsed->settingKey, "RAM", 100});
    return result;
}

std::string GetDefaultGroupId(const LibreBridge::SensorReading& reading)
{
    if (IsCpu(reading))
    {
        return "CPU";
    }
    if (IsGpu(reading))
    {
        const std::string name = Normalize(reading.text);
        if (IsType(reading, "SmallData") && (name == "gpu memory used" || name == "gpu memory total"))
        {
            return "VRAM";
        }
        return "GPU";
    }
    if (reading.rootHardwareId == "/ram" && IsType(reading, "Data"))
    {
        const std::string name = Normalize(reading.text);
        if (name == "memory used" || name == "memory available") return "RAM";
    }
    return {};
}

std::string GetGpuHardwareId(const LibreBridge::SensorSnapshot& snapshot)
{
    std::unordered_set<std::string> gpuRoots;
    for (const LibreBridge::SensorReading& reading : snapshot.readings)
    {
        if (IsGpu(reading))
        {
            gpuRoots.insert(reading.rootHardwareId);
        }
    }
    if (gpuRoots.empty()) return {};

    const auto preference = [](std::string_view hardwareId)
    {
        if (StartsWith(hardwareId, "/gpu-nvidia/")) return 0;
        if (StartsWith(hardwareId, "/gpu-amd/")) return 1;
        if (StartsWith(hardwareId, "/gpu-intel/")) return 2;
        if (StartsWith(hardwareId, "/gpu-intel-integrated/")) return 3;
        return 4;
    };
    const auto selected = std::min_element(gpuRoots.begin(), gpuRoots.end(), [&preference](const std::string& left, const std::string& right)
    {
        const int leftPreference = preference(left);
        const int rightPreference = preference(right);
        return leftPreference == rightPreference ? left < right : leftPreference < rightPreference;
    });
    return selected == gpuRoots.end() ? std::string{} : *selected;
}

std::string GetSensorFanAverageSettingKey(const LibreBridge::SensorReading& reading)
{
    if (!IsGpu(reading) || !IsType(reading, "Fan")) return {};
    constexpr std::string_view prefix = "SensorVisible_";
    if (StartsWith(reading.settingKey, prefix))
    {
        return "SensorFanAverage_" + reading.settingKey.substr(prefix.size());
    }
    return "SensorFanAverage_" + reading.settingKey;
}

std::string GetSensorGroupSettingKey(const LibreBridge::SensorReading& reading)
{
    constexpr std::string_view prefix = "SensorVisible_";
    if (StartsWith(reading.settingKey, prefix))
    {
        return "SensorGroup_" + reading.settingKey.substr(prefix.size());
    }
    return "SensorGroup_" + reading.settingKey;
}

std::string GetSensorOrderSettingKey(const LibreBridge::SensorReading& reading)
{
    constexpr std::string_view prefix = "SensorVisible_";
    if (StartsWith(reading.settingKey, prefix))
    {
        return "SensorOrder_" + reading.settingKey.substr(prefix.size());
    }
    return "SensorOrder_" + reading.settingKey;
}

std::int32_t NextSensorOrder(
    const std::vector<SensorState>& states,
    std::string_view groupId,
    std::string_view excludedKey)
{
    std::int32_t largest = MissingSensorOrder;
    for (const SensorState& state : states)
    {
        if (state.groupId == groupId && state.settingKey != excludedKey && state.order != MissingSensorOrder)
        {
            largest = std::max(largest, state.order);
        }
    }
    if (largest == MissingSensorOrder) return 100;
    return largest > std::numeric_limits<std::int32_t>::max() - 100 ? largest : largest + 100;
}

void NormalizeGroupOrder(std::vector<SensorState>& states, std::string_view groupId)
{
    std::vector<SensorState*> ordered;
    for (SensorState& state : states)
    {
        if (state.groupId == groupId && state.order != MissingSensorOrder)
        {
            ordered.push_back(&state);
        }
    }
    std::sort(ordered.begin(), ordered.end(), [](const SensorState* left, const SensorState* right)
    {
        return left->order == right->order ? left->settingKey < right->settingKey : left->order < right->order;
    });
    for (std::size_t index = 0; index < ordered.size(); ++index)
    {
        ordered[index]->order = static_cast<std::int32_t>((index + 1) * 100);
    }
}

bool MoveSelectedSensor(std::vector<SensorState>& states, std::string_view settingKey, int direction)
{
    if (direction != -1 && direction != 1) return false;
    const auto current = std::find_if(states.begin(), states.end(), [settingKey](const SensorState& state)
    {
        return state.settingKey == settingKey;
    });
    if (current == states.end() || !current->selected || current->groupId.empty()) return false;

    std::vector<SensorState*> selected;
    for (SensorState& state : states)
    {
        if (state.selected && state.groupId == current->groupId) selected.push_back(&state);
    }
    std::sort(selected.begin(), selected.end(), [](const SensorState* left, const SensorState* right)
    {
        return left->order == right->order ? left->settingKey < right->settingKey : left->order < right->order;
    });
    const auto row = std::find_if(selected.begin(), selected.end(), [settingKey](const SensorState* state)
    {
        return state->settingKey == settingKey;
    });
    if (row == selected.end()) return false;
    const std::ptrdiff_t index = row - selected.begin();
    const std::ptrdiff_t target = index + direction;
    if (target < 0 || target >= static_cast<std::ptrdiff_t>(selected.size())) return false;

    std::swap(selected[static_cast<std::size_t>(index)]->order,
              selected[static_cast<std::size_t>(target)]->order);
    NormalizeGroupOrder(states, current->groupId);
    return true;
}

std::string ComposePreview(
    const LibreBridge::SensorSnapshot& snapshot,
    const std::vector<Group>& groups,
    const std::vector<SensorState>& states,
    bool averageGpuFanRpm)
{
    std::unordered_map<std::string, const LibreBridge::SensorReading*> readings;
    readings.reserve(snapshot.readings.size());
    for (const LibreBridge::SensorReading& reading : snapshot.readings)
    {
        readings.emplace(reading.settingKey, &reading);
    }
    std::unordered_map<std::string, const SensorState*> stateByKey;
    stateByKey.reserve(states.size());
    for (const SensorState& state : states)
    {
        stateByKey.emplace(state.settingKey, &state);
    }

    std::vector<std::string> renderedGroups;
    for (const Group& group : groups)
    {
        std::vector<const SensorState*> selected;
        for (const SensorState& state : states)
        {
            if (state.selected && state.groupId == group.id && readings.contains(state.settingKey))
            {
                selected.push_back(&state);
            }
        }
        std::sort(selected.begin(), selected.end(), [](const SensorState* left, const SensorState* right)
        {
            return left->order == right->order ? left->settingKey < right->settingKey : left->order < right->order;
        });
        if (selected.empty())
        {
            continue;
        }

        const LibreBridge::SensorReading* pairFirst = nullptr;
        const LibreBridge::SensorReading* pairSecond = nullptr;
        const SensorState* firstState = nullptr;
        const SensorState* secondState = nullptr;
        const bool isVramGroup = group.id == "VRAM";
        const bool isRamGroup = group.id == "RAM";
        if (isVramGroup)
        {
            for (const SensorState* candidate : selected)
            {
                const LibreBridge::SensorReading* used = readings.at(candidate->settingKey);
                if (!IsGpu(*used) || !IsType(*used, "SmallData") || Normalize(used->text) != "gpu memory used")
                {
                    continue;
                }

                const auto totalState = std::find_if(selected.begin(), selected.end(), [&](const SensorState* row)
                {
                    const LibreBridge::SensorReading* total = readings.at(row->settingKey);
                    return total->rootHardwareId == used->rootHardwareId && IsType(*total, "SmallData") &&
                        Normalize(total->text) == "gpu memory total";
                });
                if (totalState != selected.end())
                {
                    pairFirst = used;
                    pairSecond = readings.at((*totalState)->settingKey);
                    firstState = candidate;
                    secondState = *totalState;
                    break;
                }
            }
        }
        if (pairFirst != nullptr && pairSecond != nullptr)
        {
            const auto first = stateByKey.find(pairFirst->settingKey);
            const auto second = stateByKey.find(pairSecond->settingKey);
            if (first != stateByKey.end() && first->second->selected && first->second->groupId == group.id)
                firstState = first->second;
            if (second != stateByKey.end() && second->second->selected && second->second->groupId == group.id)
                secondState = second->second;
        }
        const bool pairSelected = firstState != nullptr && secondState != nullptr;
        const bool pairInCanonicalOrder = pairSelected &&
            std::find(selected.begin(), selected.end(), firstState) <
            std::find(selected.begin(), selected.end(), secondState);
        const bool pairVram = pairInCanonicalOrder && isVramGroup;

        const LibreBridge::SensorReading* ramUsed = isRamGroup ? ExactSensor(snapshot, "/ram", "Data", "Memory Used") : nullptr;
        const LibreBridge::SensorReading* ramAvailable = isRamGroup ? ExactSensor(snapshot, "/ram", "Data", "Memory Available") : nullptr;
        const SensorState* ramUsedState = nullptr;
        const SensorState* ramAvailableState = nullptr;
        std::string ramPairValue;
        bool pairRam = false;
        if (ramUsed != nullptr)
        {
            const auto usedState = stateByKey.find(ramUsed->settingKey);
            if (usedState != stateByKey.end() && usedState->second->selected && usedState->second->groupId == group.id)
                ramUsedState = usedState->second;
        }
        if (ramAvailable != nullptr)
        {
            const auto availableState = stateByKey.find(ramAvailable->settingKey);
            if (availableState != stateByKey.end() && availableState->second->selected && availableState->second->groupId == group.id)
                ramAvailableState = availableState->second;
        }
        if (ramUsed != nullptr && ramUsedState != nullptr && ramAvailable != nullptr)
        {
            const auto used = ParseFormattedUnit(ramUsed->value, "GB");
            const auto available = ParseFormattedUnit(ramAvailable->value, "GB");
            const bool visibleAvailableIsAfterUsed = ramAvailableState == nullptr ||
                std::find(selected.begin(), selected.end(), ramUsedState) <
                std::find(selected.begin(), selected.end(), ramAvailableState);
            if (used.has_value() && available.has_value() && visibleAvailableIsAfterUsed &&
                std::isfinite(*used + *available))
            {
                pairRam = true;
                ramPairValue = CompactGigabytes(ramUsed->value) + "/" + FormatGigabytes(*used + *available);
            }
        }

        struct FanAverage
        {
            std::size_t count = 0;
            double value = 0;
            const SensorState* firstState = nullptr;
            std::optional<std::string> text;
        };
        std::unordered_map<std::string, FanAverage> fanAverages;
        if (averageGpuFanRpm)
        {
            for (const SensorState* state : selected)
            {
                const LibreBridge::SensorReading* reading = readings.at(state->settingKey);
                if (!state->averageFan || !IsGpu(*reading) || !IsType(*reading, "Fan")) continue;
                const auto rpm = ParseFormattedUnit(reading->value, "RPM");
                if (!rpm.has_value()) continue;
                FanAverage& average = fanAverages[reading->rootHardwareId];
                if (average.count == 0)
                {
                    average.value = *rpm;
                    average.firstState = state;
                }
                else
                {
                    average.value += (*rpm - average.value) / static_cast<double>(average.count + 1);
                }
                ++average.count;
            }
            for (auto& [rootHardwareId, average] : fanAverages)
            {
                (void)rootHardwareId;
                if (average.count >= 2) average.text = FormatFanAverage(average.value);
            }
        }

        std::vector<std::string> values;
        bool pairAdded = false;
        for (const SensorState* state : selected)
        {
            const LibreBridge::SensorReading* reading = readings.at(state->settingKey);
            if (pairVram && (state == firstState || state == secondState))
            {
                if (!pairAdded)
                {
                    values.push_back(CompactMegabytes(DisplayValue(*pairFirst)) + "/" +
                                     CompactMegabytes(DisplayValue(*pairSecond)));
                    pairAdded = true;
                }
                continue;
            }

            if (pairRam && state == ramUsedState)
            {
                values.push_back(ramPairValue);
                continue;
            }
            if (pairRam && state == ramAvailableState) continue;

            const auto fanAverage = fanAverages.find(reading->rootHardwareId);
            const auto rpm = ParseFormattedUnit(reading->value, "RPM");
            if (averageGpuFanRpm && state->averageFan && rpm.has_value() && IsGpu(*reading) && IsType(*reading, "Fan") &&
                fanAverage != fanAverages.end() && fanAverage->second.count >= 2 && fanAverage->second.text.has_value())
            {
                if (state == fanAverage->second.firstState) values.push_back(*fanAverage->second.text);
                continue;
            }
            values.push_back(DisplayValue(*reading));
        }
        if (values.empty())
        {
            continue;
        }

        std::string rendered;
        if (!group.name.empty())
        {
            rendered = group.name;
        }
        for (const std::string& value : values)
        {
            if (!rendered.empty()) rendered.push_back(' ');
            rendered += value;
        }
        renderedGroups.push_back(std::move(rendered));
    }

    if (renderedGroups.empty())
    {
        return "No sensors selected";
    }
    std::string result;
    for (const std::string& group : renderedGroups)
    {
        if (!result.empty()) result += " | ";
        result += group;
    }
    return result;
}
}
