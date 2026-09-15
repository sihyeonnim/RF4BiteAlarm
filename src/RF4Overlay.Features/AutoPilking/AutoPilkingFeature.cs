using RF4Overlay.Core.Features;

namespace RF4Overlay.Features.AutoPilking;

public sealed class AutoPilkingFeature() : PlannedFeature(
    FeatureId.AutoPilking, "Auto Pilking",
    "구현 보류 · 실제 구현 전 RF4의 최신 자동화 운영정책을 확인해야 합니다.");
