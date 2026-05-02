#pragma once

#include "Common.h"
#include "System.h"
#include "Format.h"
#include "Spout/SpoutFrameCount.h"
#include <atomic>

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

    void update()
    {
        // Wait for or poll a new frame from the sender.
        // WaitNewFrame blocks the render thread until the sender signals,
        // effectively pacing Unity to the sender's frame rate.
        // Both calls are no-ops until frame counting is enabled on connect.
        if (_syncToSender.load())
            _frame.WaitNewFrame(100);
        else
            _frame.GetNewFrame();
        _isFrameNew = _frame.IsFrameNew();

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
    };

    InteropData getInteropData() const
    {
        return InteropData
          { .width = _width, .height = _height, .format = _format,
            .texture_pointer = _texture.Get(),
            .is_frame_new = _isFrameNew ? 1 : 0 };
    }

private:

    std::string _name;
    unsigned int _width = 0, _height = 0;
    Format _format = {};
    WRL::ComPtr<IUnknown> _texture;
    spoutFrameCount _frame;
    std::atomic<bool> _syncToSender{false};
    bool _isFrameNew = true;
    bool _frameCountEnabled = false;
};

} // namespace KlakSpout
