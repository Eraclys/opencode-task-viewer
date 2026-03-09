namespace TaskViewer.Application.Orchestration;

public interface ITaskReconcilerService
{
    Task ReconcileAsync(int leaseSeconds);
}
