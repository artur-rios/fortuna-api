using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.WebApi.Services;

public sealed class DatabaseInitializationHostedService(
    IServiceScopeFactory scopeFactory,
    Configuration.FortunaOptions options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (options.RunMigrations)
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
