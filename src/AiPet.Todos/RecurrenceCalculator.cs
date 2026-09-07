namespace AiPet.Todos;

/// <summary>Calendar recurrence at a fixed wall-clock time in the saved zone.
/// Legacy rules without a zone retain their original fixed offset.</summary>
public static class RecurrenceCalculator
{
    public static void Validate(RecurrenceRule rule)
    {
        if (!Enum.IsDefined(rule.Kind) || rule.Interval is < 1 or > 365)
            throw new TodoValidationException("重复规则或间隔无效（1–365）。");
        if (rule.DaysOfWeek is null || rule.DaysOfWeek.Any(day => !Enum.IsDefined(day)) ||
            (rule.Kind == RecurrenceKind.CustomDays && rule.DaysOfWeek.Count == 0))
            throw new TodoValidationException("请选择有效的重复星期。");
        if (!string.IsNullOrWhiteSpace(rule.TimeZoneId))
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            { throw new TodoValidationException("重复规则的时区无效。"); }
        }
    }

    public static DateTimeOffset? Next(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset after)
    {
        Validate(rule);
        if (rule.Kind == RecurrenceKind.None) return null;
        var zone = string.IsNullOrWhiteSpace(rule.TimeZoneId) ? null : TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId);
        var first = zone is null ? anchor.DateTime : TimeZoneInfo.ConvertTime(anchor, zone).DateTime;
        var current = zone is null ? after.ToOffset(anchor.Offset).DateTime : TimeZoneInfo.ConvertTime(after, zone).DateTime;
        var date = current.Date > first.Date ? current.Date : first.Date;
        // At most 365 weeks plus a week of selected days. Never replay missed occurrences.
        for (var i = 0; i < 2563; i++)
        {
            var days = (date - first.Date).Days;
            var weekday = (int)date.DayOfWeek;
            var anchorWeekday = ((int)first.DayOfWeek + 6) % 7;
            var week = (days + anchorWeekday) / 7;
            var matches = days >= 0 && (rule.Kind switch
            {
                RecurrenceKind.Daily => days % rule.Interval == 0,
                RecurrenceKind.Weekly => days % (7 * rule.Interval) == 0,
                RecurrenceKind.Weekdays => weekday is >= 1 and <= 5 && week % rule.Interval == 0,
                RecurrenceKind.CustomDays => rule.DaysOfWeek.Contains(date.DayOfWeek) && week % rule.Interval == 0,
                _ => false,
            });
            if (matches)
            {
                var local = DateTime.SpecifyKind(date + first.TimeOfDay, DateTimeKind.Unspecified);
                var candidate = zone is null ? new DateTimeOffset(local, anchor.Offset) : ResolveLocal(local, zone);
                if (rule.EndsAt is { } end && candidate > end) return null;
                if (candidate > after && candidate >= anchor) return candidate;
            }
            if (date >= DateTime.MaxValue.Date) return null;
            date = date.AddDays(1);
        }
        return null;
    }

    public static DateTimeOffset ResolveLocal(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Spring gap: first valid minute. Fall overlap: later occurrence, once only.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    public static string Describe(RecurrenceRule rule) => rule.Kind switch
    {
        RecurrenceKind.Daily => $"每 {rule.Interval} 天",
        RecurrenceKind.Weekly => $"每 {rule.Interval} 周",
        RecurrenceKind.Weekdays => rule.Interval == 1 ? "工作日" : $"每 {rule.Interval} 周的工作日",
        RecurrenceKind.CustomDays => $"每 {rule.Interval} 周 · " + string.Join("、", rule.DaysOfWeek.Select(d => "日一二三四五六"[(int)d])),
        _ => "一次性",
    };
}
