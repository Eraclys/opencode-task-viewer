namespace TaskViewer.Domain.Orchestration;

public interface ITaskReconcilerService
{
    Task ReconcileAsync(int leaseSeconds);
}
