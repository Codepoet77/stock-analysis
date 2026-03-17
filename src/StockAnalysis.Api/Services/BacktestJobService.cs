using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockAnalysis.Api.Data;
using StockAnalysis.Api.Models.Backtest;

namespace StockAnalysis.Api.Services;

public class BacktestJobService(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task<BacktestJob?> GetRunningJobAsync(string userId)
        => await db.BacktestJobs
            .Where(j => j.UserId == userId && j.Status == BacktestJobStatus.Running)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task<BacktestJob?> GetLatestJobAsync(string userId)
        => await db.BacktestJobs
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task<List<BacktestJob>> GetHistoryAsync(string userId, int limit = 20)
        => await db.BacktestJobs
            .Where(j => j.UserId == userId && j.Status == BacktestJobStatus.Completed)
            .OrderByDescending(j => j.CompletedAt)
            .Take(limit)
            .ToListAsync();

    public async Task<BacktestJob?> GetJobAsync(string userId, Guid jobId)
        => await db.BacktestJobs
            .Where(j => j.UserId == userId && j.Id == jobId)
            .FirstOrDefaultAsync();

    public async Task<BacktestJob> CreateJobAsync(string userId, string userEmail, BacktestParameters parameters)
    {
        var job = new BacktestJob
        {
            UserId = userId,
            UserEmail = userEmail,
            Status = BacktestJobStatus.Running,
            ParametersJson = JsonSerializer.Serialize(parameters, JsonOpts),
        };
        db.BacktestJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    public async Task UpdateProgressAsync(Guid jobId, BacktestProgressUpdate progress)
    {
        await db.BacktestJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                j => j.ProgressJson, JsonSerializer.Serialize(progress, JsonOpts)));
    }

    public async Task CompleteJobAsync(Guid jobId, BacktestResult result)
    {
        await db.BacktestJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BacktestJobStatus.Completed)
                .SetProperty(j => j.CompletedAt, DateTime.UtcNow)
                .SetProperty(j => j.ResultJson, JsonSerializer.Serialize(result, JsonOpts))
                .SetProperty(j => j.ProgressJson, (string?)null));
    }

    public async Task CancelJobAsync(Guid jobId)
    {
        await db.BacktestJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BacktestJobStatus.Cancelled)
                .SetProperty(j => j.CompletedAt, DateTime.UtcNow)
                .SetProperty(j => j.CancelRequested, true));
    }

    public async Task FailJobAsync(Guid jobId, string error)
    {
        await db.BacktestJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BacktestJobStatus.Failed)
                .SetProperty(j => j.CompletedAt, DateTime.UtcNow)
                .SetProperty(j => j.ErrorMessage, error));
    }

    /// <summary>Mark any jobs still "Running" as Failed on startup (server was restarted mid-run).</summary>
    public async Task RecoverStaleJobsAsync()
    {
        await db.BacktestJobs
            .Where(j => j.Status == BacktestJobStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BacktestJobStatus.Failed)
                .SetProperty(j => j.ErrorMessage, "Server restarted during backtest")
                .SetProperty(j => j.CompletedAt, DateTime.UtcNow));
    }

    public BacktestProgressUpdate? DeserializeProgress(string? json)
        => json is null ? null : JsonSerializer.Deserialize<BacktestProgressUpdate>(json, JsonOpts);

    public BacktestResult? DeserializeResult(string? json)
    {
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<BacktestResult>(json, JsonOpts); }
        catch { return null; }
    }

    public BacktestParameters? DeserializeParameters(string? json)
        => json is null ? null : JsonSerializer.Deserialize<BacktestParameters>(json, JsonOpts);
}
