using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Requests.Application.Requests;
using Requests.Infrastructure.Persistence;
using Requests.Infrastructure.Repositories;

namespace Requests.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        // ponytail: in-memory provider (exercise setup). Swapping to SQL Server/Postgres changes only this line;
        // the indexes declared in RequestsDbContext then become a real migration.
        services.AddDbContext<RequestsDbContext>(options =>
            options.UseInMemoryDatabase("CandidateRequests"));

        services.AddScoped<IRequestRepository, RequestRepository>();

        // [OLD] Replaced: Infrastructure registered an Application service, so the data layer was wiring up business logic.
        // services.AddScoped<IRequestService, RequestService>();
        // [NEW] RequestService registration moved to Program.cs (the composition root); Infrastructure registers only data access.

        return services;
    }
}
