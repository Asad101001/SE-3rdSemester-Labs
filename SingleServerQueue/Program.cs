using SingleServerQueue;
using SingleServerQueue.Engine;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/simulate", (SimRequest req) =>
{
    try
    {
        if (req.MeanArrival <= 0) return Results.BadRequest("Mean arrival time must be > 0");
        if (req.MeanService <= 0) return Results.BadRequest("Mean service time must be > 0");
        if (req.SimTime     < 10) return Results.BadRequest("Simulation time must be >= 10");
        return Results.Ok(QueueingEngine.Compute(req));
    }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex)         { return Results.Problem(ex.Message); }
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.Run();
