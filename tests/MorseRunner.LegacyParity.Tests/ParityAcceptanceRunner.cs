namespace MorseRunner.LegacyParity.Tests;

internal sealed record SelectedParityObservation(
    ParityTargetKind Target,
    ParityObservation Observation);

internal static class ParityAcceptanceRunner
{
    public static async Task<SelectedParityObservation> ExecuteSelectedAsync(
        ParityCertificationCase definition,
        CancellationToken cancellationToken)
    {
        ParityCertificationCase currentDefinition =
            ParityCertificationCase.Load(definition.Id);
        if (!definition.HasSameExecutionDefinition(
                currentDefinition))
        {
            throw new InvalidDataException(
                $"Parity case '{definition.Id}' changed before execution.");
        }

        definition = currentDefinition;
        ParityAcceptanceRegistration registration =
            ParityAcceptanceRegistry.Get(definition.Id);
        registration.ValidateManifestBinding(definition);
        ParityTargetKind targetKind =
            ParityTargetSelection.Current;
        ParityObservation observation;
        bool adapterCompleted;
        try
        {
            IParityTarget target =
                registration.CreateTarget(targetKind)();
            observation = await target.ExecuteAsync(
                definition.Scenario,
                cancellationToken);
            adapterCompleted = true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            observation = new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                "parity-adapter-exception",
                $"{registration.AdapterId(targetKind)}:"
                + exception.GetType().FullName);
            adapterCompleted = false;
        }

        await ParityRunRecorder.RecordAsync(
            definition,
            targetKind,
            observation,
            adapterCompleted,
            cancellationToken);

        return new SelectedParityObservation(
            targetKind,
            observation);
    }
}

internal static class ParityRegressionRunner
{
    public static Task<SelectedParityObservation> ExecuteSelectedAsync(
        ParityScenario scenario,
        Func<IParityTarget> createLegacy,
        Func<IParityTarget> createXPlat,
        CancellationToken cancellationToken)
    {
        return ParitySelectedTargetRunner.ExecuteAsync(
            scenario,
            createLegacy,
            createXPlat,
            cancellationToken);
    }
}

internal static class ParitySelectedTargetRunner
{
    public static async Task<SelectedParityObservation> ExecuteAsync(
        ParityScenario scenario,
        Func<IParityTarget> createLegacy,
        Func<IParityTarget> createXPlat,
        CancellationToken cancellationToken)
    {
        ParityTargetKind selectedTarget = ParityTargetSelection.Current;
        IParityTarget target = ParityTargetFactory.Create(
            selectedTarget,
            createLegacy,
            createXPlat);
        ParityObservation observation = await target.ExecuteAsync(
            scenario,
            cancellationToken);

        return new SelectedParityObservation(selectedTarget, observation);
    }
}
