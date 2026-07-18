using System.Buffers;
using System.Buffers.Binary;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Audio;

public sealed class WavAudioSink(string path) : IAudioSink
{
    private const int HeaderLength = 44;
    private readonly string _path = System.IO.Path.GetFullPath(path);
    private FileStream? _stream;
    private byte[]? _conversionBuffer;
    private AudioStreamFormat _format;
    private long _sampleCount;
    private int _completed;

    public string Path => _path;

    public async ValueTask InitializeAsync(
        SessionId sessionId,
        AudioStreamFormat format,
        CancellationToken cancellationToken)
    {
        if (_stream is not null)
        {
            throw new InvalidOperationException("The sink is already initialized.");
        }

        if (format.Channels != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(format),
                "The initial WAV sink supports mono audio.");
        }

        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (!String.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _format = format;
        _conversionBuffer = ArrayPool<byte>.Shared.Rent(format.BlockSize * 2);
        _stream = new FileStream(
            _path,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await _stream.WriteAsync(
            new byte[HeaderLength],
            cancellationToken);
    }

    public async ValueTask WriteAsync(
        ReadOnlyMemory<float> samples,
        long simulationBlock,
        CancellationToken cancellationToken)
    {
        FileStream stream = _stream
            ?? throw new InvalidOperationException("The sink is not initialized.");
        if (_completed != 0)
        {
            throw new InvalidOperationException("The sink is already complete.");
        }

        byte[] buffer = _conversionBuffer!;
        if (samples.Length * 2 > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samples),
                "The audio block is larger than the configured block size.");
        }

        ReadOnlySpan<float> source = samples.Span;
        Span<byte> destination = buffer.AsSpan(0, samples.Length * 2);
        for (int index = 0; index < source.Length; index++)
        {
            float normalized = Math.Clamp(source[index], -1F, 1F);
            short pcm = normalized <= -1F
                ? Int16.MinValue
                : (short)Math.Round(
                    normalized * Int16.MaxValue,
                    MidpointRounding.AwayFromZero);
            BinaryPrimitives.WriteInt16LittleEndian(
                destination.Slice(index * 2, 2),
                pcm);
        }

        await stream.WriteAsync(
            buffer.AsMemory(0, samples.Length * 2),
            cancellationToken);
        _sampleCount += samples.Length;
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        FileStream? stream = _stream;
        if (stream is null || Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        long dataLength = checked(_sampleCount * 2);
        if (dataLength > UInt32.MaxValue - HeaderLength)
        {
            throw new InvalidOperationException(
                "The WAV recording exceeds the RIFF size limit.");
        }

        byte[] header = CreateHeader(_format, (uint)dataLength);
        stream.Position = 0;
        await stream.WriteAsync(header, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CompleteAsync(CancellationToken.None);
        }
        finally
        {
            if (_stream is not null)
            {
                await _stream.DisposeAsync();
                _stream = null;
            }

            if (_conversionBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_conversionBuffer);
                _conversionBuffer = null;
            }
        }
    }

    private static byte[] CreateHeader(
        AudioStreamFormat format,
        uint dataLength)
    {
        byte[] header = new byte[HeaderLength];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(4, 4),
            dataLength + 36);
        "WAVE"u8.CopyTo(header.AsSpan(8));
        "fmt "u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(22, 2),
            checked((ushort)format.Channels));
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(24, 4),
            checked((uint)format.SampleRate));
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(28, 4),
            checked((uint)(format.SampleRate * format.Channels * 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(32, 2),
            checked((ushort)(format.Channels * 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34, 2), 16);
        "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40, 4), dataLength);
        return header;
    }
}
