namespace TaskViewer.Server.Application.Orchestration;

interface ITaskReconcilerService
{
    Task ReconcileAsync(int leaseSeconds);
}
