#pragma once

#include "Common.h"
#include "System.h"
#include "Format.h"
#include "Spout/SpoutFrameCount.h"
#include <atomic>
#include <chrono>

namespace KlakSpout {

// DX11/12 compatible Spout receiver class
class Receiver final
{
public:

    Receiver(const char* name)
      : _name(name) {}

    ~Receiver()
    {
        _texture = nullptr;
        _frame.CleanupFrameCount();
    }

    void setSyncToSender(bool sync)
    {
        _syncToSender.store(sync);
    }

    void setSyncTimeout(int ms)
    {
        _syncTimeoutMs.store(ms > 0 ? ms : 1);
    }

    void setSyncSleep(int ms)
    {
        _syncSleepMs.store(ms >= 0 ? ms : 0);
    }

    void update()
    {
        // Wait for or poll a new frame from the sender.
        // WaitNewFrame blocks the render thread until the sender signals,
        // effectively pacing Unity to the sender's frame rate.
        // Both calls are no-ops until frame counting is enabled on connect.
        if (_syncToSender.load()) {
            auto t0 = std::chrono::steady_clock::now();
            bool got = _frame.WaitNewFrame(_syncTimeoutMs.load(), _syncSleepMs.load());
            _syncWaitMs   = std::chrono::duration<float, std::milli>(
                std::chrono::steady_clock::now() - t0).count();
            _syncTimedOut = !got;
        } else {
            _frame.GetNewFrame();
            _syncWaitMs   = 0.0f;
            _syncTimedOut = false;
        }
        _isFrameNew = _frame.IsFrameNew();
        _senderFps  = static_cast<float>(_frame.GetSenderFps());

        // Search the Spout name list.
        unsigned int width, height;
        HANDLE handle;
        DWORD format;
        auto res = _system->spout
          .CheckSender(_name.c_str(), width, height, handle, format);

        // Do nothing further if the current texture is valid.
        if (res && _texture && _width == width && _height == height) return;

        // On first successful connection, activate frame counting.
        // SetFrameCount writes the system registry key so that both this
        // receiver and any Spout2 sender (e.g. OBS via SpoutDX) participate
        // in the same semaphore-based frame signalling protocol.
        if (res && !_frameCountEnabled)
        {
            _frame.SetFrameCount(true);
            _frame.EnableFrameCount(_name.c_str());
            _frameCountEnabled = true;
        }

        HRESULT hres;

        if (_system->isD3D12)
        {
            // Handle -> D3D12Resource
            WRL::ComPtr<ID3D12Resource> resource;
            hres = _system->getD3D12Device()
              ->OpenSharedHandle(handle, IID_PPV_ARGS(&resource));
            _texture = resource;
        }
        else
        {
            // Handle -> D3D11Resource
            WRL::ComPtr<ID3D11Resource> resource;
            hres = _system->getD3D11Device()
              ->OpenSharedResource(handle, IID_PPV_ARGS(&resource));
            _texture = resource;
        }

        _width = width;
        _height = height;
        _format = ToFormat(static_cast<DXGI_FORMAT>(format));

        if (FAILED(hres)) LogError("OpenSharedResource", _name, hres);
    }

    // Receiver interop data structure
    // Should match with Klak.Spout.Plugin.ReceiverData (Plugin.cs)
    struct InteropData
    {
        unsigned int width, height;
        Format format;
        void* texture_pointer;
        int is_frame_new;
        float sync_wait_ms;
        float sender_fps;
        int sync_timed_out;
    };

    InteropData getInteropData() const
    {
        return InteropData
          { .width = _width, .height = _height, .format = _format,
            .texture_pointer = _texture.Get(),
            .is_frame_new    = _isFrameNew    ? 1 : 0,
            .sync_wait_ms    = _syncWaitMs,
            .sender_fps      = _senderFps,
            .sync_timed_out  = _syncTimedOut  ? 1 : 0 };
    }

private:

    std::string _name;
    unsigned int _width = 0, _height = 0;
    Format _format = {};
    WRL::ComPtr<IUnknown> _texture;
    spoutFrameCount _frame;
    std::atomic<bool> _syncToSender{false};
    std::atomic<int>  _syncTimeoutMs{33};
    std::atomic<int>  _syncSleepMs{4};
    bool  _isFrameNew = true;
    bool  _frameCountEnabled = false;
    bool  _syncTimedOut = false;
    float _syncWaitMs = 0.0f;
    float _senderFps  = 0.0f;
};

} // namespace KlakSpout
