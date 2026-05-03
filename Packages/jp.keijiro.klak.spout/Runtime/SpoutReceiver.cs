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

    #region Buffer texture object

    RenderTexture _buffer;

    RenderTexture PrepareBuffer()
    {
        // Receive-to-Texture mode:
        // Destroy the internal buffer and return the target texture.
        if (_targetTexture != null)
        {
            if (_buffer != null)
            {
                Utility.Destroy(_buffer);
                _buffer = null;
            }
            return _targetTexture;
        }

        var src = _receiver.Texture;

        // If the buffer exists but has wrong dimensions, destroy it first.
        if (_buffer != null &&
            (_buffer.width != src.width || _buffer.height != src.height))
        {
            Utility.Destroy(_buffer);
            _buffer = null;
        }

        // Create a buffer if it hasn't been allocated yet.
        if (_buffer == null)
        {
            _buffer = new RenderTexture(src.width, src.height, 0);
            _buffer.hideFlags = HideFlags.DontSave;
            _buffer.Create();
        }

        return _buffer;
    }

    #endregion

    #region Diagnostics

    int    _copyAccum, _syncWaitSamples;
    float  _diagWindowStart = -1f;
    float  _syncWaitAccum;
    float  _diagLogTimer;
    float  _diagDisplayTimer;
    string _diagDisplayText;
    System.IntPtr _lastTexturePtr = System.IntPtr.Zero;

    string FormatDiagnostics()
        => $"[SpoutReceiver \"{_sourceName}\"] " +
           $"copies:{copiesPerSecond:F1}/s  " +
           $"sender:{senderFps:F1}fps  syncWait:{avgSyncWaitMs:F1}ms  " +
           $"missed:{missedFrames}  timeouts:{syncTimeouts}  reconnects:{reconnectCount}";

    void UpdateDiagnostics(bool blitted, System.IntPtr ptr,
                           float syncWaitMs, float curSenderFps, bool syncTimedOut)
    {
        float now = Time.realtimeSinceStartup;
        if (_diagWindowStart < 0) _diagWindowStart = now;

        if (blitted) _copyAccum++;  else missedFrames++;
        if (syncTimedOut) syncTimeouts++;
        _syncWaitAccum += syncWaitMs;
        _syncWaitSamples++;

        if (ptr != System.IntPtr.Zero && ptr != _lastTexturePtr)
        {
            if (_lastTexturePtr != System.IntPtr.Zero) reconnectCount++;
            _lastTexturePtr = ptr;
        }

        float elapsed = now - _diagWindowStart;
        if (elapsed >= 1f)
        {
            copiesPerSecond = _copyAccum / elapsed;
            avgSyncWaitMs   = _syncWaitAccum / _syncWaitSamples;
            senderFps       = curSenderFps;
            _copyAccum = _syncWaitSamples = 0;
            _syncWaitAccum = 0f;
            _diagWindowStart = now;
        }

        if (_diagLogInterval > 0)
        {
            _diagLogTimer += Time.deltaTime;
            if (_diagLogTimer >= _diagLogInterval)
            {
                Debug.Log(FormatDiagnostics());
                _diagLogTimer = 0f;
            }
        }

        if (_diagDisplayInterval > 0)
        {
            _diagDisplayTimer += Time.deltaTime;
            if (_diagDisplayTimer >= _diagDisplayInterval)
            {
                _diagDisplayText = FormatDiagnostics();
                _diagDisplayTimer = 0f;
            }
        }
    }

    void OnGUI()
    {
        if (_diagDisplayText == null) return;
        GUI.Label(new Rect(10, 10, Screen.width - 20, 20), _diagDisplayText);
    }

    #endregion

    #region MonoBehaviour implementation

    void OnEnable()
    {
        reconnectCount = 0;
        missedFrames   = 0;
        syncTimeouts   = 0;
        _diagWindowStart = -1f;
        _lastTexturePtr  = System.IntPtr.Zero;
        _syncWaitAccum   = 0f;
        _syncWaitSamples = 0;
        _diagLogTimer    = 0f;
        _diagDisplayTimer = 0f;
        _diagDisplayText  = null;
    }

    void OnValidate()
    {
        _syncTimeoutMs = Mathf.Max(1, _syncTimeoutMs);
        _syncSleepMs   = Mathf.Max(0, _syncSleepMs);
        _receiver?.SetSyncMode(_syncToSender);
        _receiver?.SetSyncTimeout(_syncTimeoutMs);
        _receiver?.SetSyncSleep(_syncSleepMs);
    }

    void OnDisable()
    {
        ReleaseReceiver();
        _diagWindowStart = -1f;
    }

    void OnDestroy()
    {
        Utility.Destroy(_buffer);
        _buffer = null;
    }

    void Update()
    {
        // Receiver lazy initialization
        if (_receiver == null)
        {
            _receiver = new Receiver(_sourceName);
            _receiver.SetSyncMode(_syncToSender);
            _receiver.SetSyncTimeout(_syncTimeoutMs);
            _receiver.SetSyncSleep(_syncSleepMs);
        }

        // Receiver plugin-side update
        _receiver.Update();

        // Do nothing further if no texture is ready yet.
        if (_receiver.Texture == null)
        {
            UpdateDiagnostics(false, System.IntPtr.Zero,
                _receiver.SyncWaitMs, _receiver.SenderFps, _receiver.SyncTimedOut);
            return;
        }

        // Skip blit when the sender hasn't produced a new frame.
        if (!_receiver.IsFrameNew)
        {
            UpdateDiagnostics(false, _receiver.Texture.GetNativeTexturePtr(),
                _receiver.SyncWaitMs, _receiver.SenderFps, _receiver.SyncTimedOut);
            return;
        }

        // Received texture buffering
        var buffer = PrepareBuffer();
        if (buffer.isDataSRGB)
            Blitter.BlitFromSrgb(_resources, _receiver.Texture, buffer);
        else
            Blitter.Blit(_resources, _receiver.Texture, buffer, true);

        // Renderer override
        if (_targetRenderer != null)
            RendererOverride.SetTexture
              (_targetRenderer, _targetMaterialProperty, buffer);

        UpdateDiagnostics(true,
            _receiver.Texture.GetNativeTexturePtr(),
            _receiver.SyncWaitMs, _receiver.SenderFps, _receiver.SyncTimedOut);
    }

    #endregion
}

} // namespace Klak.Spout
