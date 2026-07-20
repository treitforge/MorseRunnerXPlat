using System.ComponentModel;
using System.Diagnostics;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record LegacyRepositoryInspection(
    string? Revision,
    string? Tree,
    bool Clean,
    string? Failure);

internal interface ILegacyRepositoryInspector
{
    Task<LegacyRepositoryInspection> InspectAsync(
        string legacyRoot,
        CancellationToken cancellationToken);
}

internal sealed class GitLegacyRepositoryInspector
    : ILegacyRepositoryInspector
{
    public async Task<LegacyRepositoryInspection> InspectAsync(
        string legacyRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            GitResult revision = await RunGitAsync(
                legacyRoot,
                ["rev-parse", "--verify", "HEAD^{commit}"],
                cancellationToken);
            if (revision.ExitCode != 0)
            {
                return Failure(revision.StandardError);
            }

            GitResult tree = await RunGitAsync(
                legacyRoot,
                ["rev-parse", "--verify", "HEAD^{tree}"],
                cancellationToken);
            if (tree.ExitCode != 0)
            {
                return Failure(tree.StandardError);
            }

            GitResult status = await RunGitAsync(
                legacyRoot,
                ["status", "--porcelain=v2", "--untracked-files=all"],
                cancellationToken);
            if (status.ExitCode != 0)
            {
                return Failure(status.StandardError);
            }

            return new LegacyRepositoryInspection(
                revision.StandardOutput.Trim(),
                tree.StandardOutput.Trim(),
                String.IsNullOrWhiteSpace(status.StandardOutput),
                null);
        }
        catch (Win32Exception exception)
        {
            return Failure(exception.Message);
        }
        catch (IOException exception)
        {
            return Failure(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(exception.Message);
        }
    }

    private static async Task<GitResult> RunGitAsync(
        string legacyRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new("git")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(legacyRoot);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        Task<string> outputTask =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask =
            process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new GitResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static LegacyRepositoryInspection Failure(string failure)
    {
        return new LegacyRepositoryInspection(
            null,
            null,
            Clean: false,
            failure.Trim());
    }

    private sealed record GitResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
