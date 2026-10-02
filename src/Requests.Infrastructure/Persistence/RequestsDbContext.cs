using Microsoft.EntityFrameworkCore;
using Requests.Domain.Entities;

namespace Requests.Infrastructure.Persistence;

public class RequestsDbContext : DbContext
{
    public RequestsDbContext(DbContextOptions<RequestsDbContext> options) : base(options)
    {
    }

    public DbSet<Request> Requests => Set<Request>();
}
