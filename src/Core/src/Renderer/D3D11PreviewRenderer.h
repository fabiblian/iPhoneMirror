#pragma once

#include "Media/MediaFoundationDecoder.h"

#include <Windows.h>

#include <cstdint>
#include <functional>
#include <memory>

namespace iPhoneMirror::renderer {

enum class MonitorHdrCapability : std::uint8_t {
    Unknown,
    Sdr,
    Hdr,
};

struct OutputDiagnostics {
    MonitorHdrCapability monitor_capability{MonitorHdrCapability::Unknown};
    bool source_hdr_known{};
    bool source_hdr{};
    bool actual_hdr_surface{};
    bool hdr_effective{};
    media::ColorOutputPreference requested_preference{
        media::ColorOutputPreference::Auto};
};

#ifdef IPHONEMIRROR_PREVIEW_TEST_HOOKS
// Per-renderer fault injection for the opt-in synthetic GPU smoke test only.
// S_OK from before_present delegates to the real swap-chain presentation.
struct PreviewTestHooks {
    std::function<bool()> before_upload;
    std::function<HRESULT()> before_present;
    std::function<void(HRESULT)> after_present;
};
#endif

// Outline around the video of a top-level composition preview, in physical
// pixels. The video is inset by left/top/right/bottom; all zero removes the
// outline. argb is a straight-alpha 0xAARRGGBB colour (alpha = opacity);
// auto_color replaces its RGB with the colour at the video's edges.
// roundness 0 keeps the outer corners concentric with the video; 1 rounds
// them as far as possible without exposing the video's corners.
// The home pill is button_width x button_height with its centre
// button_center_y above the client bottom; button_align 0 = none, 1 = left,
// 2 = centre, 3 = right (left/right: centre button_offset inside the video edge).
struct FrameOptions {
    std::uint32_t left{};
    std::uint32_t top{};
    std::uint32_t right{};
    std::uint32_t bottom{};
    std::uint32_t argb{};
    float roundness{};
    bool auto_color{};
    bool button_hover{};
    std::int32_t button_align{};
    std::int32_t button_offset{};
    std::int32_t button_width{};
    std::int32_t button_height{};
    std::int32_t button_center_y{};

    bool operator==(const FrameOptions&) const = default;
};

class D3D11PreviewRenderer {
public:
    using FrameProvider = std::function<std::shared_ptr<const media::DecodedFrame>()>;

    D3D11PreviewRenderer(HWND window, FrameProvider provider
#ifdef IPHONEMIRROR_PREVIEW_TEST_HOOKS
        , PreviewTestHooks test_hooks = {}
#endif
    );
    ~D3D11PreviewRenderer();
    D3D11PreviewRenderer(const D3D11PreviewRenderer&) = delete;
    D3D11PreviewRenderer& operator=(const D3D11PreviewRenderer&) = delete;

    // Re-present the newest complete frame even when its media timestamp has
    // not changed. Used after a window resize/device restore and by the GUI's
    // explicit refresh action.
    void request_refresh() noexcept;
    // Drops the retained frame and presents black on the render thread. This
    // prevents the previous iPhone's last frame from remaining visible after
    // the user selects another device.
    void clear() noexcept;
    void set_max_fps(std::uint32_t fps) noexcept;
    // Limits the local GPU intermediate render texture while leaving the
    // decoded/USB source untouched. (0,0) uses native source resolution.
    void set_render_size_limit(std::uint32_t width, std::uint32_t height) noexcept;
    // Configures the top-level preview's continuous display outline. Radius is
    // normalized to the short edge; zero disables clipping. Child/main-window
    // previews remain rectangular because their WPF panel owns that shape.
    void set_corner_profile(float normalized_radius, float curve_exponent) noexcept;
    void set_rotation(std::int32_t quarter_turns) noexcept;
    // Applies to this composition visual only, including its black background.
    void set_opacity(float opacity) noexcept;
    // Draws an outline around the video inside a top-level composition
    // preview (see FrameOptions). Child/main-window previews ignore it.
    void set_frame(const FrameOptions& options) noexcept;
    void set_color_output_preference(media::ColorOutputPreference preference) noexcept;
    void set_image_adjustments(float brightness, float contrast,
        float saturation, float gamma) noexcept;
    // Lock-free snapshot intended for UI diagnostics. HDR is effective only
    // for an HDR source on an HDR monitor with a committed FP16/scRGB surface.
    [[nodiscard]] OutputDiagnostics output_diagnostics() const noexcept;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace iPhoneMirror::renderer
