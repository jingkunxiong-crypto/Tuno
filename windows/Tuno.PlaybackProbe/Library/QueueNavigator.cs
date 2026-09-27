namespace Tuno.PlaybackProbe.Library;

public static class QueueNavigator
{
    public static int Next(int count,int current,PlaybackMode mode,bool shuffle,bool automatic,ShuffleOrder order)
    {
        if(count==0 || current<0 || current>=count)return -1;
        if(automatic && mode==PlaybackMode.StopAfterCurrent)return -1;
        if(automatic && mode==PlaybackMode.RepeatTrack)return current;
        if(shuffle)
        {
            if(count==1)return mode==PlaybackMode.RepeatPlaylist?current:-1;
            return order.Next(count,current,mode==PlaybackMode.RepeatPlaylist);
        }
        if(current+1<count)return current+1;
        return mode==PlaybackMode.RepeatPlaylist?0:-1;
    }

    public static int Previous(int count,int current,bool shuffle,ShuffleOrder order)
    {
        if(count==0 || current<0 || current>=count)return -1;
        return shuffle?order.Previous(current):current-1;
    }
}
