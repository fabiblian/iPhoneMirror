#include "Renderer/D3D11PreviewRenderer.h"
#include "iPhoneMirror/CoreApi.h"
#include <Windows.h>
#include <dwmapi.h>
#include <atomic>
#include <chrono>
#include <cmath>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <thread>

namespace {
void pump() {
    const auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(350);
    while (std::chrono::steady_clock::now() < end) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }
    DwmFlush();
}

COLORREF sample_at(HWND window, int dx, int dy) {
    RECT bounds{};
    GetWindowRect(window, &bounds);
    const auto dc = GetDC(nullptr);
    const auto pixel = GetPixel(dc, bounds.left + dx, bounds.top + dy);
    ReleaseDC(nullptr, dc);
    if (pixel == CLR_INVALID) throw std::runtime_error("Cannot sample the test window");
    return pixel;
}

int sample(HWND window) {
    RECT bounds{};
    GetWindowRect(window, &bounds);
    return GetRValue(sample_at(window, (bounds.right - bounds.left) / 2,
        (bounds.bottom - bounds.top) / 2));
}

struct TestWindow {
    HWND handle{};
    ~TestWindow() { if (handle) DestroyWindow(handle); }
};
}

// Opt-in interactive GPU smoke test. All sampled pixels belong to these
// synthetic test windows. No receiver, device, or capture session is started.
int run_preview_opacity_smoke() {
    try {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        WNDCLASSW type{};
        type.lpfnWndProc = DefWindowProcW;
        type.hInstance = GetModuleHandleW(nullptr);
        type.lpszClassName = L"PreviewOpacitySmoke";
        type.hbrBackground = static_cast<HBRUSH>(GetStockObject(WHITE_BRUSH));
        RegisterClassW(&type);
        RECT work{};
        SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
        TestWindow background{CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            type.lpszClassName, L"Opacity test background", WS_POPUP | WS_VISIBLE,
            work.left + 30, work.top + 30, 380, 190, nullptr, nullptr, type.hInstance, nullptr)};
        TestWindow first{CreateWindowExW(WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW,
            type.lpszClassName, L"Opacity test preview", WS_POPUP | WS_VISIBLE,
            work.left + 50, work.top + 50, 130, 130, background.handle, nullptr, type.hInstance, nullptr)};
        TestWindow second{CreateWindowExW(WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW,
            type.lpszClassName, L"Opacity test companion", WS_POPUP | WS_VISIBLE,
            work.left + 240, work.top + 50, 130, 130, background.handle, nullptr, type.hInstance, nullptr)};
        if (!background.handle || !first.handle || !second.handle)
            throw std::runtime_error("Could not create opacity test windows");
        std::atomic<std::shared_ptr<const iPhoneMirror::media::DecodedFrame>> frame;
        iPhoneMirror::renderer::D3D11PreviewRenderer preview(first.handle, [&] { return frame.load(); });
        iPhoneMirror::renderer::D3D11PreviewRenderer companion(second.handle, [] { return nullptr; });
        preview.set_corner_profile(0, 2.36F);
        companion.set_corner_profile(0, 2.36F);
        for (const bool with_frame : {false, true}) {
            if (with_frame) {
                auto synthetic = std::make_shared<iPhoneMirror::media::DecodedFrame>();
                synthetic->width = synthetic->height = 64;
                synthetic->stride = 64;
                synthetic->timestamp_100ns = 1;
                synthetic->nv12.assign(64 * 64, 16);
                synthetic->nv12.resize(64 * 64 * 3 / 2, 128);
                frame.store(synthetic);
            }
            preview.set_opacity(1.0F); pump();
            const auto opaque = sample(first.handle);
            preview.set_opacity(0.5F); pump();
            const auto half = sample(first.handle);
            preview.set_opacity(0.1F); pump();
            const auto faint = sample(first.handle);
            const auto unchanged = sample(second.handle);
            preview.set_opacity(1.0F); pump();
            const auto restored = sample(first.handle);
            std::cout << (with_frame ? "paused video" : "empty preview")
                << ": opaque=" << opaque << " half=" << half << " faint=" << faint
                << " companion=" << unchanged << " restored=" << restored << '\n';
            if (opaque > 30 || half < opaque + 30 || faint < half + 15 ||
                unchanged > 30 || std::abs(restored - opaque) > 8)
                throw std::runtime_error("Composition opacity did not blend independently over white");
        }
        for (const auto invalid : {0.0F, 1.1F, NAN})
            if (im_session_set_window_opacity(0, first.handle, invalid) == 0)
                throw std::runtime_error("Opacity API accepted invalid input");

        // Outline: an opaque red frame fills the band around the inset video,
        // the video stays dark, and removing the frame restores the edge.
        using iPhoneMirror::renderer::FrameOptions;
        const auto uniform = [](std::uint32_t inset, std::uint32_t argb, float roundness) {
            FrameOptions options;
            options.left = options.top = options.right = options.bottom = inset;
            options.argb = argb;
            options.roundness = roundness;
            return options;
        };
        preview.set_frame(uniform(20, 0xFFFF0000U, 1.0F)); pump();
        const auto edge = sample_at(first.handle, 6, 65);
        const auto centre = sample(first.handle);
        // Maximum roundness leaves the outermost corner pixel transparent.
        const auto corner = sample_at(first.handle, 1, 1);
        // Smooth corners: where the arc separates the white background from
        // the red frame, edge pixels are blended instead of a hard step.
        auto blended_corner_pixels = 0;
        {
            RECT bounds{};
            GetWindowRect(first.handle, &bounds);
            const auto dc = GetDC(nullptr);
            for (auto y = 0; y < 40; ++y)
                for (auto x = 0; x < 40; ++x) {
                    const auto pixel = GetPixel(dc, bounds.left + x, bounds.top + y);
                    const auto green = GetGValue(pixel);
                    if (pixel != CLR_INVALID && green > 40 && green < 215) ++blended_corner_pixels;
                }
            ReleaseDC(nullptr, dc);
        }
        preview.set_frame(uniform(0, 0xFFFF0000U, 0.0F)); pump();
        const auto edge_removed = sample_at(first.handle, 6, 65);
        std::cout << "frame: edge=" << static_cast<int>(GetRValue(edge)) << ','
            << static_cast<int>(GetGValue(edge)) << ',' << static_cast<int>(GetBValue(edge))
            << " centre=" << centre << " removed=" << static_cast<int>(GetRValue(edge_removed))
            << ',' << static_cast<int>(GetGValue(edge_removed)) << '\n';
        if (GetRValue(edge) < 200 || GetGValue(edge) > 60 || GetBValue(edge) > 60 ||
            centre > 30 || GetRValue(edge_removed) > 60)
            throw std::runtime_error("Composition frame did not draw around the inset video");
        // The background window is white; a red corner means no rounding.
        if (GetGValue(corner) < 200)
            throw std::runtime_error("Composition frame corners are not rounded");
        std::cout << "frame corner: blended=" << blended_corner_pixels << '\n';
        if (blended_corner_pixels == 0)
            throw std::runtime_error("Composition frame corners are not anti-aliased");

        // Home bar: a taller bottom band moves the video up; the centred pill
        // is light on the dark-red outline.
        auto bar = uniform(10, 0xFFFF0000U, 0.0F);
        bar.bottom = 40;
        bar.button_align = 2;
        bar.button_width = 40;
        bar.button_height = 6;
        bar.button_center_y = 20;
        preview.set_frame(bar); pump();
        const auto band = sample_at(first.handle, 30, 95);
        const auto pill = sample_at(first.handle, 65, 110);
        const auto video = sample_at(first.handle, 65, 60);
        std::cout << "home bar: band=" << static_cast<int>(GetRValue(band)) << ','
            << static_cast<int>(GetGValue(band)) << " pill=" << static_cast<int>(GetRValue(pill))
            << ',' << static_cast<int>(GetGValue(pill)) << " video="
            << static_cast<int>(GetRValue(video)) << '\n';
        if (GetRValue(band) < 200 || GetGValue(band) > 60 || GetGValue(pill) < 150 ||
            GetRValue(video) > 30)
            throw std::runtime_error("Composition home bar or pill was not drawn");

        // Automatic colour: a bright video turns the outline bright and the
        // pill dark, replacing the configured red.
        auto bright = std::make_shared<iPhoneMirror::media::DecodedFrame>();
        bright->width = bright->height = 64;
        bright->stride = 64;
        bright->timestamp_100ns = 2;
        bright->nv12.assign(64 * 64, 235);
        bright->nv12.resize(64 * 64 * 3 / 2, 128);
        frame.store(bright);
        auto automatic = bar;
        automatic.auto_color = true;
        preview.set_frame(automatic); pump(); pump();
        const auto auto_band = sample_at(first.handle, 30, 95);
        const auto auto_pill = sample_at(first.handle, 65, 110);
        std::cout << "auto colour: band=" << static_cast<int>(GetRValue(auto_band)) << ','
            << static_cast<int>(GetGValue(auto_band)) << ',' << static_cast<int>(GetBValue(auto_band))
            << " pill=" << static_cast<int>(GetGValue(auto_pill)) << '\n';
        if (GetGValue(auto_band) < 200 || GetBValue(auto_band) < 200 || GetGValue(auto_pill) > 120)
            throw std::runtime_error("Automatic frame colour did not follow the video");
        preview.set_frame(FrameOptions{}); pump();

        iPhoneMirror::WindowFrame invalid{};
        invalid.struct_size = sizeof(invalid);
        if (im_session_set_window_frame2(0, first.handle, nullptr) == 0)
            throw std::runtime_error("Frame API accepted a null structure");
        auto too_small = invalid;
        too_small.struct_size = 4;
        auto thick = invalid;
        thick.bottom = 513;
        auto negative = invalid;
        negative.left = -1;
        auto round = invalid;
        round.roundness = NAN;
        auto align = invalid;
        align.button_align = static_cast<iPhoneMirror::WindowFrameButtonAlign>(4);
        for (const auto& candidate : {too_small, thick, negative, round, align})
            if (im_session_set_window_frame2(0, first.handle, &candidate) == 0)
                throw std::runtime_error("Frame API accepted invalid input");
        std::cout << "Native preview opacity smoke passed.\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
