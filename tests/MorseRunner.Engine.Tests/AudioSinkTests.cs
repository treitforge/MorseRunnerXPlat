using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

public sealed class AudioSinkTests
{
    private static readonly ClientId TestClient = new("audio-test");

    [Fact]
    public void DefaultMonitorLevelMatchesLegacyFullVolume()
    {
        SessionSettings settings = SessionSettings.CreateDefault(seed: 12_345);

        Assert.Equal(0d, settings.MonitorLevelDb);
    }

    [Fact]
    public async Task FullMonitorProducesCePipelineLocalAudio()
    {
        float[] samples = await RenderFirstOperatorBlockAsync(
            monitorLevelDb: 0d);

        Assert.Equal(
            "7d925cbba9a0bb2e86a48c5a1777c347cfed68080a559446d8e3ed3c9d6af4ee",
            ComputeRawSingleSha256(samples));
        Assert.InRange(samples.Max(MathF.Abs), 0.6103517f, 0.6103518f);
    }

    [Fact]
    public async Task MonitorLevelAttenuatesLocalInputBeforeSharedAgc()
    {
        float[] full = await RenderFirstOperatorBlockAsync(
            monitorLevelDb: 0d);
        float[] reduced = await RenderFirstOperatorBlockAsync(
            monitorLevelDb: -20d);

        double ratio = RootMeanSquare(reduced) / RootMeanSquare(full);
        Assert.InRange(ratio, 0.854d, 0.855d);
    }

    [Fact]
    public async Task MinusSixtyDbMonitorProducesCeSignedZeroMute()
    {
        float[] samples = await RenderFirstOperatorBlockAsync(
            monitorLevelDb: -60d);

        Assert.All(samples, sample => Assert.Equal(0f, MathF.Abs(sample)));
        Assert.Equal(
            "b73ce67d7f6a60efbc46929d114471b7e79ddaee5b5a60a350a2c6a0a3ce3e6a",
            ComputeRawSingleSha256(samples));
    }

    [Fact]
    public async Task QskDucksReceiverBeforeTheSharedCePipeline()
    {
        float[] samples = await RenderFirstOperatorBlockAsync(
            monitorLevelDb: 0d,
            qskEnabled: true);

        Assert.Equal(
            "a4568db0f89409e3bf3640cd4d3a8e04fe619e20467a98674f6c6dbf5dca85f3",
            ComputeRawSingleSha256(samples));
    }

    [Fact]
    public async Task MonitorLevelDoesNotAttenuateReceiverAudio()
    {
        float[] full = await RenderFirstReceiverBlockAsync(
            monitorLevelDb: 0d);
        float[] reduced = await RenderFirstReceiverBlockAsync(
            monitorLevelDb: -20d);

        Assert.Equal(full, reduced);
        Assert.True(RootMeanSquare(full) > 0d);
    }

    [Fact]
    public async Task WavSinkWritesDeterministicPcm16HeaderAndSamples()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"morse-runner-{Guid.NewGuid():N}.wav");
        try
        {
            await using WavAudioSink sink = new(path);
            await sink.InitializeAsync(
                SessionId.New(),
                new AudioStreamFormat(11_025, Channels: 1, BlockSize: 4),
                TestContext.Current.CancellationToken);
            await sink.WriteAsync(
                new float[] { -1F, -0.5F, 0.5F, 1F },
                simulationBlock: 0,
                TestContext.Current.CancellationToken);
            await sink.CompleteAsync(TestContext.Current.CancellationToken);
            await sink.DisposeAsync();

            byte[] bytes = await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(52, bytes.Length);
            Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)));
            Assert.Equal(
                [-Int16.MaxValue, -16384, 16384, Int16.MaxValue],
                Enumerable.Range(0, 4)
                    .Select(
                        index => BinaryPrimitives.ReadInt16LittleEndian(
                            bytes.AsSpan(44 + index * 2, 2))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WavSinkUsesCeTiesToEvenPcm16Conversion()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"morse-runner-{Guid.NewGuid():N}.wav");
        try
        {
            float scale = Int16.MaxValue;
            float[] input =
            [
                -2.5f / scale,
                -1.5f / scale,
                -0.5f / scale,
                0.5f / scale,
                1.5f / scale,
                2.5f / scale,
            ];
            await using WavAudioSink sink = new(path);
            await sink.InitializeAsync(
                SessionId.New(),
                new AudioStreamFormat(
                    11_025,
                    Channels: 1,
                    BlockSize: input.Length),
                TestContext.Current.CancellationToken);
            await sink.WriteAsync(
                input,
                simulationBlock: 0,
                TestContext.Current.CancellationToken);
            await sink.CompleteAsync(TestContext.Current.CancellationToken);

            byte[] bytes = await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                [-2, -2, 0, 0, 2, 2],
                Enumerable.Range(0, input.Length)
                    .Select(
                        index => BinaryPrimitives.ReadInt16LittleEndian(
                            bytes.AsSpan(44 + index * 2, 2))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<float[]> RenderFirstOperatorBlockAsync(
        double monitorLevelDb,
        bool qskEnabled = false)
    {
        var sink = new CapturingAudioSink();
        await using MorseRunnerEngine engine = new(_ => sink);
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 12_345) with
            {
                MonitorLevelDb = monitorLevelDb,
                Qsk = qskEnabled,
            },
            TestContext.Current.CancellationToken);
        await StartAndAdvanceAsync(engine, handle.SessionId);
        return sink.Blocks.Single();
    }

    private static async Task<float[]> RenderFirstReceiverBlockAsync(
        double monitorLevelDb)
    {
        var sink = new CapturingAudioSink();
        await using MorseRunnerEngine engine = new(_ => sink);
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 12_345) with
            {
                MonitorLevelDb = monitorLevelDb,
                Qrm = true,
            },
            TestContext.Current.CancellationToken);
        Assert.True(
            (await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    TestClient),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await engine.ExecuteAsync(
                new SendOperatorIntentCommand(
                    RequestId.New(),
                    handle.SessionId,
                    TestClient,
                    OperatorIntent.Abort,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await engine.ExecuteAsync(
                new AdvanceSimulationCommand(
                    RequestId.New(),
                    handle.SessionId,
                    TestClient,
                    BlockCount: 1),
                TestContext.Current.CancellationToken)).Accepted);
        return sink.Blocks.Single();
    }

    private static async Task StartAndAdvanceAsync(
        MorseRunnerEngine engine,
        SessionId sessionId)
    {
        Assert.True(
            (await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    sessionId,
                    TestClient),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await engine.ExecuteAsync(
                new SendOperatorIntentCommand(
                    RequestId.New(),
                    sessionId,
                    TestClient,
                    OperatorIntent.Cq,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await engine.ExecuteAsync(
                new AdvanceSimulationCommand(
                    RequestId.New(),
                    sessionId,
                    TestClient,
                    BlockCount: 1),
                TestContext.Current.CancellationToken)).Accepted);
    }

    private static double RootMeanSquare(ReadOnlySpan<float> samples)
    {
        double sum = 0d;
        for (int index = 0; index < samples.Length; index++)
        {
            sum += samples[index] * samples[index];
        }

        return Math.Sqrt(sum / samples.Length);
    }

    private static string ComputeRawSingleSha256(float[] samples) =>
        Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes(samples.AsSpan())));

    private sealed class CapturingAudioSink : IAudioSink
    {
        public List<float[]> Blocks { get; } = [];

        public ValueTask InitializeAsync(
            SessionId sessionId,
            AudioStreamFormat format,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask WriteAsync(
            ReadOnlyMemory<float> samples,
            long simulationBlock,
            CancellationToken cancellationToken)
        {
            Blocks.Add(samples.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
