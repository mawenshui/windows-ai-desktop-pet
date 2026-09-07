namespace AiPet.Search;

public sealed partial class SearchIndex
{
    public void SetPinned(SearchItem item, bool pinned)
    {
        using var command=_conn.CreateCommand();
        command.CommandText="INSERT INTO search_preferences(range_id,full_path,pinned) SELECT $id,$path,$pin WHERE EXISTS(SELECT 1 FROM items WHERE range_id=$id AND full_path=$path) ON CONFLICT(range_id,full_path) DO UPDATE SET pinned=excluded.pinned";
        command.Parameters.AddWithValue("$id",item.RangeId.ToString("D")); command.Parameters.AddWithValue("$path",item.FullPath); command.Parameters.AddWithValue("$pin",pinned?1:0); command.ExecuteNonQuery();
    }
    public void RecordUse(SearchItem item,DateTimeOffset time)
    {
        using var command=_conn.CreateCommand();
        command.CommandText="INSERT INTO search_preferences(range_id,full_path,last_used) SELECT $id,$path,$at WHERE EXISTS(SELECT 1 FROM items WHERE range_id=$id AND full_path=$path) ON CONFLICT(range_id,full_path) DO UPDATE SET last_used=excluded.last_used";
        command.Parameters.AddWithValue("$id",item.RangeId.ToString("D")); command.Parameters.AddWithValue("$path",item.FullPath); command.Parameters.AddWithValue("$at",time.ToUniversalTime().ToString("O")); command.ExecuteNonQuery();
    }
    public void ClearHistory()
    {
        using var command=_conn.CreateCommand(); command.CommandText="UPDATE search_preferences SET last_used=NULL; DELETE FROM search_preferences WHERE pinned=0;"; command.ExecuteNonQuery();
    }
}
