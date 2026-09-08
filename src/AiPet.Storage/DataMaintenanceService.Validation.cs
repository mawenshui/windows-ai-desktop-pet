using System.Text.Json;

namespace AiPet.Storage;

public sealed partial class DataMaintenanceService
{
    private static void ValidateModuleShape(string name, JsonElement root)
    {
        static void Require(bool valid) { if (!valid) throw new InvalidDataException("模块内容格式或容量无效。"); }
        static bool Text(JsonElement value,string property,int max,bool required=false) =>
            value.TryGetProperty(property,out var text) ? (text.ValueKind==JsonValueKind.String && text.GetString()!.Length<=max && (!required || !string.IsNullOrWhiteSpace(text.GetString()))) : !required;
        static bool Date(JsonElement value,string property) => !value.TryGetProperty(property,out var date) || date.ValueKind==JsonValueKind.Null || (date.ValueKind==JsonValueKind.String && date.TryGetDateTimeOffset(out _));
        static bool EnumValue(JsonElement value,string property,string[] names) => !value.TryGetProperty(property,out var entry) ||
            (entry.ValueKind==JsonValueKind.Number && entry.TryGetInt32(out var number) && number>=0 && number<names.Length) ||
            (entry.ValueKind==JsonValueKind.String && names.Contains(entry.GetString()));
        static bool Boolean(JsonElement value,string property) => !value.TryGetProperty(property,out var entry) || entry.ValueKind is JsonValueKind.True or JsonValueKind.False;

        foreach(var property in root.EnumerateObject())
            Require(root.EnumerateObject().Count(candidate=>candidate.Name.Equals(property.Name,StringComparison.OrdinalIgnoreCase))==1);
        if (name is "todos.json" or "shortcuts.json" or "notifications.json")
        {
            var listName=name=="notifications.json"?"entries":"items";
            Require(root.TryGetProperty(listName,out var items) && items.ValueKind==JsonValueKind.Array && items.GetArrayLength()<=(name=="notifications.json"?2000:10000));
            var ids=new HashSet<Guid>();
            foreach(var item in items.EnumerateArray())
            {
                Require(item.ValueKind==JsonValueKind.Object && item.TryGetProperty("id",out var id) && id.TryGetGuid(out var guid) && guid!=Guid.Empty && ids.Add(guid));
                Require(Text(item,name=="shortcuts.json"?"displayName":"title",200,true));
                foreach(var date in new[] {"createdAt","updatedAt","completedAt","reminderAt","dueAt","queuedAt","scheduledAt","submittedAt","handledAt","queuedOccurrenceAt","recurrenceAnchorAt"}) Require(Date(item,date));
                if(name=="todos.json")
                {
                    Require(Text(item,"notes",4000) && EnumValue(item,"status",new[]{"Pending","Completed"}) && EnumValue(item,"reminderState",new[]{"None","Scheduled","Delivered","Snoozed","Cancelled","Failed","Queued"}));
                    foreach(var flag in new[]{"isReminder","reminderBubbleEnabled","reminderRoamEnabled"}) Require(Boolean(item,flag));
                    if(item.TryGetProperty("additionalReminderTimes",out var times)) Require(times.ValueKind==JsonValueKind.Array && times.GetArrayLength()<=32 && times.EnumerateArray().All(time=>time.ValueKind==JsonValueKind.String && time.TryGetDateTimeOffset(out _)));
                    if(item.TryGetProperty("recurrence",out var rule))
                    {
                        Require(rule.ValueKind==JsonValueKind.Object && EnumValue(rule,"kind",new[]{"None","Daily","Weekly","Weekdays","CustomDays"}) && Date(rule,"endsAt"));
                        if(rule.TryGetProperty("interval",out var interval)) Require(interval.TryGetInt32(out var n) && n is >=1 and <=365);
                        if(rule.TryGetProperty("daysOfWeek",out var days)) Require(days.ValueKind==JsonValueKind.Array && days.GetArrayLength()<=7 && days.EnumerateArray().All(day=>Enum.TryParse<DayOfWeek>(day.ToString(),out var parsed) && Enum.IsDefined(parsed)));
                        if(rule.TryGetProperty("timeZoneId",out var zone) && !string.IsNullOrEmpty(zone.GetString()))
                        { try { _=TimeZoneInfo.FindSystemTimeZoneById(zone.GetString()!); } catch { throw new InvalidDataException("规则时区不可用。"); } }
                    }
                }
                else if(name=="shortcuts.json") Require(Text(item,"targetPath",32767,true) && Text(item,"group",40) && EnumValue(item,"kind",new[]{"File","Application","Url","Folder"}) && Boolean(item,"pinned"));
                else Require(EnumValue(item,"state",new[]{"Queued","Submitted","Handled","Cancelled"}) && item.TryGetProperty("todoId",out var todo) && todo.TryGetGuid(out _));
            }
        }
        if(name=="notifications.json" && root.TryGetProperty("quiet",out var quiet) && quiet.ValueKind!=JsonValueKind.Null)
        {
            Require(quiet.ValueKind==JsonValueKind.Object && Boolean(quiet,"enabled"));
            Require(quiet.TryGetProperty("start",out var start) && TimeOnly.TryParseExact(start.GetString(),"HH:mm",out _) && quiet.TryGetProperty("end",out var end) && TimeOnly.TryParseExact(end.GetString(),"HH:mm",out _));
        }
        if(name=="settings.json")
        {
            foreach(var module in new[] {"pet","search","toolWindow","ai","appearance","features","autostart","hotkeys","backup"})
                if(root.TryGetProperty(module,out var value)) Require(value.ValueKind==JsonValueKind.Object);
            if(root.TryGetProperty("search",out var search) && search.TryGetProperty("ranges",out var ranges)) Require(ranges.ValueKind==JsonValueKind.Array && ranges.GetArrayLength()<=1000 && ranges.EnumerateArray().All(range=>range.ValueKind==JsonValueKind.String && !string.IsNullOrWhiteSpace(range.GetString())));
            if(root.TryGetProperty("ai",out var ai) && ai.TryGetProperty("profiles",out var profiles))
                Require(profiles.ValueKind==JsonValueKind.Array && profiles.GetArrayLength()<=100 && profiles.EnumerateArray().All(profile=>profile.ValueKind==JsonValueKind.Object && Text(profile,"id",100,true) && Text(profile,"displayName",100,true) && Text(profile,"endpoint",2048,true) && Text(profile,"model",200,true)));
            if(root.TryGetProperty("hotkeys",out var hotkeys)) Require(Boolean(hotkeys,"enabled") && Text(hotkeys,"searchGesture",80,true) && Text(hotkeys,"quickTodoGesture",80,true));
            if(root.TryGetProperty("backup",out var backup))
            {
                Require(Boolean(backup,"automaticEnabled"));
                if(backup.TryGetProperty("retentionCount",out var retention)) Require(retention.TryGetInt32(out var count) && count is >=1 and <=30);
            }
        }
        if(name=="provider-presets.json") Require(root.TryGetProperty("providers",out var providers) && providers.ValueKind==JsonValueKind.Array && providers.GetArrayLength()<=32 && providers.EnumerateArray().All(provider=>provider.ValueKind==JsonValueKind.Object));
        if(name=="layout.json") foreach(var property in root.EnumerateObject()) Require(property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null);
    }
}
