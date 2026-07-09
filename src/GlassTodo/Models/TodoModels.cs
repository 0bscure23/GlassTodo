namespace GlassTodo.Models;

public enum Priority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public class TodoTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListId { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public bool IsDone { get; set; }
    public Priority Priority { get; set; }
    /// <summary>Due date (date component only, midnight local).</summary>
    public DateTime? DueAt { get; set; }
    /// <summary>Exact reminder moment.</summary>
    public DateTime? RemindAt { get; set; }
    /// <summary>Set once a reminder toast has fired, so it never refires.</summary>
    public DateTime? ReminderFiredAt { get; set; }
    public long SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
}

public class TodoList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ColorAccent { get; set; } = "#5B9DFF";
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
}

public class AppData
{
    public int SchemaVersion { get; set; } = 1;
    public List<TodoList> Lists { get; set; } = new();
    public List<TodoTask> Tasks { get; set; } = new();
}
