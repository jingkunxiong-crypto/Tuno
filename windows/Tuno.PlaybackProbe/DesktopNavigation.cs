using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Tuno.PlaybackProbe;

internal enum DesktopPage { Library, Player, Lyrics, Settings, Equalizer }

internal sealed class DesktopNavigation
{
    private readonly Dictionary<DesktopPage,FrameworkElement> pages;
    private DesktopPage toolReturnPage=DesktopPage.Library;
    public DesktopPage Current { get; private set; } = DesktopPage.Library;

    public DesktopNavigation(FrameworkElement library,FrameworkElement player,FrameworkElement lyrics,
        FrameworkElement settings,FrameworkElement equalizer)
    {
        pages=new()
        {
            [DesktopPage.Library]=library,
            [DesktopPage.Player]=player,
            [DesktopPage.Lyrics]=lyrics,
            [DesktopPage.Settings]=settings,
            [DesktopPage.Equalizer]=equalizer
        };
    }

    public void Navigate(DesktopPage page,bool animate=true)
    {
        var changed=Current!=page;
        foreach(var (candidate,panel) in pages)
            panel.Visibility=candidate==page?Visibility.Visible:Visibility.Collapsed;
        Current=page;
        if(changed && animate)AnimateEntry(pages[page]);
    }

    public void OpenTool(DesktopPage page)
    {
        if(page is not (DesktopPage.Settings or DesktopPage.Equalizer))
            throw new ArgumentOutOfRangeException(nameof(page));
        if(Current is not (DesktopPage.Settings or DesktopPage.Equalizer))
            toolReturnPage=Current;
        Navigate(page);
    }

    public void ReturnFromTool()=>Navigate(toolReturnPage);

    private static void AnimateEntry(FrameworkElement panel)
    {
        panel.Opacity=0;
        panel.RenderTransformOrigin=new Point(0.5,0.5);
        var scale=new ScaleTransform(0.985,0.985);
        panel.RenderTransform=scale;
        var ease=new QuadraticEase{EasingMode=EasingMode.EaseOut};
        panel.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(220)){EasingFunction=ease});
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(0.985,1,TimeSpan.FromMilliseconds(220)){EasingFunction=ease});
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(0.985,1,TimeSpan.FromMilliseconds(220)){EasingFunction=ease});
    }
}
