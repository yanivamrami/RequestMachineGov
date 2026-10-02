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

        // Data access only; Application services are registered in Program.cs.
        services.AddScoped<IRequestRepository, RequestRepository>();

        return services;
    }
}
