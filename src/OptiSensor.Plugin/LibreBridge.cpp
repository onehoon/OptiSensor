#include "LibreBridge.h"

#include <Windows.h>
#include <bcrypt.h>
#include <winhttp.h>

#include <algorithm>
#include <array>
#include <memory>
#include <string>
#include <unordered_map>
#include <utility>

#include "../../third_party/yyjson/yyjson.h"

#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "winhttp.lib")

namespace OptiSensorPlugin::LibreBridge
{
namespace
{
constexpr std::size_t MaxTreeDepth = 32;
constexpr std::size_t MaxResponseBytes = 2 * 1024 * 1024;
constexpr std::string_view NotAvailable = "N/A";

struct Candidate
{
    SensorReading reading;
    std::string baseIdentity;
    std::string treePosition;
};

class JsonDocument
{
public:
    explicit JsonDocument(yyjson_doc* document) : _document(document) {}
    ~JsonDocument() { yyjson_doc_free(_document); }
    JsonDocument(const JsonDocument&) = delete;
    JsonDocument& operator=(const JsonDocument&) = delete;

    yyjson_doc* Get() const { return _document; }

private:
    yyjson_doc* _document;
};

struct WinHttpHandle
{
    HINTERNET value = nullptr;

    ~WinHttpHandle()
    {
        if (value != nullptr)
        {
            WinHttpCloseHandle(value);
        }
    }
};

std::string GetString(const yyjson_val* object, const char* name)
{
    const yyjson_val* value = yyjson_obj_get(object, name);
    if (!yyjson_is_str(value))
    {
        return {};
    }

    const char* text = yyjson_get_str(value);
    return text == nullptr ? std::string{} : std::string(text, yyjson_get_len(value));
}

bool HasUsableValue(const std::string& value)
{
    const auto first = value.find_first_not_of(" \t\r\n");
    if (first == std::string::npos)
    {
        return false;
    }
    const auto last = value.find_last_not_of(" \t\r\n");
    return !(last - first + 1 == 1 && value[first] == '-');
}

bool StartsWith(std::string_view value, std::string_view prefix)
{
    return value.size() >= prefix.size() && value.substr(0, prefix.size()) == prefix;
}

bool IsCpu(std::string_view hardwareId)
{
    return StartsWith(hardwareId, "/amdcpu/") ||
        StartsWith(hardwareId, "/intelcpu/") ||
        StartsWith(hardwareId, "/cpu/");
}

bool IsGpu(std::string_view hardwareId)
{
    return StartsWith(hardwareId, "/gpu-nvidia/") ||
        StartsWith(hardwareId, "/gpu-amd/") ||
        StartsWith(hardwareId, "/gpu-intel/") ||
        StartsWith(hardwareId, "/gpu-intel-integrated/");
}

std::string GroupTitle(std::string_view hardwareId, const std::string& name)
{
    std::string_view category = "Hardware";
    if (IsCpu(hardwareId)) category = "CPU";
    else if (IsGpu(hardwareId)) category = "GPU";
    else if (StartsWith(hardwareId, "/memory/") || StartsWith(hardwareId, "/ram") || StartsWith(hardwareId, "/vram")) category = "Memory";
    else if (StartsWith(hardwareId, "/motherboard") || StartsWith(hardwareId, "/superio/")) category = "Motherboard / SuperIO";
    else if (StartsWith(hardwareId, "/nvme/") || StartsWith(hardwareId, "/ssd/") || StartsWith(hardwareId, "/hdd/")) category = "Storage";
    else if (StartsWith(hardwareId, "/nic/") || StartsWith(hardwareId, "/network/")) category = "Network";
    else if (StartsWith(hardwareId, "/battery")) category = "Battery";
    else if (StartsWith(hardwareId, "/ec/") || StartsWith(hardwareId, "/embeddedcontroller")) category = "Controller";
    else if (StartsWith(hardwareId, "/psu") || StartsWith(hardwareId, "/powermonitor")) category = "PSU / Power Monitor";

    std::string result(category);
    result += " - ";
    result += name;
    return result;
}

bool CreateSettingKey(std::string_view prefix, std::string_view identity, std::string& key)
{
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    DWORD objectLength = 0;
    DWORD bytesWritten = 0;
    std::array<UCHAR, 32> digest{};
    std::vector<UCHAR> hashObject;

    NTSTATUS status = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0);
    if (status < 0)
    {
        return false;
    }

    status = BCryptGetProperty(
        algorithm,
        BCRYPT_OBJECT_LENGTH,
        reinterpret_cast<PUCHAR>(&objectLength),
        sizeof(objectLength),
        &bytesWritten,
        0);
    if (status >= 0)
    {
        hashObject.resize(objectLength);
        status = BCryptCreateHash(algorithm, &hash, hashObject.data(), objectLength, nullptr, 0, 0);
    }
    if (status >= 0 && !identity.empty())
    {
        status = BCryptHashData(
            hash,
            reinterpret_cast<PUCHAR>(const_cast<char*>(identity.data())),
            static_cast<ULONG>(identity.size()),
            0);
    }
    if (status >= 0)
    {
        status = BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0);
    }

    if (hash != nullptr)
    {
        BCryptDestroyHash(hash);
    }
    BCryptCloseAlgorithmProvider(algorithm, 0);
    if (status < 0)
    {
        return false;
    }

    constexpr char Hex[] = "0123456789ABCDEF";
    key.assign(prefix);
    key.reserve(prefix.size() + digest.size() * 2);
    for (UCHAR byte : digest)
    {
        key.push_back(Hex[byte >> 4]);
        key.push_back(Hex[byte & 0x0F]);
    }
    return true;
}

void AddSensor(
    const yyjson_val* node,
    const std::string& rootHardwareId,
    const std::string& rootName,
    const std::string& hardwareId,
    const std::string& hardwareName,
    const std::string& treePosition,
    std::vector<Candidate>& candidates)
{
    const std::string sensorId = GetString(node, "SensorId");
    const std::string type = GetString(node, "Type");
    const std::string text = GetString(node, "Text");
    if (sensorId.empty() || type.empty() || text.empty() || rootHardwareId.empty() || hardwareId.empty())
    {
        return;
    }

    SensorReading reading;
    reading.rootHardwareId = rootHardwareId;
    reading.hardwareId = hardwareId;
    reading.sensorId = sensorId;
    reading.device = GroupTitle(rootHardwareId, rootName);
    reading.text = text;
    reading.label = rootHardwareId == hardwareId ? text : hardwareName + " / " + text;
    reading.sensorType = type;
    const yyjson_val* jsonValue = yyjson_obj_get(node, "Value");
    if (yyjson_is_str(jsonValue))
    {
        const char* formatted = yyjson_get_str(jsonValue);
        if (formatted != nullptr)
        {
            reading.value.assign(formatted, yyjson_get_len(jsonValue));
        }
    }
    if (!HasUsableValue(reading.value))
    {
        reading.value.assign(NotAvailable);
    }

    Candidate candidate;
    candidate.baseIdentity = rootHardwareId + "|" + hardwareId + "|" + sensorId;
    candidate.treePosition = treePosition;
    candidate.reading = std::move(reading);
    candidates.push_back(std::move(candidate));
}

bool WalkNode(
    const yyjson_val* node,
    const std::string& parentRootId,
    const std::string& parentRootName,
    const std::string& parentHardwareId,
    const std::string& parentHardwareName,
    const std::string& treePosition,
    std::size_t depth,
    std::vector<Candidate>& candidates)
{
    if (depth > MaxTreeDepth || !yyjson_is_obj(node))
    {
        return false;
    }

    const std::string nodeHardwareId = GetString(node, "HardwareId");
    const std::string nodeName = GetString(node, "Text");
    const bool startsHardwareRoot = parentRootId.empty() && !nodeHardwareId.empty();
    const std::string rootId = startsHardwareRoot ? nodeHardwareId : parentRootId;
    const std::string rootName = startsHardwareRoot ? nodeName : parentRootName;
    const std::string hardwareId = !nodeHardwareId.empty() ? nodeHardwareId : parentHardwareId;
    const std::string hardwareName = !nodeHardwareId.empty() ? nodeName : parentHardwareName;

    if (!rootId.empty() && yyjson_obj_get(node, "SensorId") != nullptr)
    {
        AddSensor(node, rootId, rootName, hardwareId, hardwareName, treePosition, candidates);
    }

    const yyjson_val* children = yyjson_obj_get(node, "Children");
    if (children == nullptr || yyjson_is_null(children))
    {
        return true;
    }
    if (!yyjson_is_arr(children))
    {
        return false;
    }

    const std::size_t childCount = yyjson_arr_size(children);
    for (std::size_t index = 0; index < childCount; ++index)
    {
        const yyjson_val* child = yyjson_arr_get(children, index);
        const std::string childPosition = treePosition.empty()
            ? std::to_string(index)
            : treePosition + "/" + std::to_string(index);
        if (!WalkNode(
                child,
                rootId,
                rootName,
                hardwareId,
                hardwareName,
                childPosition,
                depth + 1,
                candidates))
        {
            return false;
        }
    }
    return true;
}
}

bool ParseSnapshot(std::string_view json, SensorSnapshot& snapshot)
{
    if (json.empty())
    {
        return false;
    }

    yyjson_read_err error{};
    JsonDocument document(
        yyjson_read_opts(const_cast<char*>(json.data()), json.size(), YYJSON_READ_NOFLAG, nullptr, &error));
    if (document.Get() == nullptr)
    {
        return false;
    }

    const yyjson_val* root = yyjson_doc_get_root(document.Get());
    const yyjson_val* children = yyjson_obj_get(root, "Children");
    if (!yyjson_is_obj(root) || !yyjson_is_arr(children))
    {
        return false;
    }

    std::vector<Candidate> candidates;
    yyjson_arr_iter iterator = yyjson_arr_iter_with(children);
    std::size_t index = 0;
    while (const yyjson_val* child = yyjson_arr_iter_next(&iterator))
    {
        if (!WalkNode(
                child,
                {},
                {},
                {},
                {},
                std::to_string(index++),
                0,
                candidates))
        {
            return false;
        }
    }

    std::sort(candidates.begin(), candidates.end(), [](const Candidate& left, const Candidate& right)
    {
        if (left.reading.rootHardwareId != right.reading.rootHardwareId)
            return left.reading.rootHardwareId < right.reading.rootHardwareId;
        if (left.reading.hardwareId != right.reading.hardwareId)
            return left.reading.hardwareId < right.reading.hardwareId;
        if (left.reading.sensorId != right.reading.sensorId)
            return left.reading.sensorId < right.reading.sensorId;
        if (left.reading.sensorType != right.reading.sensorType)
            return left.reading.sensorType < right.reading.sensorType;
        if (left.reading.text != right.reading.text)
            return left.reading.text < right.reading.text;
        return left.treePosition < right.treePosition;
    });

    std::unordered_map<std::string, std::size_t> identityCounts;
    identityCounts.reserve(candidates.size());
    for (const Candidate& candidate : candidates)
    {
        ++identityCounts[candidate.baseIdentity];
    }

    SensorSnapshot parsed;
    parsed.hasValidSnapshot = true;
    parsed.totalSensorCount = candidates.size();
    parsed.readings.reserve(std::min(candidates.size(), MaxVisibleSensors));
    const std::size_t visibleCount = std::min(candidates.size(), MaxVisibleSensors);
    for (std::size_t sensorIndex = 0; sensorIndex < visibleCount; ++sensorIndex)
    {
        Candidate& candidate = candidates[sensorIndex];
        candidate.reading.identity = candidate.baseIdentity;
        if (identityCounts[candidate.baseIdentity] > 1)
        {
            candidate.reading.identity += "|" + candidate.reading.sensorType + "|" +
                candidate.reading.text + "|" + candidate.treePosition;
        }

        if (!CreateSettingKey("SensorVisible_", candidate.reading.identity, candidate.reading.settingKey) ||
            !CreateSettingKey("GroupExpanded_", candidate.reading.rootHardwareId, candidate.reading.groupSettingKey))
        {
            return false;
        }
        parsed.readings.push_back(std::move(candidate.reading));
    }

    snapshot = std::move(parsed);
    return true;
}

bool ReadLocalResponse(std::string& response)
{
    WinHttpHandle session{WinHttpOpen(
        L"OptiSensorPlugin/1.0",
        WINHTTP_ACCESS_TYPE_NO_PROXY,
        WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS,
        0)};
    if (session.value == nullptr || !WinHttpSetTimeouts(session.value, 1000, 1000, 1500, 1500))
    {
        return false;
    }

    WinHttpHandle connection{WinHttpConnect(session.value, L"127.0.0.1", 8085, 0)};
    if (connection.value == nullptr)
    {
        return false;
    }

    WinHttpHandle request{WinHttpOpenRequest(
        connection.value,
        L"GET",
        L"/data.json",
        nullptr,
        WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES,
        0)};
    if (request.value == nullptr)
    {
        return false;
    }

    DWORD redirectPolicy = WINHTTP_OPTION_REDIRECT_POLICY_NEVER;
    if (!WinHttpSetOption(
            request.value,
            WINHTTP_OPTION_REDIRECT_POLICY,
            &redirectPolicy,
            sizeof(redirectPolicy)) ||
        !WinHttpSendRequest(
            request.value,
            WINHTTP_NO_ADDITIONAL_HEADERS,
            0,
            WINHTTP_NO_REQUEST_DATA,
            0,
            0,
            0) ||
        !WinHttpReceiveResponse(request.value, nullptr))
    {
        return false;
    }

    DWORD status = 0;
    DWORD statusSize = sizeof(status);
    if (!WinHttpQueryHeaders(
            request.value,
            WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
            WINHTTP_HEADER_NAME_BY_INDEX,
            &status,
            &statusSize,
            WINHTTP_NO_HEADER_INDEX) ||
        status != 200)
    {
        return false;
    }

    response.clear();
    response.reserve(64 * 1024);
    std::array<char, 16 * 1024> buffer{};
    for (;;)
    {
        DWORD available = 0;
        if (!WinHttpQueryDataAvailable(request.value, &available))
        {
            return false;
        }
        if (available == 0)
        {
            return true;
        }
        if (available > MaxResponseBytes - response.size())
        {
            return false;
        }

        while (available > 0)
        {
            const DWORD requested = std::min(available, static_cast<DWORD>(buffer.size()));
            DWORD received = 0;
            if (!WinHttpReadData(request.value, buffer.data(), requested, &received) || received == 0)
            {
                return false;
            }
            response.append(buffer.data(), received);
            available -= received;
        }
    }
}

SensorSnapshot MakeFailedPollSnapshot(const SensorSnapshot* lastGood, std::uint32_t failedPollCount)
{
    if (lastGood == nullptr || !lastGood->hasValidSnapshot)
    {
        SensorSnapshot unavailable;
        unavailable.unavailable = true;
        return unavailable;
    }

    SensorSnapshot result = *lastGood;
    result.unavailable = failedPollCount >= 3;
    if (result.unavailable)
    {
        for (SensorReading& reading : result.readings)
        {
            reading.value.assign(NotAvailable);
        }
    }
    return result;
}
}
