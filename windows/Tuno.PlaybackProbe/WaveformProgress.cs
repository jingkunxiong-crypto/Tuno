using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Tuno.PlaybackProbe;

/// <summary>A lightweight playback ruler shaped like the Android waveform control.</summary>
public sealed class WaveformProgress : FrameworkElement
{
    private double progress;
    private long seed;
    public event EventHandler<double>? SeekRequested;

    public void SetTrack(long trackId)
    {
        seed=trackId;
        InvalidateVisual();
    }

    public void SetProgress(long positionMs,long durationMs)
    {
        var next=durationMs<=0?0:Math.Clamp(positionMs/(double)durationMs,0,1);
        if(Math.Abs(next-progress)<0.0005)return;
        progress=next;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if(ActualWidth<=0)return;
        SeekRequested?.Invoke(this,Math.Clamp(e.GetPosition(this).X/ActualWidth,0,1));
        e.Handled=true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        const int count=88;
        var gap=ActualWidth/count;
        var width=Math.Max(2,gap*0.58);
        var center=ActualHeight/2;
        var played=new SolidColorBrush(Color.FromArgb(0xF0,0xEF,0xFB,0xFC));
        var future=new SolidColorBrush(Color.FromArgb(0x62,0xD4,0xDF,0xE4));
        played.Freeze();future.Freeze();
        for(var i=0;i<count;i++)
        {
            var wave=Math.Abs(Math.Sin((i+1)*(0.41+(seed%19)*0.007))+Math.Sin((i+3)*0.17+seed%11)*0.42);
            var envelope=0.24+Math.Min(0.76,wave*0.55);
            var edge=Math.Min(1,Math.Min((i+2)/10d,(count-i+1)/10d));
            var height=Math.Max(5,ActualHeight*envelope*edge);
            var x=i*gap+(gap-width)/2;
            var rect=new Rect(x,center-height/2,width,height);
            drawingContext.DrawRoundedRectangle(i/(double)(count-1)<=progress?played:future,null,rect,width/2,width/2);
        }
        var playheadX=progress*Math.Max(0,ActualWidth-2);
        drawingContext.DrawRoundedRectangle(played,null,new Rect(playheadX,2,2,Math.Max(0,ActualHeight-4)),1,1);
    }
}
