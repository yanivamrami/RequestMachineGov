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
        services.AddDbContext<RequestsDbContext>(options =>
            options.UseInMemoryDatabase("CandidateRequests"));

        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<IRequestService, RequestService>();

        return services;
    }
}
