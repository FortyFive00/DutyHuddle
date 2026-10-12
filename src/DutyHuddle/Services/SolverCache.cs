using Dalamud.Plugin.Services;
using DutyHuddle.Domain.Solver;

namespace DutyHuddle.Services;

/// <summary>Builds one <see cref="StickerSolver"/> per goal in the background and keeps it.</summary>
public sealed class SolverCache
{
    private readonly IPluginLog log;
    private readonly Dictionary<AdvisorGoal, Task<StickerSolver>> solvers = [];
    private readonly Lock gate = new();

    public SolverCache(IPluginLog log)
    {
        this.log = log;
    }

    /// <summary>Returns the solver if ready; otherwise starts computing it and returns null.</summary>
    public StickerSolver? TryGet(AdvisorGoal goal)
    {
        Task<StickerSolver> task;
        lock (gate)
        {
            if (!solvers.TryGetValue(goal, out task!))
            {
                task = Task.Run(() => new StickerSolver(goal));
                task.ContinueWith(t => log.Error(t.Exception!, "Sticker solver failed."), TaskContinuationOptions.OnlyOnFaulted);
                solvers[goal] = task;
            }
        }

        return task.IsCompletedSuccessfully ? task.Result : null;
    }
}
