using System.Buffers.Binary;
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
    public async Task FullMonitorProducesLegacyCalibratedLocalSidetone()
    {
        float[] samples = await RenderFirstOperatorBlockAsync(
            monitorLevelDb: 0d);

        Assert.True(
            samples.Max(MathF.Abs) > 0.95f,
            $"Local sidetone peak was {samples.Max(MathF.Abs):F6}.");
    }

    [Fact]
    public async Task MonitorLevelAttenuatesOnlyLocalSidetone()
    {
        float[] full = await RenderFirstOperatorBlockAsync(
            monitorLevelDb: 0d);
        float[] reduced = await RenderFirstOperatorBlockAsync(
            monitorLevelDb: -20d);

        double ratio = RootMeanSquare(reduced) / RootMeanSquare(full);
        Assert.InRange(ratio, 0.099d, 0.101d);
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
                [Int16.MinValue, -16384, 16384, Int16.MaxValue],
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

    private static async Task<float[]> RenderFirstOperatorBlockAsync(
        double monitorLevelDb)
    {
        var sink = new CapturingAudioSink();
        await using MorseRunnerEngine engine = new(_ => sink);
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 12_345) with
            {
                MonitorLevelDb = monitorLevelDb,
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
