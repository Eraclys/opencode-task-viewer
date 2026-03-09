using Microsoft.Extensions.DependencyInjection;
using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Persistence.DependencyInjection;

public static class TaskViewerPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddTaskViewerPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, (string DbPath, Action OnChange)> optionsFactory)
    {
        services.AddSingleton<IOrchestrationPersistence>(
            sp =>
            {
                var options = optionsFactory(sp);
                return new SqliteOrchestrationPersistence(options.DbPath, options.OnChange);
            });

        return services;
    }
}
