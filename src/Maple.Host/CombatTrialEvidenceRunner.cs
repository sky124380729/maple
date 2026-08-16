using System.Text.Json;
using Maple.Contracts;
using Maple.Runtime;

namespace Maple.Host;

public sealed class CombatTrialEvidenceRunner : IDisposable
{
    private readonly MainWindow window;
    private readonly SamePlatformCombatTrialController controller;
    private readonly LiveObservationSource observations;
    private readonly string outputPath;
    private readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(90));
    private bool disposed;

    public CombatTrialEvidenceRunner(
        MainWindow window,
        SamePlatformCombatTrialController controller,
        LiveObservationSource observations,
        string outputPath)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));
        this.outputPath = Path.GetFullPath(string.IsNullOrWhiteSpace(outputPath)
            ? throw new ArgumentException("Evidence output path is required", nameof(outputPath))
            : outputPath);
        window.Shown += OnShown;
    }

    public int ExitCode { get; private set; } = 2;

    private async void OnShown(object? sender, EventArgs e)
    {
        try
        {
            await WaitForOperatorForegroundAsync(cancellation.Token).ConfigureAwait(true);
            long startFrameId = observations.Latest?.Snapshot.FrameId ?? -1;
            var recorder = new CombatTrialEvidenceRecorder(startFrameId);
            var completed = new TaskCompletionSource<CombatTrialCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnAction(AbstractAction action) => recorder.Record(action);
            void OnCompleted(CombatTrialCompletion result) => completed.TrySetResult(result);
            controller.ActionAccepted += OnAction;
            controller.Completed += OnCompleted;
            try
            {
                AutomaticCombatArmResult armed = await controller.StartAsync(cancellation.Token).ConfigureAwait(true);
                if (!armed.Success)
                {
                    WriteFailure(armed.Code, startFrameId, armed.PauseReason);
                    return;
                }
                CombatTrialCompletion result = await completed.Task.WaitAsync(cancellation.Token).ConfigureAwait(true);
                CombatTrialEvidenceReport report = recorder.Complete(result);
                Write(report);
                ExitCode = report.Success ? 0 : 2;
            }
            finally
            {
                controller.ActionAccepted -= OnAction;
                controller.Completed -= OnCompleted;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WriteFailure("EVIDENCE_RUNNER_" + exception.GetType().Name, observations.Latest?.Snapshot.FrameId ?? -1, PauseReason.SafetyViolation);
        }
        finally
        {
            try { await controller.PauseAsync(PauseReason.OperatorRequested).ConfigureAwait(true); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { ExitCode = 2; }
            window.Close();
        }
    }

    private async Task WaitForOperatorForegroundAsync(CancellationToken cancellationToken)
    {
        while (!window.ContainsFocus)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken).ConfigureAwait(true);
        }
    }

    private void WriteFailure(string code, long frameId, PauseReason reason)
    {
        Write(new CombatTrialEvidenceReport(
            1, false, code, frameId, frameId, 0, 0, 0, false, true,
            reason.ToString(), [], DateTimeOffset.UtcNow));
        ExitCode = 2;
    }

    private void Write(CombatTrialEvidenceReport report)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.Shown -= OnShown;
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
