using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using AiPet.Todos;

namespace AiPet.ToolWindow;

public sealed record RecurrenceOption(RecurrenceKind Kind, string DisplayName);
public sealed class WeekdayChoice : INotifyPropertyChanged
{
    public DayOfWeek Day { get; init; }
    public string Label => "日一二三四五六"[(int)Day].ToString();
    private bool _selected;
    public bool IsSelected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class TodoViewModel
{
    public IReadOnlyList<RecurrenceOption> RecurrenceOptions { get; } = new[]
    {
        new RecurrenceOption(RecurrenceKind.None,"一次性"), new RecurrenceOption(RecurrenceKind.Daily,"每天"),
        new RecurrenceOption(RecurrenceKind.Weekly,"每周"), new RecurrenceOption(RecurrenceKind.Weekdays,"工作日"),
        new RecurrenceOption(RecurrenceKind.CustomDays,"自选星期"),
    };
    public IReadOnlyList<WeekdayChoice> Weekdays { get; } = new[] { 1, 2, 3, 4, 5, 6, 0 }.Select(day => new WeekdayChoice { Day = (DayOfWeek)day }).ToArray();
    public IReadOnlyList<string> TimeZones { get; } = TimeZoneInfo.GetSystemTimeZones().Select(zone => zone.Id).ToArray();
    private RecurrenceOption? _editorRecurrence;
    public RecurrenceOption? EditorRecurrence { get => _editorRecurrence ?? RecurrenceOptions[0]; set { _editorRecurrence = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditorRecurrencePreview)); } }
    private int _editorInterval = 1;
    public int EditorInterval { get => _editorInterval; set { _editorInterval = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditorRecurrencePreview)); } }
    private DateTime? _editorEndsOn;
    public DateTime? EditorEndsOn { get => _editorEndsOn; set { _editorEndsOn = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditorRecurrencePreview)); } }
    private string _editorTimeZoneId = TimeZoneInfo.Local.Id;
    public string EditorTimeZoneId { get => _editorTimeZoneId; set { _editorTimeZoneId = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditorRecurrencePreview)); } }
    private string _editorAdditionalTimes = string.Empty;
    public string EditorAdditionalTimes { get => _editorAdditionalTimes; set { _editorAdditionalTimes = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditorRecurrencePreview)); } }
    public bool EditorOnlyThis { get; set; }
    public ICommand SkipOccurrenceCommand => new RelayCommand(parameter =>
    {
        var item = Find(parameter);
        if (_store is null || item is null) return;
        try { _store.SkipOccurrence(item.Id); Status = "已跳过此次，后续规则保留。"; Reload(); }
        catch (TodoValidationException ex) { Status = ex.Message; }
    });

    private void LoadRuleEditor(TodoItem? item)
    {
        var rule = item?.Recurrence ?? new();
        EditorRecurrence = RecurrenceOptions.First(option => option.Kind == rule.Kind);
        EditorInterval = rule.Interval;
        EditorTimeZoneId = string.IsNullOrEmpty(rule.TimeZoneId) ? TimeZoneInfo.Local.Id : rule.TimeZoneId;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(EditorTimeZoneId);
        EditorEndsOn = rule.EndsAt is { } end ? TimeZoneInfo.ConvertTime(end, zone).Date : null;
        if (item?.ReminderAt is { } first)
        {
            var local = TimeZoneInfo.ConvertTime(first, zone);
            EditorReminderDate = local.Date;
            EditorReminderTime = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        foreach (var day in Weekdays)
        {
            day.PropertyChanged -= WeekdayChanged;
            day.IsSelected = rule.DaysOfWeek.Contains(day.Day);
            day.PropertyChanged += WeekdayChanged;
        }
        EditorAdditionalTimes = string.Join(Environment.NewLine, (item?.AdditionalReminderTimes ?? Array.Empty<DateTimeOffset>()).Select(time => TimeZoneInfo.ConvertTime(time, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        EditorOnlyThis = false;
        OnPropertyChanged(nameof(EditorOnlyThis));
    }
    private void WeekdayChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(EditorRecurrencePreview));
    private RecurrenceRule ReadEditorRule()
    {
        var rule = new RecurrenceRule { Kind = EditorRecurrence?.Kind ?? RecurrenceKind.None, Interval = EditorInterval,
            DaysOfWeek = Weekdays.Where(day => day.IsSelected).Select(day => day.Day).ToArray(), TimeZoneId = EditorTimeZoneId,
            EndsAt = EditorEndsOn is null ? null : CombineReminder(EditorEndsOn, "23:59") };
        RecurrenceCalculator.Validate(rule);
        return rule;
    }
    private DateTimeOffset? CombineReminder(DateTime? date, string time)
    {
        if (date is null) return null;
        if (!TimeSpan.TryParseExact(time, "hh\\:mm", CultureInfo.InvariantCulture, out var clock) || clock.TotalHours >= 24)
            throw new TodoValidationException("提醒时间请使用 HH:mm。");
        try { return RecurrenceCalculator.ResolveLocal(date.Value.Date + clock, TimeZoneInfo.FindSystemTimeZoneById(EditorTimeZoneId)); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new TodoValidationException("请选择有效时区。"); }
    }
    private DateTimeOffset[] ReadAdditionalTimes()
    {
        var lines = EditorAdditionalTimes.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 32) throw new TodoValidationException("最多设置 32 个额外提醒时间。");
        return lines.Select(line =>
        {
            if (!DateTime.TryParseExact(line, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new TodoValidationException("额外时间每行使用 yyyy-MM-dd HH:mm。");
            return CombineReminder(date.Date, date.ToString("HH:mm", CultureInfo.InvariantCulture))!.Value;
        }).Distinct().OrderBy(time => time).ToArray();
    }
    public string EditorRecurrencePreview
    {
        get
        {
            try
            {
                var rule = ReadEditorRule();
                var first = CombineReminder(EditorReminderDate, EditorReminderTime);
                if (first is null) return "设置首次提醒后可预览。";
                var times = ReadAdditionalTimes().Append(first.Value).ToList();
                var cursor = first.Value;
                for (var i = 0; i < 3; i++) { var next = RecurrenceCalculator.Next(rule, first.Value, cursor); if (next is null) break; times.Add(next.Value); cursor = next.Value; }
                return RecurrenceCalculator.Describe(rule) + " · " + EditorTimeZoneId + "\n" + string.Join(" → ", times.Distinct().OrderBy(time => time).Take(4).Select(time => TimeZoneInfo.ConvertTime(time, TimeZoneInfo.FindSystemTimeZoneById(EditorTimeZoneId)).ToString("MM-dd HH:mm")));
            }
            catch (TodoValidationException ex) { return ex.Message; }
        }
    }
}
