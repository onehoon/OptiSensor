#pragma once

#include <cstdint>

namespace OptiSensorPlugin::UpdateChecker
{
enum class Status : std::uint8_t
{
    Checking,
    UpToDate,
    UpdateAvailable,
    Unavailable,
    LocalBuild
};

Status CheckStatus(std::uint64_t localBuildRunId);
}
