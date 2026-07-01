#pragma once

#include <cstddef>
#include <cstdint>

namespace OptiExternalOverlay
{
constexpr wchar_t MappingName[] = L"Local\\OptiScalerExternalOverlay";
constexpr uint32_t PayloadMagic = 0x564F534F; // OSOV
constexpr uint32_t PayloadVersion = 1;
constexpr uint32_t MaxLines = 4;
constexpr uint32_t MaxLineLength = 128;
constexpr uint64_t StaleAfterMs = 2000;

struct Payload
{
    uint32_t magic;
    uint32_t version;
    volatile uint32_t sequence;
    uint64_t lastUpdateTickMs;
    uint32_t lineCount;
    char lines[MaxLines][MaxLineLength];
};

static_assert(offsetof(Payload, lastUpdateTickMs) == 16);
static_assert(offsetof(Payload, lineCount) == 24);
static_assert(offsetof(Payload, lines) == 28);
static_assert(sizeof(Payload) == 544);
}
