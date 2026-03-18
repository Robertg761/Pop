#ifndef POP_MAC_BRIDGE_H
#define POP_MAC_BRIDGE_H

#include <stdint.h>

typedef struct {
    int32_t x;
    int32_t y;
} PopPointDto;

typedef struct {
    int32_t x;
    int32_t y;
    int32_t width;
    int32_t height;
} PopRectDto;

typedef struct {
    PopRectDto bounds;
    PopRectDto workArea;
} PopMonitorInfoDto;

typedef struct {
    uint8_t enabled;
    uint8_t launchAtStartup;
    double throwVelocityThresholdPxPerSec;
    double horizontalDominanceRatio;
    int32_t glideDurationMs;
    uint8_t enableDiagnostics;
} PopAppSettingsDto;

typedef struct {
    int32_t x;
    int32_t y;
    int64_t timestampUnixMilliseconds;
} PopDragSampleDto;

typedef struct {
    PopMonitorInfoDto initialMonitor;
    PopMonitorInfoDto currentMonitor;
    PopRectDto initialBounds;
    PopRectDto currentBounds;
    uint8_t isOptionPressedAtRelease;
} PopDragContextDto;

typedef struct {
    int32_t target;
    PopMonitorInfoDto targetMonitor;
    PopPointDto projectedLandingPoint;
    double horizontalVelocityPxPerSec;
    double verticalVelocityPxPerSec;
    double horizontalDominanceRatio;
    uint8_t isQualified;
    int32_t rejectionReason;
} PopSnapDecisionDto;

typedef struct {
    int32_t offsetMilliseconds;
    PopRectDto bounds;
} PopAnimationFrameDto;

typedef struct {
    void *frames;
    int32_t frameCount;
    PopRectDto finalBounds;
    int32_t durationMs;
    int32_t maxOvershootPx;
} PopAnimationPlanDto;

typedef struct {
    const char *key;
    const char *value;
} PopDiagnosticFieldDto;

PopSnapDecisionDto PopMacBridge_EvaluateDragGesture(
    const PopDragSampleDto *samples,
    int32_t sampleCount,
    const PopMonitorInfoDto *monitors,
    int32_t monitorCount,
    PopDragContextDto context,
    PopAppSettingsDto settings);

PopRectDto PopMacBridge_GetTileBounds(int32_t target, PopMonitorInfoDto monitor);

PopAnimationPlanDto PopMacBridge_CreateAnimationPlan(
    PopRectDto startBounds,
    PopRectDto targetBounds,
    double releaseVelocityX,
    int32_t durationMs);

void PopMacBridge_FreeAnimationPlan(PopAnimationPlanDto plan);

char *PopMacBridge_FormatDiagnosticEvent(
    int64_t timestampUnixMilliseconds,
    const char *category,
    const char *message,
    const PopDiagnosticFieldDto *fields,
    int32_t fieldCount);

void PopMacBridge_FreeUtf8String(void *pointer);

#endif
