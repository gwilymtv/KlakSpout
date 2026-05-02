using UnityEngine;

namespace Klak.Spout {

//
// Spout receiver class (main implementation)
//
[ExecuteInEditMode]
[AddComponentMenu("Klak/Spout/Spout Receiver")]
public sealed partial class SpoutReceiver : MonoBehaviour
{
    #region Receiver plugin object

    Receiver _receiver;

    void ReleaseReceiver()
    {
        _receiver?.Dispose();
        _receiver = null;
    }

    #endregion

    #region Buffer texture objects

    RenderTexture _buffer;
    RenderTexture _writeBuffer;

    RenderTexture PrepareBuffers()
    {
        if (_targetTexture != null)
        {
            Utility.Destroy(_buffer);      _buffer = null;
            Utility.Destroy(_writeBuffer); _writeBuffer = null;
            return _targetTexture;
        }

        var src = _receiver.Texture;

        if (_buffer != null &&
            (_buffer.width != src.width || _buffer.height != src.height))
        {
            Utility.Destroy(_buffer);      _buffer = null;
            Utility.Destroy(_writeBuffer); _writeBuffer = null;
        }
        if (_writeBuffer != null && !_useDoubleBuffer)
        {
            Utility.Destroy(_writeBuffer); _writeBuffer = null;
        }
        if (_buffer == null)
        {
            _buffer = new RenderTexture(src.width, src.height, 0);
            _buffer.hideFlags = HideFlags.DontSave;
            _buffer.Create();
        }
        if (_useDoubleBuffer && _writeBuffer == null)
        {
            _writeBuffer = new RenderTexture(src.width, src.height, 0);
            _writeBuffer.hideFlags = HideFlags.DontSave;
            _writeBuffer.Create();
        }
        return _useDoubleBuffer ? _writeBuffer : _buffer;
    }

    #endregion

    #region Diagnostics

    int    _copyAccum, _flushAccum;
    float  _diagWindowStart = -1f;
    System.IntPtr _lastTexturePtr = System.IntPtr.Zero;

    void UpdateDiagnostics(bool blitted, bool flushed, System.IntPtr ptr)
    {
        float now = Time.realtimeSinceStartup;
        if (_diagWindowStart < 0) _diagWindowStart = now;

        if (blitted) _copyAccum++;  else missedFrames++;
        if (flushed) _flushAccum++;

        if (ptr != System.IntPtr.Zero && ptr != _lastTexturePtr)
        {
            if (_lastTexturePtr != System.IntPtr.Zero) reconnectCount++;
            _lastTexturePtr = ptr;
        }

        float elapsed = now - _diagWindowStart;
        if (elapsed >= 1f)
        {
            copiesPerSecond  = _copyAccum  / elapsed;
            flushesPerSecond = _flushAccum / elapsed;
            _copyAccum = _flushAccum = 0;
            _diagWindowStart = now;
        }
    }

    #endregion

    #region MonoBehaviour implementation

    void OnEnable()
    {
        reconnectCount = 0;
        missedFrames = 0;
        _diagWindowStart = -1f;
        _lastTexturePtr = System.IntPtr.Zero;
    }

    void OnDisable()
    {
        ReleaseReceiver();
        _diagWindowStart = -1f;
    }

    void OnDestroy()
    {
        Utility.Destroy(_buffer);      _buffer = null;
        Utility.Destroy(_writeBuffer); _writeBuffer = null;
    }

    void Update()
    {
        // Receiver lazy initialization
        if (_receiver == null)
        {
            _receiver = new Receiver(_sourceName);
            _receiver.SetSyncMode(_syncToSender);
        }

        // Receiver plugin-side update
        _receiver.Update();

        // Do nothing further if no texture is ready yet.
        if (_receiver.Texture == null)
        {
            UpdateDiagnostics(false, false, System.IntPtr.Zero);
            return;
        }

        // Skip blit when the sender hasn't produced a new frame.
        if (!_receiver.IsFrameNew) return;

        // Received texture buffering
        var dest = PrepareBuffers();
        if (dest.isDataSRGB)
            Blitter.BlitFromSrgb(_resources, _receiver.Texture, dest);
        else
            Blitter.Blit(_resources, _receiver.Texture, dest, true);

        bool swapped = false;
        if (_useDoubleBuffer && _targetTexture == null && _writeBuffer != null)
        {
            (_buffer, _writeBuffer) = (_writeBuffer, _buffer);
            swapped = true;
        }

        var displayTarget = (_targetTexture != null) ? _targetTexture : _buffer;
        if (_targetRenderer != null)
            RendererOverride.SetTexture
              (_targetRenderer, _targetMaterialProperty, displayTarget);

        UpdateDiagnostics(true, swapped || !_useDoubleBuffer,
            _receiver.Texture.GetNativeTexturePtr());
    }

    #endregion
}

} // namespace Klak.Spout
