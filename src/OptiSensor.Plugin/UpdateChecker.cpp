#include "UpdateChecker.h"

#include <Windows.h>
#include <winhttp.h>

#include <algorithm>
#include <array>
#include <charconv>
#include <cstdint>
#include <string>
#include <string_view>

#pragma comment(lib, "winhttp.lib")

namespace OptiSensorPlugin::UpdateChecker
{
namespace
{
constexpr std::size_t kMaxUpdateResponseBytes = 64 * 1024;

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

bool ReadReleaseResponse(std::string& response)
{
    WinHttpHandle session{WinHttpOpen(
        L"OptiSensorPlugin/1.0",
        WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
        WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS,
        0)};
    if (session.value == nullptr || !WinHttpSetTimeouts(session.value, 3000, 3000, 3000, 3000))
    {
        return false;
    }

    WinHttpHandle connection{WinHttpConnect(session.value, L"api.github.com", INTERNET_DEFAULT_HTTPS_PORT, 0)};
    if (connection.value == nullptr)
    {
        return false;
    }

    WinHttpHandle request{WinHttpOpenRequest(
        connection.value,
        L"GET",
        L"/repos/onehoon/OptiSensor/releases/tags/OptiSensor.Plugin",
        nullptr,
        WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES,
        WINHTTP_FLAG_SECURE)};
    if (request.value == nullptr)
    {
        return false;
    }

    constexpr wchar_t headers[] =
        L"Accept: application/vnd.github+json\r\n"
        L"X-GitHub-Api-Version: 2022-11-28\r\n";
    if (!WinHttpSendRequest(
            request.value,
            headers,
            static_cast<DWORD>(-1),
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

    std::array<char, 4096> buffer{};
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
        if (available > kMaxUpdateResponseBytes - response.size())
        {
            return false;
        }

        while (available > 0)
        {
            const DWORD requested = std::min(available, static_cast<DWORD>(buffer.size()));
            DWORD received = 0;
            if (!WinHttpReadData(request.value, buffer.data(), requested, &received))
            {
                return false;
            }
            if (received == 0)
            {
                break;
            }

            response.append(buffer.data(), received);
            available -= received;
        }
    }
}

bool ExtractReleaseBody(const std::string& response, std::string& body)
{
    constexpr std::string_view field = "\"body\"";
    const std::size_t fieldPosition = response.find(field);
    if (fieldPosition == std::string::npos)
    {
        return false;
    }

    std::size_t position = fieldPosition + field.size();
    const auto skipWhitespace = [&response](std::size_t& index)
    {
        while (index < response.size() &&
               (response[index] == ' ' || response[index] == '\t' || response[index] == '\r' || response[index] == '\n'))
        {
            ++index;
        }
    };
    skipWhitespace(position);
    if (position >= response.size() || response[position++] != ':')
    {
        return false;
    }
    skipWhitespace(position);
    if (position >= response.size() || response[position++] != '"')
    {
        return false;
    }

    body.clear();
    while (position < response.size())
    {
        const char value = response[position++];
        if (value == '"')
        {
            return true;
        }
        if (static_cast<unsigned char>(value) < 0x20)
        {
            return false;
        }
        if (value != '\\')
        {
            body.push_back(value);
            continue;
        }
        if (position >= response.size())
        {
            return false;
        }

        switch (response[position++])
        {
        case '"': body.push_back('"'); break;
        case '\\': body.push_back('\\'); break;
        case '/': body.push_back('/'); break;
        case 'b': body.push_back('\b'); break;
        case 'f': body.push_back('\f'); break;
        case 'n': body.push_back('\n'); break;
        case 'r': body.push_back('\r'); break;
        case 't': body.push_back('\t'); break;
        case 'u':
        {
            if (position + 4 > response.size())
            {
                return false;
            }
            unsigned int codePoint = 0;
            const auto result = std::from_chars(
                response.data() + position,
                response.data() + position + 4,
                codePoint,
                16);
            if (result.ec != std::errc{} || result.ptr != response.data() + position + 4)
            {
                return false;
            }
            position += 4;
            body.push_back(codePoint <= 0x7f ? static_cast<char>(codePoint) : '?');
            break;
        }
        default:
            return false;
        }
    }

    return false;
}

bool ParseReleaseRunId(const std::string& body, std::uint64_t& runId)
{
    constexpr std::string_view marker = "Build-Run-ID:";
    bool found = false;
    std::size_t lineStart = 0;
    while (lineStart <= body.size())
    {
        const std::size_t lineEnd = body.find('\n', lineStart);
        const std::size_t length = lineEnd == std::string::npos ? body.size() - lineStart : lineEnd - lineStart;
        std::string_view line(body.data() + lineStart, length);
        if (!line.empty() && line.back() == '\r')
        {
            line.remove_suffix(1);
        }
        while (!line.empty() && (line.front() == ' ' || line.front() == '\t'))
        {
            line.remove_prefix(1);
        }
        while (!line.empty() && (line.back() == ' ' || line.back() == '\t'))
        {
            line.remove_suffix(1);
        }

        if (line.starts_with(marker))
        {
            if (found)
            {
                return false;
            }
            std::string_view digits = line.substr(marker.size());
            while (!digits.empty() && (digits.front() == ' ' || digits.front() == '\t'))
            {
                digits.remove_prefix(1);
            }
            std::uint64_t parsed = 0;
            const auto result = std::from_chars(digits.data(), digits.data() + digits.size(), parsed);
            if (digits.empty() || result.ec != std::errc{} || result.ptr != digits.data() + digits.size())
            {
                return false;
            }
            runId = parsed;
            found = true;
        }

        if (lineEnd == std::string::npos)
        {
            break;
        }
        lineStart = lineEnd + 1;
    }

    return found;
}

bool IsNewerRunId(std::uint64_t remoteRunId, std::uint64_t localRunId)
{
    return localRunId != 0 && remoteRunId > localRunId;
}

}

Status CheckStatus(std::uint64_t localBuildRunId)
{
    if (localBuildRunId == 0)
    {
        return Status::LocalBuild;
    }

    std::string response;
    std::string releaseBody;
    std::uint64_t remoteRunId = 0;
    if (!ReadReleaseResponse(response) ||
        !ExtractReleaseBody(response, releaseBody) ||
        !ParseReleaseRunId(releaseBody, remoteRunId))
    {
        // The optional network check must never interrupt the game.
        return Status::Unavailable;
    }

    return IsNewerRunId(remoteRunId, localBuildRunId)
        ? Status::UpdateAvailable
        : Status::UpToDate;
}
}
