namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface ITaskReconcilerService
{
    Task ReconcileAsync(int leaseSeconds);
}
