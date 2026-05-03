using UnityEngine;

namespace Klak.Spout {

//
// Spout receiver class (properties)
//
partial class SpoutReceiver
{
    #region Spout source

    [SerializeField] string _sourceName = null;

    public string sourceName
      { get => _sourceName;
        set => ChangeSourceName(value); }

    void ChangeSourceName(string name)
    {
        // Receiver refresh on source changes
        if (_sourceName == name) return;
        _sourceName = name;
        ReleaseReceiver();
    }

    #endregion

    #region Destination settings

    [SerializeField] RenderTexture _targetTexture = null;

    public RenderTexture targetTexture
      { get => _targetTexture;
        set => _targetTexture = value; }

    [SerializeField] Renderer _targetRenderer = null;

    public Renderer targetRenderer
      { get => _targetRenderer;
        set => _targetRenderer = value; }

    [SerializeField] string _targetMaterialProperty = null;

    public string targetMaterialProperty
      { get => _targetMaterialProperty;
        set => _targetMaterialProperty = value; }

    #endregion

    #region Sync settings

    [SerializeField, Tooltip("Block the render thread until the sender produces a new frame, pacing Unity to the sender's frame rate. Disabling VSync is recommended when using this. When disabled, Unity runs at its own rate and skips rendering if no new frame has arrived.")]
    bool _syncToSender = false;

    public bool syncToSender
      { get => _syncToSender;
        set => SetSyncToSender(value); }

    void SetSyncToSender(bool sync)
    {
        _syncToSender = sync;
        _receiver?.SetSyncMode(sync);
    }

    [SerializeField, Min(1), Tooltip("How long to wait for a new frame from the sender before giving up, in milliseconds. Lower values reduce latency when the sender stalls; higher values tolerate slower senders.")]
    int _syncTimeoutMs = 33;

    public int syncTimeoutMs
      { get => _syncTimeoutMs;
        set => SetSyncTimeout(value); }

    void SetSyncTimeout(int ms)
    {
        _syncTimeoutMs = Mathf.Max(1, ms);
        _receiver?.SetSyncTimeout(_syncTimeoutMs);
    }

    [SerializeField, Min(0), Tooltip("How long to sleep between polling attempts while waiting for a new sender frame, in milliseconds. Lower values increase sync accuracy at the cost of CPU usage. 0 spins as fast as possible (yields the OS time slice between polls but does not sleep).")]
    int _syncSleepMs = 4;

    public int syncSleepMs
      { get => _syncSleepMs;
        set => SetSyncSleepMs(value); }

    void SetSyncSleepMs(int ms)
    {
        _syncSleepMs = Mathf.Max(0, ms);
        _receiver?.SetSyncSleep(_syncSleepMs);
    }

    #endregion

    #region Runtime property

    public RenderTexture receivedTexture
      => _buffer != null ? _buffer : _targetTexture;

    #endregion

    #region Resource asset reference

    [SerializeField, HideInInspector] SpoutResources _resources = null;

    public void SetResources(SpoutResources resources)
      => _resources = resources;

    #endregion

    #region Double-buffer option

    [SerializeField, Tooltip("Maintain two receive buffers so Unity always displays a fully written frame while the next one is being received. Reduces tearing and flickering at the cost of one extra frame of latency.")]
    bool _useDoubleBuffer = false;

    public bool useDoubleBuffer
      { get => _useDoubleBuffer;
        set => _useDoubleBuffer = value; }

    #endregion

    #region Diagnostics

    public float copiesPerSecond  { get; private set; }
    public float flushesPerSecond { get; private set; }
    public int   reconnectCount   { get; private set; }
    public int   missedFrames     { get; private set; }

    #endregion
}

} // namespace Klak.Spout
