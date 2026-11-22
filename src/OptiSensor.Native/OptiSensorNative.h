#pragma once

#include <stdint.h>

#define OPTISENSOR_MAX_READINGS 512
#define OPTISENSOR_IDENTIFIER_BYTES 256
#define OPTISENSOR_GROUP_IDENTIFIER_BYTES 256
#define OPTISENSOR_DEVICE_BYTES 128
#define OPTISENSOR_LABEL_BYTES 128
#define OPTISENSOR_SENSOR_TYPE_BYTES 32
#define OPTISENSOR_UNIT_BYTES 32
#define OPTISENSOR_SETTING_KEY_BYTES 80
#define OPTISENSOR_MESSAGE_BYTES 256

typedef enum OptiSensorStatus
{
    OptiSensor_StatusLoading = 0,
    OptiSensor_StatusReady = 1,
    OptiSensor_StatusError = 2,
    OptiSensor_StatusStopped = 3
} OptiSensorStatus;

#pragma pack(push, 4)
typedef struct OptiSensorReading
{
    int32_t groupDefaultExpanded;
    int32_t available;
    float value;
    char identifier[OPTISENSOR_IDENTIFIER_BYTES];
    char groupIdentifier[OPTISENSOR_GROUP_IDENTIFIER_BYTES];
    char device[OPTISENSOR_DEVICE_BYTES];
    char label[OPTISENSOR_LABEL_BYTES];
    char sensorType[OPTISENSOR_SENSOR_TYPE_BYTES];
    char unit[OPTISENSOR_UNIT_BYTES];
    char settingKey[OPTISENSOR_SETTING_KEY_BYTES];
    char groupSettingKey[OPTISENSOR_SETTING_KEY_BYTES];
} OptiSensorReading;

typedef struct OptiSensorSnapshotInfo
{
    int32_t status;
    int32_t count;
    int32_t totalCount;
    char message[OPTISENSOR_MESSAGE_BYTES];
} OptiSensorSnapshotInfo;
#pragma pack(pop)

#ifdef __cplusplus
extern "C" {
#endif

int32_t __cdecl OptiSensor_Start(void);
int32_t __cdecl OptiSensor_GetSnapshot(
    OptiSensorReading* readings,
    int32_t capacity,
    OptiSensorSnapshotInfo* info);
int32_t __cdecl OptiSensor_Stop(void);

#ifdef __cplusplus
}

static_assert(sizeof(OptiSensorReading) == 1004, "OptiSensorReading ABI layout changed");
static_assert(sizeof(OptiSensorSnapshotInfo) == 268, "OptiSensorSnapshotInfo ABI layout changed");
#endif
