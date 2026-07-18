using System.Diagnostics;
using MiniAudioExNET;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Audio;

public sealed record PhysicalAudioSinkOptions(
    string? DeviceName = null,
    int QueueDepth = 4);

public sealed record AudioDeviceInfo(
    string Name,
    int Index,
    bool IsDefault);

public enum PhysicalAudioSinkState
{
    Created,
    Running,
    Completed,
    Faulted,
    Disposed,
}

public sealed record PhysicalAudioSinkDiagnostics(
    PhysicalAudioSinkState State,
    int QueuedBlocks,
    long CallbackCount,
    long UnderrunCount,
    long DroppedBlockCount,
    long CallbackFaultCount,
    long LastSimulationBlock,
    TimeSpan TimeSinceLastCallback);

public sealed class PhysicalAudioSink :
    IAudioSink,
    IAudioSinkMetricsSource,
    IRecoverableAudioSink
{
    private static readonly object ContextGate = new();
    private static PhysicalAudioSink? s_contextOwner;

    private readonly PhysicalAudioSinkOptions _options;
    private string? _deviceName;
    private AudioBlockQueue? _queue;
    private AudioSource? _source;
    private AudioStreamFormat _format;
    private SessionId _sessionId;
    private long _lastCallbackTimestamp;
    private long _callbackCount;
    private long _underrunCount;
    private long _droppedBlockCount;
    private long _callbackFaultCount;
    private long _lastSimulationBlock = -1;
    private int _state = (int)PhysicalAudioSinkState.Created;

    public PhysicalAudioSink(PhysicalAudioSinkOptions? options = null)
    {
        _options = options ?? new();
        _deviceName = _options.DeviceName;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.QueueDepth);
    }

    public static IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices()
    {
        DeviceInfo[] devices = AudioContext.GetDevices() ?? [];
        return devices
            .Select(device => new AudioDeviceInfo(
                device.Name,
                device.Index,
                device.IsDefault))
            .ToArray();
    }

    public PhysicalAudioSinkDiagnostics GetDiagnostics()
    {
        long callbackTimestamp = Volatile.Read(ref _lastCallbackTimestamp);
        TimeSpan elapsed = callbackTimestamp == 0
            ? TimeSpan.MaxValue
            : Stopwatch.GetElapsedTime(callbackTimestamp);
        return new(
            (PhysicalAudioSinkState)Volatile.Read(ref _state),
            _queue?.Count ?? 0,
            Interlocked.Read(ref _callbackCount),
            Interlocked.Read(ref _underrunCount),
            Interlocked.Read(ref _droppedBlockCount),
            Interlocked.Read(ref _callbackFaultCount),
            Interlocked.Read(ref _lastSimulationBlock),
            elapsed);
    }

    public AudioSinkMetrics GetMetrics()
    {
        PhysicalAudioSinkDiagnostics diagnostics = GetDiagnostics();
        bool isHealthy = diagnostics.State == PhysicalAudioSinkState.Running
            && diagnostics.CallbackFaultCount == 0
            && diagnostics.TimeSinceLastCallback < TimeSpan.FromSeconds(2);
        return new(
            diagnostics.QueuedBlocks,
            diagnostics.UnderrunCount,
            diagnostics.DroppedBlockCount,
            isHealthy);
    }

    public ValueTask InitializeAsync(
        SessionId sessionId,
        AudioStreamFormat format,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (format.Channels != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(format),
                "The physical sink currently requires mono engine audio.");
        }

        lock (ContextGate)
        {
            PhysicalAudioSinkState state =
                (PhysicalAudioSinkState)Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(
                state == PhysicalAudioSinkState.Disposed,
                this);

            if (_source is not null)
            {
                throw new InvalidOperationException(
                    "The physical audio sink is already initialized.");
            }

            if (s_contextOwner is not null)
            {
                throw new InvalidOperationException(
                    "Only one physical audio sink may own the process audio device.");
            }

            s_contextOwner = this;
            try
            {
                _format = format;
                _sessionId = sessionId;
                _queue = new AudioBlockQueue(
                    _options.QueueDepth,
                    format.BlockSize);
                DeviceInfo? device = SelectDevice(_deviceName);
                AudioContext.Initialize(
                    checked((uint)format.SampleRate),
                    checked((uint)format.Channels),
                    device);
                _source = new AudioSource();
                if (_source.Handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "The physical audio device could not be initialized.");
                }

                _source.Read += OnAudioRead;
                Volatile.Write(
                    ref _lastCallbackTimestamp,
                    Stopwatch.GetTimestamp());
                _source.Play();
                Volatile.Write(
                    ref _state,
                    (int)PhysicalAudioSinkState.Running);
            }
            catch
            {
                ReleaseContext(PhysicalAudioSinkState.Faulted);
                throw;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<float> samples,
        long simulationBlock,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((PhysicalAudioSinkState)Volatile.Read(ref _state)
            != PhysicalAudioSinkState.Running)
        {
            throw new InvalidOperationException(
                "The physical audio sink is not running.");
        }

        if (samples.Length != _format.BlockSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samples),
                "Physical audio writes must contain one canonical block.");
        }

        if (!_queue!.TryWrite(samples.Span))
        {
            Interlocked.Increment(ref _droppedBlockCount);
        }

        Interlocked.Exchange(ref _lastSimulationBlock, simulationBlock);
        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (ContextGate)
        {
            ReleaseContext(PhysicalAudioSinkState.Completed);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecoverAsync(
        string? deviceName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SessionId sessionId;
        AudioStreamFormat format;
        lock (ContextGate)
        {
            ObjectDisposedException.ThrowIf(
                (PhysicalAudioSinkState)Volatile.Read(ref _state)
                    == PhysicalAudioSinkState.Disposed,
                this);
            sessionId = _sessionId;
            format = _format;
            _deviceName = deviceName ?? _deviceName;
            ReleaseContext(PhysicalAudioSinkState.Created);
        }

        return InitializeAsync(sessionId, format, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (ContextGate)
        {
            ReleaseContext(PhysicalAudioSinkState.Disposed);
        }

        return ValueTask.CompletedTask;
    }

    private static DeviceInfo? SelectDevice(string? deviceName)
    {
        if (String.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        DeviceInfo[] devices = AudioContext.GetDevices() ?? [];
        return devices.FirstOrDefault(
                device => String.Equals(
                    device.Name,
                    deviceName,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"Playback device '{deviceName}' was not found.",
                nameof(deviceName));
    }

    private void OnAudioRead(
        AudioBuffer<float> output,
        ulong frameCount,
        int channels)
    {
        try
        {
            bool underrun = false;
            int outputIndex = 0;
            for (ulong frame = 0; frame < frameCount; frame++)
            {
                bool hasSample = _queue!.TryReadSample(out float sample);
                underrun |= !hasSample;
                for (int channel = 0; channel < channels; channel++)
                {
                    output[outputIndex] = sample;
                    outputIndex++;
                }
            }

            if (underrun)
            {
                Interlocked.Increment(ref _underrunCount);
            }

            Interlocked.Increment(ref _callbackCount);
            Volatile.Write(
                ref _lastCallbackTimestamp,
                Stopwatch.GetTimestamp());
        }
        catch
        {
            Interlocked.Increment(ref _callbackFaultCount);
            Volatile.Write(
                ref _state,
                (int)PhysicalAudioSinkState.Faulted);
            for (int index = 0; index < output.Length; index++)
            {
                output[index] = 0f;
            }
        }
    }

    private void ReleaseContext(PhysicalAudioSinkState finalState)
    {
        if (_source is not null)
        {
            _source.Read -= OnAudioRead;
            _source.Stop();
            _source.Dispose();
            _source = null;
        }

        if (ReferenceEquals(s_contextOwner, this))
        {
            AudioContext.Deinitialize();
            s_contextOwner = null;
        }

        _queue = null;
        Volatile.Write(ref _state, (int)finalState);
    }
}
