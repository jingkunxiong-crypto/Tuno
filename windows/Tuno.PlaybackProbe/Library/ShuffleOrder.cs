namespace Tuno.PlaybackProbe.Library;

public sealed class ShuffleOrder(Random? random = null)
{
    private readonly Random random = random ?? new Random();
    private readonly List<int> remaining = [];
    private readonly Stack<int> history = new();
    private bool started;

    public void Reset()
    {
        remaining.Clear();
        history.Clear();
        started=false;
    }

    public int Next(int count, int current, bool repeat=true)
    {
        if(count<2)return -1;
        if(remaining.Count==0)
        {
            if(started && !repeat)return -1;
            remaining.AddRange(Enumerable.Range(0,count).Where(i=>i!=current));
            started=true;
        }
        if(current>=0)history.Push(current);
        var position=random.Next(remaining.Count);
        var result=remaining[position];
        remaining.RemoveAt(position);
        return result;
    }

    public int Previous(int current)
    {
        if(!history.TryPop(out var previous))return -1;
        if(current>=0 && !remaining.Contains(current))remaining.Add(current);
        return previous;
    }
}
