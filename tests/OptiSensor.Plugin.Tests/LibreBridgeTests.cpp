#include "LibreBridge.h"

#include <fstream>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>

using namespace OptiSensorPlugin::LibreBridge;

void RunOverlayLayoutTests();

namespace
{
constexpr char Fixture[] = R"json({
  "Children": [{
    "Text": "DESKTOP",
    "Children": [
      {
        "Text": "Gigabyte B650M K",
        "HardwareId": "/motherboard",
        "Children": [{
          "Text": "ITE IT8689E",
          "HardwareId": "/lpc/it8689e/0",
          "Children": [{
            "Text": "Voltage #1",
            "SensorId": "/lpc/it8689e/0/voltage/0",
            "Type": "Voltage",
            "Value": "0.852 V"
          }]
        }]
      },
      {
        "Text": "AMD Ryzen 5 7600",
        "HardwareId": "/amdcpu/0",
        "Children": [
          {
            "Text": "Core (Tctl/Tdie)",
            "SensorId": "/amdcpu/0/temperature/2",
            "Type": "Temperature",
            "Value": "54.5 \u00B0C"
          },
          {
            "Text": "Cores (Average)",
            "SensorId": "/amdcpu/0/clock/1",
            "Type": "Clock",
            "Value": "4378.0 MHz"
          },
          {
            "Text": "Package",
            "SensorId": "/amdcpu/0/power/0",
            "Type": "Power",
            "Value": "33.5 W"
          },
          {
            "Text": "Core VID unavailable",
            "SensorId": "/amdcpu/0/voltage/2",
            "Type": "Voltage",
            "Value": null
          },
          {
            "Text": "Optional value",
            "SensorId": "/amdcpu/0/load/99",
            "Type": "Load",
            "Value": "-"
          }
        ]
      },
      {
        "Text": "NVIDIA GeForce",
        "HardwareId": "/gpu-nvidia/0",
        "Children": [
          {
            "Text": "GPU Memory",
            "SensorId": "/gpu-nvidia/0/load/3",
            "Type": "Load",
            "Value": "18.0 %"
          },
          {
            "Text": "GPU Bus",
            "SensorId": "/gpu-nvidia/0/load/3",
            "Type": "Load",
            "Value": "0.0 %"
          }
        ]
      },
      {
        "Text": "NVMe drive",
        "HardwareId": "/nvme/1",
        "Children": [{
          "Text": "Drive temperature",
          "SensorId": "/nvme/1/temperature/0",
          "Type": "Temperature",
          "Value": "36.0 \u00B0C"
        }]
      },
      {
        "Text": "SATA SSD",
        "HardwareId": "/ssd/0",
        "Children": [{
          "Text": "Drive temperature",
          "SensorId": "/ssd/0/temperature/0",
          "Type": "Temperature",
          "Value": "34.0 \u00B0C"
        }]
      },
      {
        "Text": "Hard disk",
        "HardwareId": "/hdd/0",
        "Children": [{
          "Text": "Drive temperature",
          "SensorId": "/hdd/0/temperature/0",
          "Type": "Temperature",
          "Value": "39.0 \u00B0C"
        }]
      },
      {
        "Text": "Ethernet",
        "HardwareId": "/nic/0",
        "Children": [{
          "Text": "Upload",
          "SensorId": "/nic/0/throughput/0",
          "Type": "Throughput",
          "Value": "12.9 MB/s"
        }]
      }
    ]
  }]
})json";

void Require(bool condition, const char* message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

const SensorReading& FindSensor(const SensorSnapshot& snapshot, const std::string& sensorId)
{
    for (const SensorReading& reading : snapshot.readings)
    {
        if (reading.sensorId == sensorId)
        {
            return reading;
        }
    }
    throw std::runtime_error(("Sensor not found: " + sensorId).c_str());
}

std::size_t CountSensors(const SensorSnapshot& snapshot, const std::string& sensorId)
{
    std::size_t count = 0;
    for (const SensorReading& reading : snapshot.readings)
    {
        count += reading.sensorId == sensorId;
    }
    return count;
}

void TestSchemaAndIdentity()
{
    SensorSnapshot snapshot;
    Require(ParseSnapshot(Fixture, snapshot), "v0.9.6 fixture did not parse");
    Require(snapshot.hasValidSnapshot && !snapshot.unavailable, "parsed snapshot state is wrong");
    Require(snapshot.totalSensorCount == 12 && snapshot.readings.size() == 12, "unexpected visible sensor count");

    const SensorReading& temperature = FindSensor(snapshot, "/amdcpu/0/temperature/2");
    Require(temperature.text == "Core (Tctl/Tdie)", "sensor Text changed");
    Require(temperature.sensorType == "Temperature", "sensor Type changed");
    Require(temperature.value == std::string("54.5 ") + "\xC2\xB0" + "C", "escaped Unicode or formatted value changed");
    Require(temperature.identity == "/amdcpu/0|/amdcpu/0|/amdcpu/0/temperature/2", "legacy sensor identity changed");
    Require(temperature.settingKey == "SensorVisible_399732E2CE7578D0F97D7C869F057C8293A06A2EA6AE9ABCAA63096166B6F4A3", "legacy sensor setting key changed");
    Require(temperature.groupSettingKey == "GroupExpanded_0367DA87AA66D6703746BCA443F60E9F389702896701B76D0AC123FF82BAABF7", "legacy group setting key changed");
    Require(!temperature.groupDefaultExpanded, "CPU group must default collapsed");

    const SensorReading& averageClock = FindSensor(snapshot, "/amdcpu/0/clock/1");
    const SensorReading& packagePower = FindSensor(snapshot, "/amdcpu/0/power/0");
    Require(averageClock.value == "4378.0 MHz" && averageClock.sensorType == "Clock", "formatted clock changed");
    Require(packagePower.value == "33.5 W" && packagePower.sensorType == "Power", "formatted power changed");

    const SensorReading& missing = FindSensor(snapshot, "/amdcpu/0/voltage/2");
    Require(missing.value == "N/A", "null value should display N/A");
    Require(FindSensor(snapshot, "/gpu-nvidia/0/load/3").value == "0.0 %", "valid zero should be preserved");
    Require(FindSensor(snapshot, "/amdcpu/0/load/99").value == "N/A", "dash value should display N/A");

    Require(CountSensors(snapshot, "/gpu-nvidia/0/load/3") == 2, "duplicate GPU SensorId entries were lost");
    const SensorReading* memory = nullptr;
    const SensorReading* bus = nullptr;
    for (const SensorReading& reading : snapshot.readings)
    {
        if (reading.text == "GPU Memory") memory = &reading;
        if (reading.text == "GPU Bus") bus = &reading;
    }
    Require(memory != nullptr && bus != nullptr, "duplicate GPU names were not preserved");
    Require(memory->settingKey != bus->settingKey && memory->identity != bus->identity, "duplicate GPU settings collide");
    Require(memory->identity.find("|Load|GPU Memory|") != std::string::npos, "duplicate identity lacks stable discriminator");
    Require(!memory->groupDefaultExpanded && !bus->groupDefaultExpanded, "GPU group must default collapsed");

    Require(CountSensors(snapshot, "/nvme/1/temperature/0") == 1, "NVMe sensor was not included");
    Require(CountSensors(snapshot, "/ssd/0/temperature/0") == 1, "SSD sensor was not included");
    Require(CountSensors(snapshot, "/hdd/0/temperature/0") == 1, "HDD sensor was not included");
    Require(CountSensors(snapshot, "/nic/0/throughput/0") == 1, "network sensor was not included");
    Require(FindSensor(snapshot, "/ssd/0/temperature/0").device.starts_with("Storage - "), "storage group title was lost");
    Require(FindSensor(snapshot, "/nic/0/throughput/0").device.starts_with("Network - "), "network group title was lost");
    Require(!FindSensor(snapshot, "/ssd/0/temperature/0").groupDefaultExpanded, "storage group must default collapsed");
    Require(!FindSensor(snapshot, "/nic/0/throughput/0").groupDefaultExpanded, "network group must default collapsed");
    const SensorReading& motherboard = FindSensor(snapshot, "/lpc/it8689e/0/voltage/0");
    Require(motherboard.rootHardwareId == "/motherboard", "nested motherboard sensor was grouped incorrectly");
    Require(motherboard.hardwareId == "/lpc/it8689e/0", "nested hardware identity was lost");
    Require(motherboard.label == "ITE IT8689E / Voltage #1", "nested hardware label changed");
    Require(!motherboard.groupDefaultExpanded, "motherboard group must default collapsed");
}

void TestLimitMalformedAndRetention()
{
    std::ostringstream json;
    json << R"json({"Children":[{"Text":"PC","Children":[{"Text":"CPU","HardwareId":"/amdcpu/0","Children":[)json";
    for (std::size_t index = 0; index < MaxVisibleSensors + 1; ++index)
    {
        if (index != 0) json << ',';
        json << "{\"Text\":\"Sensor " << index << "\",\"SensorId\":\"/amdcpu/0/load/" << index
             << "\",\"Type\":\"Load\",\"Value\":\"0.0 %\"}";
    }
    json << "]}]}]}";

    SensorSnapshot limited;
    Require(ParseSnapshot(json.str(), limited), "sensor-limit fixture did not parse");
    Require(limited.totalSensorCount == MaxVisibleSensors + 1, "total count was not preserved");
    Require(limited.readings.size() == MaxVisibleSensors, "visible sensor limit was not enforced");

    SensorSnapshot ignored;
    Require(!ParseSnapshot("{\"Children\":[", ignored), "malformed JSON was accepted");
    Require(!ParseSnapshot("{\"Wrong\":[]}", ignored), "unexpected JSON schema was accepted");

    SensorSnapshot valid;
    Require(ParseSnapshot(Fixture, valid), "retention fixture did not parse");
    SensorSnapshot firstFailure = MakeFailedPollSnapshot(&valid, 1);
    SensorSnapshot secondFailure = MakeFailedPollSnapshot(&valid, 2);
    SensorSnapshot thirdFailure = MakeFailedPollSnapshot(&valid, 3);
    const std::string formattedTemperature = std::string("54.5 ") + "\xC2\xB0" + "C";
    Require(!firstFailure.unavailable && FindSensor(firstFailure, "/amdcpu/0/temperature/2").value == formattedTemperature, "first failure did not retain last good data");
    Require(!secondFailure.unavailable && FindSensor(secondFailure, "/amdcpu/0/temperature/2").value == formattedTemperature, "second failure did not retain last good data");
    Require(thirdFailure.unavailable && thirdFailure.hasValidSnapshot, "third failure did not mark retained snapshot unavailable");
    for (const SensorReading& reading : thirdFailure.readings)
    {
        Require(reading.value == "N/A", "third failure retained a stale live value");
    }

    SensorSnapshot recovered;
    Require(ParseSnapshot(Fixture, recovered), "valid snapshot did not recover after failures");
    Require(!recovered.unavailable && FindSensor(recovered, "/amdcpu/0/temperature/2").value == formattedTemperature, "valid snapshot did not recover immediately");
}

void TestIntelHardwareGroupClassification()
{
    constexpr std::string_view fixture = R"json({
      "Children": [
        {
          "Text": "Intel CPU",
          "HardwareId": "/intelcpu/0",
          "Children": [{
            "Text": "CPU Package",
            "SensorId": "/intelcpu/0/temperature/0",
            "Type": "Temperature",
            "Value": "55.0 °C"
          }]
        },
        {
          "Text": "Intel GPU",
          "HardwareId": "/gpu-intel-integrated/0",
          "Children": [{
            "Text": "GPU Core",
            "SensorId": "/gpu-intel-integrated/0/temperature/0",
            "Type": "Temperature",
            "Value": "45.0 °C"
          }]
        }
      ]
    })json";

    SensorSnapshot snapshot;
    Require(ParseSnapshot(fixture, snapshot), "Intel hardware fixture did not parse");
    const SensorReading& cpu = FindSensor(snapshot, "/intelcpu/0/temperature/0");
    const SensorReading& gpu = FindSensor(snapshot, "/gpu-intel-integrated/0/temperature/0");
    Require(cpu.device == "CPU - Intel CPU" && !cpu.groupDefaultExpanded,
            "Intel CPU was not classified with a collapsed default");
    Require(gpu.device == "GPU - Intel GPU" && !gpu.groupDefaultExpanded,
            "Intel integrated GPU was not classified with a collapsed default");
}

void TestSparseHandheldSnapshot()
{
    constexpr std::string_view fixture = R"json({
      "Children": [{
        "Text": "MSI Claw 8 EX AI+",
        "Children": [
          {
            "Text": "Intel CPU",
            "HardwareId": "/intelcpu/0",
            "Children": [{
              "Text": "CPU Total",
              "SensorId": "/intelcpu/0/load/0",
              "Type": "Load",
              "Value": "12.0 %"
            }]
          },
          {
            "Text": "Intel Graphics",
            "HardwareId": "/gpu-intel-integrated/0",
            "Children": [
              {"Text":"D3D", "SensorId":"/gpu-intel-integrated/0/load/0", "Type":"Load", "Value":"1.0 %"},
              {"Text":"D3D 3D", "SensorId":"/gpu-intel-integrated/0/load/1", "Type":"Load", "Value":"1.0 %"},
              {"Text":"D3D Shared Memory Used", "SensorId":"/gpu-intel-integrated/0/smalldata/0", "Type":"SmallData", "Value":"1.0 GB"},
              {"Text":"D3D Shared Memory Free", "SensorId":"/gpu-intel-integrated/0/smalldata/1", "Type":"SmallData", "Value":"7.0 GB"},
              {"Text":"D3D Shared Memory Total", "SensorId":"/gpu-intel-integrated/0/smalldata/2", "Type":"SmallData", "Value":"8.0 GB"}
            ]
          },
          {
            "Text": "Generic Memory",
            "HardwareId": "/ram",
            "Children": [
              {"Text":"Memory Used", "SensorId":"/ram/data/0", "Type":"Data", "Value":"8.0 GB"},
              {"Text":"Memory Available", "SensorId":"/ram/data/1", "Type":"Data", "Value":"24.0 GB"}
            ]
          },
          {
            "Text": "Battery",
            "HardwareId": "/battery/0",
            "Children": [{
              "Text": "Charge Level",
              "SensorId": "/battery/0/load/0",
              "Type": "Load",
              "Value": "85.0 %"
            }]
          }
        ]
      }]
    })json";

    SensorSnapshot snapshot;
    Require(ParseSnapshot(fixture, snapshot), "sparse Intel handheld fixture did not parse");
    Require(snapshot.readings.size() == 9, "sparse fixture produced unexpected or fabricated readings");
    Require(FindSensor(snapshot, "/intelcpu/0/load/0").text == "CPU Total", "Intel CPU load was not retained");
    Require(FindSensor(snapshot, "/gpu-intel-integrated/0/load/0").text == "D3D", "Intel GPU D3D load was not retained");
    Require(FindSensor(snapshot, "/gpu-intel-integrated/0/smalldata/2").text == "D3D Shared Memory Total",
            "Intel GPU shared memory was not retained");
    Require(FindSensor(snapshot, "/ram/data/0").text == "Memory Used" &&
            FindSensor(snapshot, "/ram/data/1").text == "Memory Available",
            "physical RAM readings were not retained");
    Require(FindSensor(snapshot, "/battery/0/load/0").text == "Charge Level",
            "optional battery sensor was not retained");
    for (const SensorReading& reading : snapshot.readings)
    {
        Require(!reading.groupDefaultExpanded, "a hardware group did not default to collapsed");
    }
}

std::string ReadFile(const char* path)
{
    std::ifstream input(path, std::ios::binary);
    if (!input)
    {
        throw std::runtime_error("could not open JSON file");
    }
    return std::string(std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>());
}
}

int main(int argc, char** argv)
{
    try
    {
        TestSchemaAndIdentity();
        TestLimitMalformedAndRetention();
        TestIntelHardwareGroupClassification();
        TestSparseHandheldSnapshot();
        RunOverlayLayoutTests();
        if (argc == 2 && std::string(argv[1]) == "--live")
        {
            std::string response;
            SensorSnapshot live;
            Require(ReadLocalResponse(response), "WinHTTP could not read the local LibreHardwareMonitor endpoint");
            Require(ParseSnapshot(response, live), "live LibreHardwareMonitor response did not parse");
            Require(live.totalSensorCount > 0, "live LibreHardwareMonitor response contained no sensors");
            std::cout << "Read and parsed local LibreHardwareMonitor endpoint: " << live.totalSensorCount
                      << " sensors; visible " << live.readings.size() << ".\n";
        }
        else if (argc == 3 && std::string(argv[1]) == "--file")
        {
            SensorSnapshot live;
            Require(ParseSnapshot(ReadFile(argv[2]), live), "live LibreHardwareMonitor response did not parse");
            Require(live.totalSensorCount > 0, "live LibreHardwareMonitor response contained no sensors");
            std::cout << "Parsed live LibreHardwareMonitor response: " << live.totalSensorCount
                      << " sensors; visible " << live.readings.size() << ".\n";
        }
        else if (argc != 1)
        {
            throw std::runtime_error("usage: OptiSensor.Plugin.Tests.exe [--live | --file <data.json>]");
        }
        std::cout << "Libre bridge tests passed.\n";
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << "Libre bridge test failed: " << exception.what() << '\n';
        return 1;
    }
}
