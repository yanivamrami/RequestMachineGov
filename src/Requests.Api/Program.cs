using System.Diagnostics;
using Requests.Application.Requests;
using Requests.Infrastructure;
using Requests.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure();

// [NEW] Application services are registered here, in the composition root (moved out of Infrastructure).
builder.Services.AddScoped<IRequestService, RequestService>();

// [NEW] Every error response (400/401/404/500) uses the standard RFC 7807 ProblemDetails JSON shape,
// with a traceId that matches the server log entry.
// CustomizeProblemDetails: the exception-handler 500 doesn't include a traceId by default (verified), so add it to every response.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));

var app = builder.Build();

// [NEW] Global error handling, by environment:
//  - Production (and any non-Development environment, including when ASPNETCORE_ENVIRONMENT is unset; fails closed):
//    an unhandled exception returns a generic 500 ProblemDetails { type, title, status, traceId }.
//    No exception message, stack trace, SQL, or connection details reach the client.
//    The exception handler middleware logs the full exception server-side, so traceId links the response to the log.
//  - Development: ASP.NET Core enables the Developer Exception Page automatically, so the full exception and stack
//    trace are returned (as ProblemDetails JSON for API clients) to speed up debugging.
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RequestsDbContext>();
    DbSeeder.Seed(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

public partial class Program { }
