using Eve.Agent.Data.Repositories;
using Eve.Agent.Services;
using Mediahost.Agents.Http;
using Mediahost.Agents.Services;

namespace Eve.Agent.Controllers;

public static class EveEndpoints
{
    public static IEndpointRouteBuilder MapEveEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/eve").WithTags("Eve");

        // POST /api/eve/chat
        group.MapPost("/chat", async (
            ChatRequest req,
            IAgentService agent,
            CancellationToken ct) =>
        {
            var result = await agent.HandleMessageAsync(req.Message, req.SessionId, ct);
            return Results.Ok(new ChatResponse(result.Response, result.SessionId, result.ToolCallCount));
        })
        .WithName("EveChat")
        .WithSummary("Send a message to Eve");

        // GET /api/eve/briefing/today
        group.MapGet("/briefing/today", async (
            MorningBriefingGeneratorService generator) =>
        {
            var today    = DateOnly.FromDateTime(DateTime.Today);
            var briefing = await generator.GenerateBriefingAsync(today);
            return Results.Ok(briefing);
        })
        .WithName("EveBriefing")
        .WithSummary("Get today's morning briefing markdown");

        // GET /api/eve/reminders?filter=today&person=name
        group.MapGet("/reminders", async (
            ReminderRepository repo,
            string? filter,
            string? person) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            var list = filter switch
            {
                "today"    => await repo.GetDueTodayAsync(today),
                "tomorrow" => await repo.GetDueTomorrowAsync(today.AddDays(1)),
                "week"     => await repo.GetUpcomingAsync(7),
                _          => await repo.GetAllActiveAsync()
            };

            if (!string.IsNullOrEmpty(person))
                list = await repo.GetByPersonAsync(person);

            return Results.Ok(list);
        })
        .WithName("EveReminders")
        .WithSummary("List reminders with optional filter");

        // GET /api/eve/status
        group.MapGet("/status", async (
            ReminderRepository repo) =>
        {
            var reminderCount = await repo.GetActiveCountAsync();
            var overdueCount  = await repo.GetOverdueCountAsync();

            return Results.Ok(new
            {
                agentName     = "Eve",
                reminderCount,
                overdueCount
            });
        })
        .WithName("EveStatus")
        .WithSummary("Get Eve's status and reminder summary");

        return app;
    }
}

