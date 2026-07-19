using MorseRunner.Audio;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

public sealed class InProcessClientTests
{
    [Fact]
    public async Task ClientRunsTheSameSeededSessionThroughTheSemanticFacade()
    {
        MorseRunnerEngine engine = new(_ => new NullAudioSink());
        await using InProcessMorseRunnerClient client = new(engine);
        EngineInfo info = await client.GetEngineInfoAsync(
            TestContext.Current.CancellationToken);
        SessionHandle handle = await client.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 42),
            TestContext.Current.CancellationToken);
        ClientId clientId = new("client-tests");
        await client.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                clientId),
            TestContext.Current.CancellationToken);
        await client.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                clientId,
                BlockCount: 8),
            TestContext.Current.CancellationToken);
        SessionSnapshot snapshot = await client.GetSnapshotAsync(
            handle.SessionId,
            TestContext.Current.CancellationToken);

        Assert.True(info.IsInProcess);
        Assert.Contains("deterministic-scenarios", info.Capabilities);
        Assert.Equal(8, snapshot.SimulationBlock);
        Assert.NotNull(snapshot.LastCaller);
    }

    [Fact]
    public async Task BufferedWavClientCreatesACompleteArchiveProbeRecording()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-{Guid.NewGuid():N}.wav");
        try
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            await using InProcessMorseRunnerClient client =
                InProcessMorseRunnerClient.CreateWithBufferedWavAudio(path);
            SessionHandle handle = await client.CreateSessionAsync(
                SessionSettings.CreateDefault(seed: 12345),
                cancellationToken);
            ClientId clientId = new("recording-probe-test");
            Assert.True(
                (await client.ExecuteAsync(
                    new StartSessionCommand(
                        RequestId.New(),
                        handle.SessionId,
                        clientId),
                    cancellationToken)).Accepted);
            Assert.True(
                (await client.ExecuteAsync(
                    new AdvanceSimulationCommand(
                        RequestId.New(),
                        handle.SessionId,
                        clientId,
                        BlockCount: 8),
                    cancellationToken)).Accepted);
            Assert.True(
                (await client.ExecuteAsync(
                    new StopSessionCommand(
                        RequestId.New(),
                        handle.SessionId,
                        clientId),
                    cancellationToken)).Accepted);
            await client.CloseSessionAsync(handle.SessionId, cancellationToken);

            byte[] content = await File.ReadAllBytesAsync(
                path,
                cancellationToken);
            Assert.Equal("RIFF"u8.ToArray(), content[..4]);
            Assert.Equal("WAVE"u8.ToArray(), content[8..12]);
            Assert.True(content.Length > 44);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
