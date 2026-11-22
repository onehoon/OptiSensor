#include "OverlayLayout.h"

#include <stdexcept>
#include <string>
#include <vector>

using namespace OptiSensorPlugin;

namespace
{
void RequireOverlay(bool condition, const char* message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

LibreBridge::SensorReading MakeReading(
    const char* root,
    const char* sensorId,
    const char* key,
    const char* text,
    const char* type,
    const char* value)
{
    LibreBridge::SensorReading reading;
    reading.rootHardwareId = root;
    reading.hardwareId = root;
    reading.sensorId = sensorId;
    reading.settingKey = key;
    reading.text = text;
    reading.sensorType = type;
    reading.value = value;
    return reading;
}

void TestCuratedDefaults()
{
    LibreBridge::SensorSnapshot snapshot;
    snapshot.hasValidSnapshot = true;
    snapshot.readings = {
        MakeReading("/amdcpu/0", "/amdcpu/0/temperature/2", "SensorVisible_CPU_TEMP", "Core (Tctl/Tdie)", "Temperature", "55.0 °C"),
        MakeReading("/amdcpu/0", "/amdcpu/0/temperature/0", "SensorVisible_CPU_OTHER_TEMP", "Core #1", "Temperature", "52.0 °C"),
        MakeReading("/amdcpu/0", "/amdcpu/0/load/0", "SensorVisible_CPU_TOTAL", "CPU Total", "Load", "8.0 %"),
        MakeReading("/amdcpu/0", "/amdcpu/0/load/1", "SensorVisible_CPU_CORE", "Core #1", "Load", "12.0 %"),
        MakeReading("/amdcpu/0", "/amdcpu/0/power/0", "SensorVisible_CPU_PACKAGE", "Package", "Power", "34.0 W"),
        MakeReading("/amdcpu/0", "/amdcpu/0/power/1", "SensorVisible_CPU_CORE_POWER", "Core Power", "Power", "5.0 W"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/temperature/0", "SensorVisible_GPU_TEMP", "GPU Core", "Temperature", "38.0 °C"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/temperature/1", "SensorVisible_GPU_HOTSPOT", "GPU Hot Spot", "Temperature", "48.0 °C"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/load/0", "SensorVisible_GPU_CORE", "GPU Core", "Load", "23.0 %"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/load/1", "SensorVisible_GPU_MEM_LOAD", "GPU Memory", "Load", "40.0 %"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/power/0", "SensorVisible_GPU_POWER", "GPU Package Power", "Power", "15.0 W"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/fan/0", "SensorVisible_GPU_FAN0", "GPU Fan 1", "Fan", "0 RPM"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/fan/1", "SensorVisible_GPU_FAN1", "GPU Fan 2", "Fan", "100 RPM"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/smalldata/0", "SensorVisible_GPU_USED", "GPU Memory Used", "SmallData", "6666.0 MB"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/smalldata/1", "SensorVisible_GPU_TOTAL_MEM", "GPU Memory Total", "SmallData", "12000.0 MB"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/load/2", "SensorVisible_GPU_OTHER_MEM", "GPU Memory Free", "SmallData", "5334.0 MB"),
        MakeReading("/nvme/0", "/nvme/0/temperature/0", "SensorVisible_NVME", "Drive temperature", "Temperature", "36.0 °C"),
        MakeReading("/ram", "/ram/data/0", "SensorVisible_RAM_USED", "Memory Used", "Data", "12.4 GB"),
        MakeReading("/ram", "/ram/data/1", "SensorVisible_RAM_AVAILABLE", "Memory Available", "Data", "19.6 GB"),
        MakeReading("/vram", "/vram/data/0", "SensorVisible_VIRTUAL_MEMORY_USED", "Memory Used", "Data", "8.0 GB"),
        MakeReading("/memory/dimm/0", "/memory/dimm/0/temperature/0", "SensorVisible_DIMM_TEMP", "Temperature", "Temperature", "35.0 °C"),
    };

    const std::vector<OverlayLayout::Recommendation> defaults = OverlayLayout::GetDefaultRecommendations(snapshot);
    RequireOverlay(defaults.size() == 11, "expected three CPU, four GPU, two VRAM, and two physical RAM defaults");
    RequireOverlay(defaults[0].settingKey == "SensorVisible_CPU_TEMP" && defaults[0].groupId == "CPU" && defaults[0].order == 100,
            "Tctl/Tdie was not preferred as the first CPU sensor");
    RequireOverlay(defaults[1].settingKey == "SensorVisible_CPU_TOTAL" && defaults[1].order == 200,
            "CPU Total was not preferred over per-core load");
    RequireOverlay(defaults[2].settingKey == "SensorVisible_CPU_PACKAGE" && defaults[2].order == 300,
            "CPU package power was not selected");
    RequireOverlay(defaults[3].settingKey == "SensorVisible_GPU_TEMP" && defaults[4].settingKey == "SensorVisible_GPU_CORE" &&
            defaults[5].settingKey == "SensorVisible_GPU_POWER" && defaults[6].settingKey == "SensorVisible_GPU_FAN0",
            "GPU defaults are not in temperature/load/power/fan order");
    RequireOverlay(defaults[7].settingKey == "SensorVisible_GPU_USED" && defaults[8].settingKey == "SensorVisible_GPU_TOTAL_MEM",
            "VRAM defaults are not the GPU Memory Used/Total pair");
    RequireOverlay(defaults[9].settingKey == "SensorVisible_RAM_USED" && defaults[9].groupId == "RAM" && defaults[9].order == 100 &&
            defaults[10].settingKey == "SensorVisible_RAM_AVAILABLE" && defaults[10].groupId == "RAM" && defaults[10].order == 200,
            "physical RAM recommendations are not Used then Available");
    RequireOverlay(OverlayLayout::GetGpuHardwareId(snapshot) == "/gpu-nvidia/0", "unexpected GPU identity selected");
    RequireOverlay(OverlayLayout::GetDefaultGroupId(snapshot.readings[13]) == "VRAM", "GPU Memory Used was not assigned to VRAM");
    RequireOverlay(OverlayLayout::GetDefaultGroupId(snapshot.readings[16]).empty(), "storage sensor was classified into a default group");
    RequireOverlay(OverlayLayout::GetDefaultGroupId(snapshot.readings[17]) == "RAM" &&
            OverlayLayout::GetDefaultGroupId(snapshot.readings[19]).empty(),
            "physical RAM and virtual memory were not kept separate");
    RequireOverlay(OverlayLayout::GetSensorGroupSettingKey(snapshot.readings[0]) == "SensorGroup_CPU_TEMP" &&
            OverlayLayout::GetSensorOrderSettingKey(snapshot.readings[0]) == "SensorOrder_CPU_TEMP",
            "per-sensor grouping keys do not reuse the sensor identity hash");
}

void TestDeterministicGpuAndComposedPreview()
{
    LibreBridge::SensorSnapshot snapshot;
    snapshot.hasValidSnapshot = true;
    snapshot.readings = {
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/temperature/0", "SensorVisible_NVIDIA_TEMP", "GPU Core", "Temperature", "40.0 °C"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/load/0", "SensorVisible_NVIDIA_LOAD", "GPU Core", "Load", "20.0 %"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/power/0", "SensorVisible_NVIDIA_POWER", "GPU Package Power", "Power", "30.0 W"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/fan/0", "SensorVisible_NVIDIA_FAN", "GPU Fan 1", "Fan", "800 RPM"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/smalldata/0", "SensorVisible_NVIDIA_USED", "GPU Memory Used", "SmallData", "6000 MB"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/smalldata/1", "SensorVisible_NVIDIA_TOTAL", "GPU Memory Total", "SmallData", "12000 MB"),
        MakeReading("/gpu-amd/0", "/gpu-amd/0/temperature/0", "SensorVisible_AMD_TEMP", "GPU Core", "Temperature", "38.0 °C"),
        MakeReading("/gpu-amd/0", "/gpu-amd/0/load/0", "SensorVisible_AMD_LOAD", "GPU Core", "Load", "23.0 %"),
        MakeReading("/gpu-amd/0", "/gpu-amd/0/power/0", "SensorVisible_AMD_POWER", "GPU Package Power", "Power", "15 W"),
        MakeReading("/gpu-amd/0", "/gpu-amd/0/fan/0", "SensorVisible_AMD_FAN", "GPU Fan 1", "Fan", "900 RPM"),
        MakeReading("/gpu-amd/0", "/gpu-amd/0/smalldata/0", "SensorVisible_AMD_USED", "GPU Memory Used", "SmallData", "6666.0 MB"),
        MakeReading("/gpu-amd/0", "/gpu-amd/0/smalldata/1", "SensorVisible_AMD_TOTAL", "GPU Memory Total", "SmallData", "12000.0 MB"),
        MakeReading("/gpu-intel-integrated/0", "/gpu-intel-integrated/0/temperature/0", "SensorVisible_INTEL_GPU_TEMP", "GPU Core", "Temperature", "45.0 °C"),
        MakeReading("/gpu-intel-integrated/0", "/gpu-intel-integrated/0/smalldata/0", "SensorVisible_INTEL_GPU_USED", "GPU Memory Used", "SmallData", "1000 MB"),
        MakeReading("/intelcpu/0", "/intelcpu/0/temperature/0", "SensorVisible_INTELCPU_TEMP", "CPU Package", "Temperature", "55.0 °C"),
        MakeReading("/intelcpu/0", "/intelcpu/0/load/0", "SensorVisible_INTELCPU_LOAD", "CPU Total", "Load", "10.0 %"),
        MakeReading("/intelcpu/0", "/intelcpu/0/power/0", "SensorVisible_INTELCPU_POWER", "Package", "Power", "40.0 W"),
    };

    RequireOverlay(OverlayLayout::GetGpuHardwareId(snapshot) == "/gpu-nvidia/0", "hybrid GPU preference did not choose the NVIDIA adapter");
    const auto defaults = OverlayLayout::GetDefaultRecommendations(snapshot);
    RequireOverlay(defaults.size() == 9, "Intel CPU or preferred NVIDIA GPU recommendations were missing");
    std::size_t gpuRecommendationCount = 0;
    for (const auto& item : defaults)
    {
        if (item.groupId == "CPU")
        {
            RequireOverlay(item.settingKey.find("INTELCPU") != std::string::npos, "Intel CPU sensors were not selected into CPU");
        }
        else
        {
            ++gpuRecommendationCount;
            RequireOverlay(item.settingKey.find("NVIDIA") != std::string::npos, "GPU and VRAM defaults were mixed between adapters");
        }
    }
    RequireOverlay(gpuRecommendationCount == 6, "expected NVIDIA GPU and VRAM readings from the same adapter");
    RequireOverlay(OverlayLayout::GetDefaultGroupId(snapshot.readings[12]) == "GPU" &&
                   OverlayLayout::GetDefaultGroupId(snapshot.readings[13]) == "VRAM" &&
                   OverlayLayout::GetDefaultGroupId(snapshot.readings[14]) == "CPU",
            "Intel integrated GPU or Intel CPU sensors were not classified into their default groups");

    LibreBridge::SensorSnapshot previewSnapshot;
    previewSnapshot.readings = {
        MakeReading("/amdcpu/0", "/amdcpu/0/temperature/0", "SensorVisible_CPU_TEMP", "Core (Tctl/Tdie)", "Temperature", "55°C"),
        MakeReading("/amdcpu/0", "/amdcpu/0/load/0", "SensorVisible_CPU_LOAD", "CPU Total", "Load", "8%"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/fan/0", "SensorVisible_GPU_FAN", "GPU Fan 1", "Fan", "0RPM"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/smalldata/0", "SensorVisible_VRAM_USED", "GPU Memory Used", "SmallData", "6666.0 MB"),
        MakeReading("/gpu-nvidia/0", "/gpu-nvidia/0/smalldata/1", "SensorVisible_VRAM_TOTAL", "GPU Memory Total", "SmallData", "12000.0 MB"),
        MakeReading("/nvme/0", "/nvme/0/temperature/0", "SensorVisible_NVME", "Drive temperature", "Temperature", "36°C"),
        MakeReading("/ram", "/ram/data/0", "SensorVisible_RAM_USED", "Memory Used", "Data", "12.4 GB"),
        MakeReading("/ram", "/ram/data/1", "SensorVisible_RAM_AVAILABLE", "Memory Available", "Data", "19.6 GB"),
        MakeReading("/vram", "/vram/data/0", "SensorVisible_VIRTUAL_MEMORY_USED", "Memory Used", "Data", "8.0 GB"),
    };
    const std::vector<OverlayLayout::Group> groups = {
        {"CPU", "CPU"}, {"GPU", "GPU"}, {"VRAM", "VRAM"}, {"RAM", "RAM"},
        {"Custom_1", "HOTSPOT"}, {"Ungrouped", "Ungrouped"}
    };
    const std::vector<OverlayLayout::SensorState> states = {
        {"SensorVisible_CPU_TEMP", "CPU", 100, true},
        {"SensorVisible_CPU_LOAD", "CPU", 200, true},
        {"SensorVisible_GPU_FAN", "GPU", 400, true},
        {"SensorVisible_VRAM_USED", "VRAM", 100, true},
        {"SensorVisible_VRAM_TOTAL", "VRAM", 200, true},
        {"SensorVisible_NVME", "Ungrouped", 100, false},
        {"SensorVisible_RAM_USED", "RAM", 100, true},
        {"SensorVisible_RAM_AVAILABLE", "RAM", 200, true},
        {"SensorVisible_VIRTUAL_MEMORY_USED", "Ungrouped", 300, false},
    };
    const std::string preview = OverlayLayout::ComposePreview(previewSnapshot, groups, states, "/gpu-nvidia/0");
    RequireOverlay(preview == "CPU 55°C 8% | GPU 0RPM | VRAM 6666MB/12000MB | RAM 12.4GB/32GB",
            "preview composition, default group order, separator, or VRAM formatting changed");
    RequireOverlay(OverlayLayout::ComposePreview(previewSnapshot, groups, {}, "/gpu-nvidia/0") == "No sensors selected",
            "empty selection did not produce the empty-state text");

    auto renamedGroups = groups;
    renamedGroups[2].name = "Graphics Memory";
    renamedGroups[3].name = "System RAM";
    const std::string renamedPreview = OverlayLayout::ComposePreview(previewSnapshot, renamedGroups, states, "/gpu-nvidia/0");
    RequireOverlay(renamedPreview.find("Graphics Memory 6666MB/12000MB") != std::string::npos &&
                   renamedPreview.find("System RAM 12.4GB/32GB") != std::string::npos,
            "renaming a built-in group changed its sensor-specific pairing behavior");

    auto liveRam = previewSnapshot;
    liveRam.readings[6].value = "15.0 GB";
    liveRam.readings[7].value = "16.6 GB";
    RequireOverlay(OverlayLayout::ComposePreview(liveRam, groups, states, "/gpu-nvidia/0").find("RAM 15GB/31.6GB") != std::string::npos,
            "physical RAM total was not computed from the live Used and Available readings");

    auto editedGroups = groups;
    editedGroups[0].name = "Processor";
    editedGroups[4].name = "Games";
    auto editedStates = states;
    editedStates[0].groupId = "Custom_1";
    editedStates[1].selected = false;
    editedStates[2].selected = false;
    editedStates[5].selected = true;
    editedStates[5].groupId = "Custom_1";
    const std::string edited = OverlayLayout::ComposePreview(previewSnapshot, editedGroups, editedStates, "/gpu-nvidia/0");
    RequireOverlay(edited == "VRAM 6666MB/12000MB | RAM 12.4GB/32GB | Games 55°C 36°C",
            "group rename or membership did not immediately affect the preview");

    auto missingStates = states;
    missingStates[4].selected = false;
    RequireOverlay(OverlayLayout::ComposePreview(previewSnapshot, groups, missingStates, "/gpu-nvidia/0") ==
                "CPU 55°C 8% | GPU 0RPM | VRAM 6666.0 MB | RAM 12.4GB/32GB",
            "single selected VRAM reading was paired with a fictitious total");
    previewSnapshot.readings[4].value = "N/A";
    RequireOverlay(OverlayLayout::ComposePreview(previewSnapshot, groups, states, "/gpu-nvidia/0").find("VRAM 6666MB/N/A") != std::string::npos,
            "unavailable VRAM total was not represented as N/A");
    previewSnapshot.readings[6].value = "N/A";
    const std::string ramUnavailable = OverlayLayout::ComposePreview(previewSnapshot, groups, states, "/gpu-nvidia/0");
    RequireOverlay(ramUnavailable.find("RAM N/A 19.6 GB") != std::string::npos,
            "unavailable physical RAM input was used to invent a total");

    auto ramMissing = previewSnapshot;
    ramMissing.readings.erase(ramMissing.readings.begin() + 7);
    const std::string missingRamPreview = OverlayLayout::ComposePreview(ramMissing, groups, states, "/gpu-nvidia/0");
    RequireOverlay(missingRamPreview.find("RAM N/A") != std::string::npos &&
                   missingRamPreview.find("RAM N/A/") == std::string::npos,
            "missing physical RAM input was used to invent a total");
}

void TestPersistentOrderNormalization()
{
    std::vector<OverlayLayout::SensorState> states = {
        {"A", "CPU", 100, true},
        {"Hidden", "CPU", 200, false},
        {"B", "CPU", 300, true},
        {"OtherGroup", "GPU", 100, true},
    };
    RequireOverlay(OverlayLayout::MoveSelectedSensor(states, "B", -1), "selected sensor did not move up");
    RequireOverlay(states[2].order == 100 && states[1].order == 200 && states[0].order == 300,
            "moving a selected row did not normalize the persisted group order");
    RequireOverlay(OverlayLayout::NextSensorOrder(states, "CPU") == 400 &&
                   OverlayLayout::NextSensorOrder(states, "RAM") == 100,
            "new sensor did not append after the persisted order values");
    RequireOverlay(!OverlayLayout::MoveSelectedSensor(states, "B", -1),
            "first selected row moved above the start of its group");
    RequireOverlay(OverlayLayout::MoveSelectedSensor(states, "B", 1) &&
                   states[0].order == 100 && states[1].order == 200 && states[2].order == 300,
            "moving a selected row down did not preserve hidden and selected sensor order");
}
}

void RunOverlayLayoutTests()
{
    TestCuratedDefaults();
    TestDeterministicGpuAndComposedPreview();
    TestPersistentOrderNormalization();
}
