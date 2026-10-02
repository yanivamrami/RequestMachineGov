using System.Text.Json.Serialization;
using System.Diagnostics;
using Requests.Application.Requests;
using Requests.Infrastructure;
using Requests.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Enums as names ("status": "InProgress"), matching the query-string values and the frontend types.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure();

// Application services are registered in the composition root; Infrastructure only registers its own types.
builder.Services.AddScoped<IRequestService, RequestService>();

// Every error response uses RFC 7807 ProblemDetails with a traceId that matches the server log entry
// (the exception-handler 500 has no traceId by default, so it is added here).
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));

var app = builder.Build();

// Unhandled exceptions:
//  - Any non-Development environment (fails closed if unset): generic 500 { type, title, status, traceId }.
//    No message, stack trace or SQL reaches the client; the full exception is logged server-side under that traceId.
//  - Development: the built-in Developer Exception Page returns the full exception for debugging.
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

// Gives empty error responses (e.g. 404 for an unknown route) a ProblemDetails body too.
app.UseStatusCodePages();

app.MapControllers();

app.Run();

public partial class Program { }
