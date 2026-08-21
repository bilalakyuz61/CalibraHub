namespace CalibraHub.Web.Models.Admin;

public sealed class ScheduledTasksSmartBoardViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class ScheduledTaskEditViewModel
{
    public int?    Id                  { get; init; }
    public string  Name                { get; init; } = "";
    public string? Description         { get; init; }
    public int     TaskType            { get; init; } = 1;
    public string? ParametersJson      { get; init; }
    public int     ScheduleType        { get; init; } = 4;
    public string? ScheduleExpression  { get; init; }
    public string? ScheduleDescription { get; init; }
    public bool    IsEnabled           { get; init; } = true;
    public int?    PrerequisiteTaskId  { get; init; }
    public bool    IsBuiltin           { get; init; }
    public IReadOnlyList<(int Id, string Name)> AllTasks { get; init; } = [];

    /// <summary>
    /// DbImport sihirbazından (<c>?lockContext=...&amp;taskType=8&amp;jobId=N</c>) açıldığında
    /// true olur — görev türü ve aktarım işi seçici sunucu tarafında kilitli render edilir.
    /// Yalnızca <see cref="TaskType"/> DataImport ve ilgili iş DB'de gerçekten bulunuyorsa set edilir
    /// (fail-open: iş bulunamazsa kilit uygulanmaz, form normal davranır).
    /// </summary>
    public bool    IsLockedContext     { get; init; }
    public int?    LockedJobId         { get; init; }
    public string? LockedJobName       { get; init; }
}
