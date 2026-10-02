using Microsoft.EntityFrameworkCore;
using Requests.Domain.Entities;

namespace Requests.Infrastructure.Persistence;

public class RequestsDbContext : DbContext
{
    public RequestsDbContext(DbContextOptions<RequestsDbContext> options) : base(options)
    {
    }

    public DbSet<Request> Requests => Set<Request>();

    // Indexes match the real query shapes: equality columns, then the sort key, then Id as tie-breaker.
    // The in-memory provider ignores them; on a SQL provider they become a migration.
    // No single-column Status/Type indexes (4 values each, poor selectivity) and no index per sortable column
    // (every index slows writes).
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var request = modelBuilder.Entity<Request>();

        // Bounded length so the column is indexable (nvarchar(max) can't be an index key on SQL Server); unique = business key.
        request.Property(x => x.RequestNumber).HasMaxLength(20);
        request.HasIndex(x => x.RequestNumber).IsUnique();

        // Regular user: WHERE OwnerId = @u OR AssignedToUserId = @u ORDER BY CreatedAt DESC.
        // One index per side of the OR lets the DB seek both and merge, instead of scanning the table.
        request.HasIndex(x => new { x.OwnerId, x.CreatedAt, x.Id });
        request.HasIndex(x => new { x.AssignedToUserId, x.CreatedAt, x.Id });

        // Admin listing (no permission filter), created-date range, and keyset paging on CreatedAt.
        request.HasIndex(x => new { x.CreatedAt, x.Id });

        // Status filter combined with the default sort.
        request.HasIndex(x => new { x.Status, x.CreatedAt, x.Id });
    }
}
