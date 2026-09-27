namespace Tuno.PlaybackProbe.Library;

public static class QueueOrder
{
    public static int Move<T>(List<T> items,int from,int to,int current)
    {
        if(from<0 || from>=items.Count || to<0 || to>=items.Count || from==to)return current;
        var item=items[from];
        items.RemoveAt(from);
        items.Insert(to,item);
        if(current==from)return to;
        if(from<current && to>=current)return current-1;
        if(from>current && to<=current)return current+1;
        return current;
    }
}
