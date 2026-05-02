using UnityEngine;
using UnityEngine.Rendering;

namespace Klak.Spout {

//
// Spout sender class (main implementation)
//
[ExecuteInEditMode]
[AddComponentMenu("Klak/Spout/Spout Sender")]
public sealed partial class SpoutSender : MonoBehaviour
{
    #region Sender plugin object

    Sender _sender;

    void ReleaseSender()
    {
        _sender?.Dispose();
        _sender = null;
    }

    #endregion

    #region Buffer texture objects

    RenderTexture _buffer;
    RenderTexture _altBuffer;
    int _writeSlot;

    RenderTexture ActiveWriteBuffer
        => (_useDoubleBuffer && _writeSlot == 1) ? _altBuffer : _buffer;

    void PrepareBuffer(int width, int height)
    {
        if (_buffer != null &&
            (_buffer.width != width || _buffer.height != height))
        {
            ReleaseSender();
            Utility.Destroy(_buffer);    _buffer = null;
            Utility.Destroy(_altBuffer); _altBuffer = null;
        }
        if (_altBuffer != null && !_useDoubleBuffer)
        {
            Utility.Destroy(_altBuffer); _altBuffer = null;
        }
        if (width <= 0 || height <= 0) return;
        if (_buffer == null)
        {
            _buffer = new RenderTexture(width, height, 0);
            _buffer.hideFlags = HideFlags.DontSave;
            _buffer.Create();
        }
        if (_useDoubleBuffer && _altBuffer == null)
        {
            _altBuffer = new RenderTexture(width, height, 0);
            _altBuffer.hideFlags = HideFlags.DontSave;
            _altBuffer.Create();
        }
    }

    #endregion

    #region Camera capture (SRP)

    Camera _attachedCamera;

    void OnCameraCapture(RenderTargetIdentifier source, CommandBuffer cb)
    {
        if (_attachedCamera == null) return;
        var target = (_useDoubleBuffer && _altBuffer != null) ? _altBuffer : _buffer;
        Blitter.Blit(_resources, cb, source, target, _keepAlpha);
    }

    void PrepareCameraCapture(Camera target)
    {
        // If it has been attached to another camera, detach it first.
        if (_attachedCamera != null && _attachedCamera != target)
        {
            #if KLAK_SPOUT_HAS_SRP
            CameraCaptureBridge
              .RemoveCaptureAction(_attachedCamera, OnCameraCapture);
            #endif
            _attachedCamera = null;
        }

        // Attach to the target if it hasn't been attached yet.
        if (_attachedCamera == null && target != null)
        {
            #if KLAK_SPOUT_HAS_SRP
            CameraCaptureBridge
              .AddCaptureAction(target, OnCameraCapture);
            #endif
            _attachedCamera = target;
        }
    }

    #endregion

    #region Capture coroutine

    System.Collections.IEnumerator CaptureCoroutine()
    {
        for (var eof = new WaitForEndOfFrame(); true;)
        {
            yield return eof;
            CaptureFrame();
        }
    }

    void CaptureFrame()
    {
        // GameView capture mode
        if (_captureMethod == CaptureMethod.GameView)
        {
            PrepareBuffer(Screen.width, Screen.height);
            RenderTexture.active = null;
            var temp = RenderTexture.GetTemporary(Screen.width, Screen.height, 0);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(temp);
            Blitter.BlitVFlip(_resources, temp, ActiveWriteBuffer, _keepAlpha);
            RenderTexture.ReleaseTemporary(temp);
        }

        // Texture capture mode
        if (_captureMethod == CaptureMethod.Texture)
        {
            if (_sourceTexture == null) return;
            PrepareBuffer(_sourceTexture.width, _sourceTexture.height);
            Blitter.Blit(_resources, _sourceTexture, ActiveWriteBuffer, _keepAlpha);
        }

        // Camera capture mode
        if (_captureMethod == CaptureMethod.Camera)
        {
            PrepareCameraCapture(_sourceCamera);
            if (_sourceCamera == null) return;
            PrepareBuffer(_sourceCamera.pixelWidth, _sourceCamera.pixelHeight);
            // OnCameraCapture already wrote to _altBuffer (or _buffer) this frame.
        }

        // Sender lazy initialization
        if (_sender == null) _sender = new Sender(_spoutName, _buffer);

        // Sender plugin-side update
        if (_useDoubleBuffer && _altBuffer != null)
        {
            (_buffer, _altBuffer) = (_altBuffer, _buffer);
            _writeSlot = 1 - _writeSlot;
            _sender.Update(_buffer);
        }
        else
        {
            _sender.Update();
        }
    }

    #endregion

    #region MonoBehaviour implementation

    void OnEnable()
      => StartCoroutine(CaptureCoroutine());

    void OnDisable()
    {
        StopAllCoroutines();
        ReleaseSender();
        PrepareBuffer(0, 0);
        PrepareCameraCapture(null);
    }

    #endregion
}

} // namespace Klak.Spout
