using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using Tuno.PlaybackProbe.Lyrics;
using Tuno.PlaybackProbe.Library;
namespace Tuno.PlaybackProbe;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty FilenameModeProperty=
        DependencyProperty.Register(nameof(FilenameMode),typeof(int),typeof(MainWindow),new PropertyMetadata(0));
    public int FilenameMode
    {
        get=>(int)GetValue(FilenameModeProperty);
        set=>SetValue(FilenameModeProperty,value);
    }
    private LibVLC? engine;
    private MediaPlayer? player;
    private LibraryStore? store;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(200)};
    private readonly DispatcherTimer searchTimer=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private readonly DispatcherTimer speedRampTimer=new(){Interval=TimeSpan.FromMilliseconds(35)};
    private readonly DispatcherTimer speedSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private List<LibraryTrack> allTracks=[];
    private List<LibraryTrack> playbackQueue=[];
    private ListCollectionView? view;
    private CancellationTokenSource? scan;
    private int index=-1, reloadVersion;
    private long? activePlaylist, pendingSeek;
    private long rememberedPosition;
    private bool favoritesOnly,seeking,closing,ready,restored;
    private LibraryCategory categoryMode=LibraryCategory.None;
    private string? selectedCategory;
    private bool changingCategorySelection;
    private readonly string[] initialFiles;
    private readonly string dataDirectory;
    private readonly string preferencesPath;
    private readonly DesktopPreferences preferences;
    private readonly LyricsRepository lyricsRepository;
    private readonly ShuffleOrder shuffleOrder=new();
    private IReadOnlyList<LyricLine> lyricLines=[];
    private int activeLyric=-1, lyricRequest;
    private bool changingLyricSelection, browsingLyrics, shuffleEnabled;
    private DateTime lyricBrowseUntil=DateTime.MinValue;
    private ScrollViewer? animatedLyricScroll;
    private long lyricScrollStarted;
    private double lyricScrollFrom,lyricScrollTo,lyricScrollDurationMs;
    private int lyricCenterRequest;
    private PlaybackMode playbackMode=PlaybackMode.RepeatOff;
    private Point? queueDragOrigin;
    private LibraryTrack? queueDragTrack;
    private Point? playerGestureOrigin;
    private bool playerGestureHorizontal,playerTransitioning,playerSwitchPreparing;
    private int playerNeighborIndex=-1,playerNeighborDirection,playerArtworkRequest,playerNextPreviewRequest;
    private double playerGestureOffset;
    private bool suppressPreferenceEvents=true;
    private bool updatingSpeedSlider;
    private double playbackSpeed=1,appliedPlaybackSpeed=1,targetPlaybackSpeed=1;
    private readonly DesktopNavigation navigation;
    private readonly List<Slider> equalizerSliders=[];
    private readonly Dictionary<long,BitmapSource?> artworkCache=[];
    private readonly Queue<long> artworkCacheOrder=[];
    private static readonly double[] PlaybackSpeeds=[0.75,1.0,1.25,1.5,2.0];

    public MainWindow(string[]? initialFiles=null,string? dataDirectory=null)
    {
        this.initialFiles=initialFiles ?? [];
        this.dataDirectory=dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Tuno");
        preferencesPath=Path.Combine(this.dataDirectory,"preferences.json");
        preferences=DesktopPreferences.Load(preferencesPath);
        preferences.ExcludedFolders ??=[];
        preferences.EqualizerBands ??=[];
        lyricsRepository=new LyricsRepository(this.dataDirectory);
        InitializeComponent();
        SourceInitialized+=(_,_)=>
        {
            WindowChromeBounds.Attach(this);
            var acrylic=DesktopAcrylic.TryApply(this);
            BackdropFallback.Visibility=acrylic?Visibility.Collapsed:Visibility.Visible;
            BackdropTint.Visibility=acrylic?Visibility.Visible:Visibility.Collapsed;
        };
        PlayerWaveform.SeekRequested+=PlayerWaveformSeekRequested;
        MiniWaveform.SeekRequested+=PlayerWaveformSeekRequested;
        navigation=new DesktopNavigation(LibraryPanel,NowPlayingPanel,FullscreenLyricsPanel,SettingsPanel,EqualizerPanel);
        SortBox.SelectedIndex=0;
        Loaded+=Initialize;
        timer.Tick+=(_,_)=>Refresh();
        searchTimer.Tick+=(_,_)=>{searchTimer.Stop();ApplySearch();};
        speedRampTimer.Tick+=(_,_)=>AdvancePlaybackRate();
        speedSaveTimer.Tick+=(_,_)=>{speedSaveTimer.Stop();SavePreferences();};
        SetBusy(true);
    }
    private void TitleMinimizeClick(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    private void TitleMaximizeClick(object sender,RoutedEventArgs e)
    {
        if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void TitleCloseClick(object sender,RoutedEventArgs e)=>SystemCommands.CloseWindow(this);
    private async void Initialize(object sender,RoutedEventArgs e)
    {
        try
        {
            store=await Task.Run(()=>new LibraryStore(dataDirectory));
            Core.Initialize();engine=new LibVLC("--no-video");player=new MediaPlayer(engine){Volume=(int)Volume.Value};
            InitializePreferences();
            InitializeEqualizer();
            player.EncounteredError+=(_,_)=>Dispatcher.BeginInvoke(()=>
            {if(!closing){Status.Text="无法播放此文件";MiniState.Text="请选择其他歌曲";}});
            player.EndReached+=(_,_)=>Dispatcher.BeginInvoke(()=>
            {
                if(closing || player.State!=VLCState.Ended)return;
                if(!PlayNext(true)){MiniState.Text="播放完毕";rememberedPosition=0;}
            });
            ready=true;
            await Reload();await ReloadPlaylists();
            var saved=await Task.Run(store.LoadPlayback);
            if(saved is not null)
            {
                Volume.Value=Math.Clamp(saved.Volume,0,100);
                var lookup=allTracks.ToDictionary(t=>t.Id);
                playbackQueue=saved.TrackIds.Where(lookup.ContainsKey).Select(id=>lookup[id]).ToList();
                var currentId=saved.Index>=0 && saved.Index<saved.TrackIds.Length?saved.TrackIds[saved.Index]:-1;
                index=playbackQueue.FindIndex(t=>t.Id==currentId);
                shuffleEnabled=saved.Shuffle;UpdateShuffleButton();
                playbackMode=Enum.IsDefined(saved.Mode)?saved.Mode:PlaybackMode.RepeatOff;UpdatePlaybackModeButton();UpdateQueue();
                if(index>=0)
                {
                    rememberedPosition=Math.Clamp(saved.PositionMs,0,Math.Max(0,playbackQueue[index].DurationMs));restored=true;
                    Status.Text=playbackQueue[index].Title;
                    MiniState.Text="已恢复 · 点击播放继续";
                    await ShowCover(playbackQueue[index].Id);
                    SetHeroTrack(playbackQueue[index]);
                    await ShowLyrics(playbackQueue[index]);
                }
            }
            timer.Start();
            LibraryStatus.Text=allTracks.Count==0?"添加音乐文件夹，建立你的本地音乐库。":"音乐库已恢复；可添加文件夹或重新扫描。";
        }
        catch(Exception ex){LibraryStatus.Text="初始化失败："+ex.Message;}
        finally {if(!closing)SetBusy(false);}
        if(ready && initialFiles.Length>0)await Import(initialFiles);
    }
    private void InitializePreferences()
    {
        if(!preferences.ShowAllMusic && !preferences.ShowFavorites && !preferences.ShowAlbums && !preferences.ShowArtists && !preferences.ShowFolders)
            preferences.ShowAllMusic=true;
        suppressPreferenceEvents=true;
        FilenameModeBox.SelectedIndex=Math.Clamp(preferences.FilenameMode,0,2);
        ShowAllMusicCheck.IsChecked=preferences.ShowAllMusic;
        ShowFavoritesCheck.IsChecked=preferences.ShowFavorites;
        ShowAlbumsCheck.IsChecked=preferences.ShowAlbums;
        ShowArtistsCheck.IsChecked=preferences.ShowArtists;
        ShowFoldersCheck.IsChecked=preferences.ShowFolders;
        ExcludedFoldersList.ItemsSource=preferences.ExcludedFolders;
        preferences.PlaybackSpeedIndex=Math.Clamp(preferences.PlaybackSpeedIndex,0,PlaybackSpeeds.Length-1);
        playbackSpeed=PlaybackSpeedScale.Normalize(preferences.PlaybackSpeed ?? PlaybackSpeeds[preferences.PlaybackSpeedIndex]);
        appliedPlaybackSpeed=targetPlaybackSpeed=playbackSpeed;
        preferences.PlaybackSpeed=playbackSpeed;
        SpeedSlider.Value=PlaybackSpeedScale.ToProgress(playbackSpeed);
        UpdateSpeedButtons();ApplyFilenameMode();ApplyVisibleTabs();
        suppressPreferenceEvents=false;
    }
    private void InitializeEqualizer()
    {
        if(engine is null)return;
        using var eq=new Equalizer();
        suppressPreferenceEvents=true;
        EqualizerEnabledCheck.IsChecked=preferences.EqualizerEnabled;
        for(uint i=0;i<eq.PresetCount;i++)EqualizerPresetBox.Items.Add(new ComboBoxItem{Content=eq.PresetName(i),Tag=(int)i});
        EqualizerPresetBox.Items.Add(new ComboBoxItem{Content="自定义",Tag=-1});
        if(preferences.EqualizerPreset >= (int)eq.PresetCount)preferences.EqualizerPreset=-1;
        var bandCount=(int)eq.BandCount;
        if(preferences.EqualizerBands.Length!=bandCount)preferences.EqualizerBands=Enumerable.Repeat(0f,bandCount).ToArray();
        for(var i=0;i<bandCount;i++)
        {
            var index=i;var frequency=eq.BandFrequency((uint)i);
            var label=frequency>=1000?$"{frequency/1000f:0.#} kHz":$"{frequency:0} Hz";
            var band=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(3,4,3,4)};
            var slider=new Slider{Orientation=Orientation.Vertical,Height=220,Width=32,Minimum=-12,Maximum=12,Value=preferences.EqualizerBands[i],TickFrequency=1,Foreground=(Brush)FindResource("Accent"),HorizontalAlignment=HorizontalAlignment.Center};
            slider.ValueChanged+=(_,_)=>EqualizerBandChanged(index,slider.Value);
            var value=new TextBlock{Text=$"{slider.Value:+0.0;-0.0;0.0} dB",Foreground=(Brush)FindResource("Muted"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
            slider.ValueChanged+=(_,_)=>value.Text=$"{slider.Value:+0.0;-0.0;0.0} dB";
            band.Children.Add(value);band.Children.Add(slider);
            band.Children.Add(new TextBlock{Text=label,Foreground=(Brush)FindResource("Muted"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,10,0,0)});
            EqualizerBands.Children.Add(band);equalizerSliders.Add(slider);
        }
        var selected=EqualizerPresetBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item=>item.Tag is int tag && tag==preferences.EqualizerPreset);
        EqualizerPresetBox.SelectedItem=selected??EqualizerPresetBox.Items.Cast<ComboBoxItem>().LastOrDefault();
        SetEqualizerSlidersEnabled();suppressPreferenceEvents=false;ApplyEqualizer();
    }
    private void ApplyEqualizer()
    {
        if(player is null)return;
        try
        {
            if(!preferences.EqualizerEnabled){player.UnsetEqualizer();return;}
            if(preferences.EqualizerPreset>=0)
            {using var eq=new Equalizer((uint)preferences.EqualizerPreset);player.SetEqualizer(eq);}
            else
            {
                using var eq=new Equalizer();
                for(var i=0;i<Math.Min((int)eq.BandCount,equalizerSliders.Count);i++)eq.SetAmp((float)equalizerSliders[i].Value,(uint)i);
                player.SetEqualizer(eq);
            }
        }
        catch(Exception ex){LibraryStatus.Text="均衡器暂不可用："+ex.Message;}
    }
    private void SetEqualizerSlidersEnabled()
    {
        foreach(var slider in equalizerSliders)slider.IsEnabled=preferences.EqualizerEnabled && preferences.EqualizerPreset<0;
        EqualizerSummary.Text=!preferences.EqualizerEnabled?"当前已关闭；调整会保留。":
            preferences.EqualizerPreset<0?"自定义音色 · 拖动频段即可实时试听。":
            $"当前预设：{(EqualizerPresetBox.SelectedItem as ComboBoxItem)?.Content}";
    }
    private void EqualizerBandChanged(int index,double value)
    {
        if(suppressPreferenceEvents || index>=preferences.EqualizerBands.Length)return;
        preferences.EqualizerBands[index]=(float)value;preferences.EqualizerPreset=-1;
        suppressPreferenceEvents=true;EqualizerPresetBox.SelectedItem=EqualizerPresetBox.Items.Cast<ComboBoxItem>().LastOrDefault();suppressPreferenceEvents=false;
        SetEqualizerSlidersEnabled();ApplyEqualizer();SavePreferences();
    }
    private void SavePreferences()
    {
        try{preferences.Save(preferencesPath);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){LibraryStatus.Text="设置未能保存："+ex.Message;}
    }
    private bool IsExcluded(string path)
    {
        var key=LibraryStore.PathKey(path);
        return preferences.ExcludedFolders.Any(folder=>
        {
            var excluded=LibraryStore.PathKey(folder);
            return key.Equals(excluded,StringComparison.OrdinalIgnoreCase) || key.StartsWith(excluded+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        });
    }
    private void ApplyFilenameMode()=>FilenameMode=preferences.FilenameMode;
    private void ApplyVisibleTabs()
    {
        AllMusicButton.Visibility=preferences.ShowAllMusic?Visibility.Visible:Visibility.Collapsed;
        FavoritesButton.Visibility=preferences.ShowFavorites?Visibility.Visible:Visibility.Collapsed;
        AlbumsButton.Visibility=preferences.ShowAlbums?Visibility.Visible:Visibility.Collapsed;
        ArtistsButton.Visibility=preferences.ShowArtists?Visibility.Visible:Visibility.Collapsed;
        FoldersButton.Visibility=preferences.ShowFolders?Visibility.Visible:Visibility.Collapsed;
    }
    private void UpdateSpeedButtons()
    {
        var label=$"{playbackSpeed:0.0#}×";
        if(SpeedButton is not null)SpeedButton.Content=label+"  ▴";
        if(SpeedValue is not null)SpeedValue.Text=label;
    }
    private void OpenSettings(object sender,RoutedEventArgs e)
    =>navigation.OpenTool(DesktopPage.Settings);
    private void OpenEqualizerSettings(object sender,RoutedEventArgs e)
    =>navigation.OpenTool(DesktopPage.Equalizer);
    private void CloseSettings(object sender,RoutedEventArgs e)
    =>navigation.ReturnFromTool();
    private void CloseEqualizer(object sender,RoutedEventArgs e)
    =>navigation.ReturnFromTool();
    private void OpenSystemLanguageSettings(object sender,RoutedEventArgs e)
    {
        try{Process.Start(new ProcessStartInfo("ms-settings:regionlanguage"){UseShellExecute=true});}
        catch(Exception ex){LibraryStatus.Text="无法打开 Windows 语言设置："+ex.Message;}
    }
    private void FilenameModeChanged(object sender,SelectionChangedEventArgs e)
    {
        if(suppressPreferenceEvents || FilenameModeBox.SelectedIndex<0)return;
        preferences.FilenameMode=FilenameModeBox.SelectedIndex;ApplyFilenameMode();SavePreferences();
    }
    private void VisibleTabsChanged(object sender,RoutedEventArgs e)
    {
        if(suppressPreferenceEvents)return;
        preferences.ShowAllMusic=ShowAllMusicCheck.IsChecked==true;
        preferences.ShowFavorites=ShowFavoritesCheck.IsChecked==true;
        preferences.ShowAlbums=ShowAlbumsCheck.IsChecked==true;
        preferences.ShowArtists=ShowArtistsCheck.IsChecked==true;
        preferences.ShowFolders=ShowFoldersCheck.IsChecked==true;
        if(!preferences.ShowAllMusic && !preferences.ShowFavorites && !preferences.ShowAlbums && !preferences.ShowArtists && !preferences.ShowFolders)
        {
            suppressPreferenceEvents=true;ShowAllMusicCheck.IsChecked=true;preferences.ShowAllMusic=true;suppressPreferenceEvents=false;
            LibraryStatus.Text="至少保留一个音乐库入口。";
        }
        ApplyVisibleTabs();SavePreferences();
        if(categoryMode==LibraryCategory.Albums && !preferences.ShowAlbums || categoryMode==LibraryCategory.Artists && !preferences.ShowArtists || categoryMode==LibraryCategory.Folders && !preferences.ShowFolders)
            _=SwitchView(null,false);
    }
    private async void AddExcludedFolder(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog{Title="选择要从音乐库排除的文件夹"};
        if(dialog.ShowDialog(this)!=true)return;
        var path=Path.GetFullPath(dialog.FolderName);
        if(!preferences.ExcludedFolders.Any(p=>LibraryStore.PathKey(p)==LibraryStore.PathKey(path)))preferences.ExcludedFolders.Add(path);
        preferences.ExcludedFolders.Sort(StringComparer.OrdinalIgnoreCase);ExcludedFoldersList.Items.Refresh();SavePreferences();
        await Guard(Reload);LibraryStatus.Text="排除目录已更新；现有音频文件仍保留在磁盘上。";
    }
    private async void RemoveExcludedFolder(object sender,RoutedEventArgs e)
    {
        if(ExcludedFoldersList.SelectedItem is not string path)return;
        preferences.ExcludedFolders.Remove(path);ExcludedFoldersList.Items.Refresh();SavePreferences();
        await Guard(Reload);LibraryStatus.Text="目录已从排除列表移除；重新扫描后可更新音乐库。";
    }
    private void EqualizerEnabledChanged(object sender,RoutedEventArgs e)
    {
        if(suppressPreferenceEvents)return;
        preferences.EqualizerEnabled=EqualizerEnabledCheck.IsChecked==true;ApplyEqualizer();SetEqualizerSlidersEnabled();SavePreferences();
    }
    private void EqualizerPresetChanged(object sender,SelectionChangedEventArgs e)
    {
        if(suppressPreferenceEvents || EqualizerPresetBox.SelectedItem is not ComboBoxItem item || item.Tag is not int preset)return;
        preferences.EqualizerPreset=preset;SetEqualizerSlidersEnabled();ApplyEqualizer();SavePreferences();
    }
    private void ToggleSpeedPanel(object sender,RoutedEventArgs e)=>SpeedPopup.IsOpen=!SpeedPopup.IsOpen;
    private void SpeedSliderChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(suppressPreferenceEvents || updatingSpeedSlider)return;
        var progress=Math.Abs(e.NewValue-PlaybackSpeedScale.Midpoint)<=5?PlaybackSpeedScale.Midpoint:e.NewValue;
        if(Math.Abs(progress-e.NewValue)>0.01)
        {
            updatingSpeedSlider=true;SpeedSlider.Value=progress;updatingSpeedSlider=false;
        }
        SetPlaybackSpeed(PlaybackSpeedScale.FromProgress(progress));
    }
    private void DecreaseSpeed(object sender,RoutedEventArgs e)
        =>SpeedSlider.Value=PlaybackSpeedScale.ToProgress(playbackSpeed-0.05);
    private void IncreaseSpeed(object sender,RoutedEventArgs e)
        =>SpeedSlider.Value=PlaybackSpeedScale.ToProgress(playbackSpeed+0.05);
    private void ResetSpeed(object sender,RoutedEventArgs e)
        =>SpeedSlider.Value=PlaybackSpeedScale.Midpoint;
    private void SetPlaybackSpeed(double speed)
    {
        speed=PlaybackSpeedScale.Normalize(speed);
        if(Math.Abs(speed-playbackSpeed)<0.001)return;
        playbackSpeed=targetPlaybackSpeed=speed;preferences.PlaybackSpeed=speed;
        UpdateSpeedButtons();
        if(player is not null && !speedRampTimer.IsEnabled)speedRampTimer.Start();
        speedSaveTimer.Stop();speedSaveTimer.Start();
    }
    private void AdvancePlaybackRate()
    {
        if(player is null){speedRampTimer.Stop();appliedPlaybackSpeed=targetPlaybackSpeed;return;}
        var delta=targetPlaybackSpeed-appliedPlaybackSpeed;
        if(Math.Abs(delta)<0.015){appliedPlaybackSpeed=targetPlaybackSpeed;speedRampTimer.Stop();}
        else appliedPlaybackSpeed+=delta*0.38;
        player.SetRate((float)appliedPlaybackSpeed);
    }
    private void PlayerGestureStart(object sender,MouseButtonEventArgs e)
    {
        playerGestureOrigin=null;
        playerGestureHorizontal=false;playerGestureOffset=0;
        if(e.LeftButton!=MouseButtonState.Pressed || playerTransitioning || playerSwitchPreparing || playbackQueue.Count<2)return;
        for(DependencyObject? node=e.OriginalSource as DependencyObject;node is not null && node!=PlayerPages;node=System.Windows.Media.VisualTreeHelper.GetParent(node))
            if(node is ButtonBase or Slider or TextBoxBase or ItemsControl or ScrollBar)return;
        playerGestureOrigin=e.GetPosition(PlayerPages);
        PlayerPages.CaptureMouse();
    }
    private void PlayerGestureMove(object sender,MouseEventArgs e)
    {
        if(playerGestureOrigin is not Point start || playerTransitioning || e.LeftButton!=MouseButtonState.Pressed)return;
        var delta=e.GetPosition(PlayerPages)-start;
        if(!playerGestureHorizontal)
        {
            if(Math.Abs(delta.X)<8 && Math.Abs(delta.Y)<8)return;
            if(Math.Abs(delta.X)<=Math.Abs(delta.Y)*1.15)return;
            playerGestureHorizontal=true;
        }
        var direction=delta.X<0?1:-1;
        var target=ResolveAdjacentTarget(direction);
        if(target<0){delta.X*=0.22;NeighborStage.Visibility=Visibility.Collapsed;}
        else if(playerNeighborIndex!=target || playerNeighborDirection!=direction)_=PrepareNeighbor(target,direction);
        playerGestureOffset=Math.Clamp(delta.X,-PlayerPages.ActualWidth*0.82,PlayerPages.ActualWidth*0.82);
        PlayerStageTranslate.X=playerGestureOffset;
        if(target>=0)
        {
            NeighborStageTranslate.X=direction*PlayerPages.ActualWidth+playerGestureOffset;
            NeighborStage.Visibility=Visibility.Visible;
        }
        e.Handled=true;
    }
    private void PlayerGestureEnd(object sender,MouseButtonEventArgs e)
    {
        if(playerGestureOrigin is not Point start)return;
        playerGestureOrigin=null;PlayerPages.ReleaseMouseCapture();var delta=e.GetPosition(PlayerPages)-start;
        if(playerGestureHorizontal)
        {
            var commit=playerNeighborIndex>=0 && Math.Abs(playerGestureOffset)>Math.Max(72,PlayerPages.ActualWidth*0.18);
            AnimatePlayerPages(commit?playerNeighborIndex:-1,playerNeighborDirection,playerGestureOffset);
            e.Handled=true;
        }
        else if(delta.Y< -85 && Math.Abs(delta.Y)>Math.Abs(delta.X)*1.2)
        {OpenFullscreenLyrics(sender,e);e.Handled=true;}
    }
    private void WindowKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape)
        {
            if(navigation.Current is DesktopPage.Settings or DesktopPage.Equalizer)navigation.ReturnFromTool();
            else if(navigation.Current==DesktopPage.Lyrics)navigation.Navigate(DesktopPage.Player);
            else if(navigation.Current==DesktopPage.Player)navigation.Navigate(DesktopPage.Library);
            else return;
            e.Handled=true;return;
        }
        if(Keyboard.FocusedElement is TextBoxBase or Slider or ComboBox)return;
        var modifiers=e.KeyboardDevice.Modifiers;
        if(modifiers==ModifierKeys.None)
        {
            if(e.Key==Key.Left){SeekBySeconds(-5);e.Handled=true;return;}
            if(e.Key==Key.Right){SeekBySeconds(5);e.Handled=true;return;}
            if(e.Key==Key.Up){Volume.Value=Math.Clamp(Volume.Value+5,Volume.Minimum,Volume.Maximum);e.Handled=true;return;}
            if(e.Key==Key.Down){Volume.Value=Math.Clamp(Volume.Value-5,Volume.Minimum,Volume.Maximum);e.Handled=true;return;}
            if(e.Key==Key.PageUp){Previous(sender,e);e.Handled=true;return;}
            if(e.Key==Key.PageDown){Next(sender,e);e.Handled=true;return;}
        }
        if(Keyboard.FocusedElement is ButtonBase)return;
        if(e.Key==Key.Space && !modifiers.HasFlag(ModifierKeys.Control)){Toggle(sender,e);e.Handled=true;return;}
        if(modifiers.HasFlag(ModifierKeys.Control) && e.Key==Key.Right){Next(sender,e);e.Handled=true;}
        else if(modifiers.HasFlag(ModifierKeys.Control) && e.Key==Key.Left){Previous(sender,e);e.Handled=true;}
        else if(e.Key==Key.F && NowPlayingPanel.Visibility==Visibility.Visible){OpenFullscreenLyrics(sender,e);e.Handled=true;}
    }
    private void OpenFullscreenLyrics(object sender,RoutedEventArgs e)
    {
        if(index>=0 && index<playbackQueue.Count){FullscreenSongTitle.Text=playbackQueue[index].Title;FullscreenSongArtist.Text=playbackQueue[index].Artist;}
        QueuePane.Visibility=Visibility.Collapsed;LyricsPane.Visibility=Visibility.Visible;
        FullscreenLyricList.ItemsSource=lyricLines;SetLyricSelection(activeLyric);
        navigation.Navigate(DesktopPage.Lyrics);
        FullscreenLyricsPlaceholder.Visibility=lyricLines.Count==0?Visibility.Visible:Visibility.Collapsed;
        CenterActiveLyric(FullscreenLyricList);
    }
    private void CloseFullscreenLyrics(object sender,RoutedEventArgs e)
    {navigation.Navigate(DesktopPage.Player);ShowLyricsPane(sender,e);CenterActiveLyric(LyricList);}
    private async Task Reload()
    {
        if(store is null)return;
        var revision=++reloadVersion;var playlist=activePlaylist;
        var all=(await Task.Run(()=>store.ReadTracks())).Where(t=>!IsExcluded(t.Path)).ToList();
        var rows=playlist is null?all:(await Task.Run(()=>store.ReadTracks(playlist))).Where(t=>!IsExcluded(t.Path)).ToList();
        if(closing || revision!=reloadVersion)return;
        var selected=Songs.SelectedItems.Cast<LibraryTrack>().Select(t=>t.Id).ToHashSet();
        allTracks=all;
        UpdateCategories();
        view=new ListCollectionView(rows);
        Songs.ItemsSource=view;ApplySort();ApplySearch();
        foreach(var row in rows.Where(t=>selected.Contains(t.Id)))Songs.SelectedItems.Add(row);
        SongSelectionChanged(Songs,new SelectionChangedEventArgs(Selector.SelectionChangedEvent,
            new System.Collections.ArrayList(),new System.Collections.ArrayList()));
        LibraryCount.Text=$"本地共 {all.Count} 首";
    }
    private async Task ReloadPlaylists()
    {
        if(store is null)return;
        var target=(PlaylistTarget.SelectedItem as Playlist)?.Id;
        var rows=await Task.Run(store.Playlists);
        if(closing)return;
        PlaylistList.ItemsSource=rows;
        PlaylistHomeList.ItemsSource=rows;
        PlaylistHomeCount.Text=$"{rows.Count} 个歌单";
        PlaylistHomeEmpty.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
        PlaylistTarget.ItemsSource=rows;
        PlaylistTarget.SelectedItem=rows.FirstOrDefault(p=>p.Id==target) ?? rows.FirstOrDefault();
        if(activePlaylist is long id)
        {
            var active=rows.FirstOrDefault(p=>p.Id==id);
            PlaylistList.SelectedItem=active;
            RenamePlaylistName.Text=active?.Name ?? "";
        }
    }
    private void ApplySearch()
    {
        if(view is null)return;
        var query=SearchBox.Text.Trim();
        view.Filter=item=>item is LibraryTrack t && (!favoritesOnly || t.Favorite) &&
            (categoryMode==LibraryCategory.None || selectedCategory is not null &&
                LibraryCategories.Key(t,categoryMode).Equals(selectedCategory,StringComparison.OrdinalIgnoreCase)) &&
            (query.Length==0 || new[]{t.Title,t.Artist,t.Album,t.FileName}.Any(s=>s.Contains(query,StringComparison.OrdinalIgnoreCase)));
        EmptyState.Visibility=view.IsEmpty?Visibility.Visible:Visibility.Collapsed;
        EmptyState.Text=allTracks.Count==0?"还没有音乐，点击「添加音乐」开始。":
            categoryMode!=LibraryCategory.None && selectedCategory is null?$"请选择{LibraryCategories.Title(categoryMode)}。":"没有符合条件的歌曲。";
    }
    private void SearchChanged(object sender,TextChangedEventArgs e){searchTimer.Stop();searchTimer.Start();}
    private void SortChanged(object sender,SelectionChangedEventArgs e)=>ApplySort();
    private void ApplySort()
    {
        if(view is null)return;
        var (property,direction)=SortBox.SelectedIndex switch
        {
            1=>(nameof(LibraryTrack.Title),ListSortDirection.Descending),
            2=>(nameof(LibraryTrack.Artist),ListSortDirection.Ascending),
            3=>(nameof(LibraryTrack.Album),ListSortDirection.Ascending),
            4=>(nameof(LibraryTrack.DurationMs),ListSortDirection.Ascending),
            5=>(nameof(LibraryTrack.ModifiedTicks),ListSortDirection.Descending),
            _=>(nameof(LibraryTrack.Title),ListSortDirection.Ascending)
        };
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(property,direction));
    }
    private void SongSelectionChanged(object sender,SelectionChangedEventArgs e)
    {if(TrackActions is not null)TrackActions.Visibility=Songs.SelectedItems.Count>0?Visibility.Visible:Visibility.Collapsed;}
    private async void ShowAll(object sender,RoutedEventArgs e)=>await SwitchView(null,false);
    private async void ShowFavorites(object sender,RoutedEventArgs e)=>await SwitchView(null,true);
    private void ShowPlaylists(object sender,RoutedEventArgs e)
    {
        navigation.Navigate(DesktopPage.Library);
        activePlaylist=null;categoryMode=LibraryCategory.None;selectedCategory=null;favoritesOnly=false;
        PlaylistList.SelectedItem=null;
        TrackView.Visibility=Visibility.Collapsed;CategoryHome.Visibility=Visibility.Collapsed;PlaylistHome.Visibility=Visibility.Visible;
        LibraryToolbar.Visibility=Visibility.Collapsed;LibraryActionRow.Visibility=Visibility.Collapsed;
        PlaylistHomeFeedback.Visibility=Visibility.Collapsed;
        UpdateAddMusicLabel();
        UpdateHomeTabs(true);
    }
    private void UpdateHomeTabs(bool playlists)
    {
        var normal=(Style)FindResource("SidebarButton");
        var selected=(Style)FindResource("SelectedSidebarButton");
        AllMusicButton.Style=!playlists && activePlaylist is null && !favoritesOnly && categoryMode==LibraryCategory.None?selected:normal;
        PlaylistsTabButton.Style=playlists || activePlaylist is not null?selected:normal;
        FavoritesButton.Style=favoritesOnly?selected:normal;
        AlbumsButton.Style=categoryMode==LibraryCategory.Albums?selected:normal;
        ArtistsButton.Style=categoryMode==LibraryCategory.Artists?selected:normal;
        FoldersButton.Style=categoryMode==LibraryCategory.Folders?selected:normal;
    }
    private async void ShowAlbums(object sender,RoutedEventArgs e)=>await SwitchCategory(LibraryCategory.Albums);
    private async void ShowArtists(object sender,RoutedEventArgs e)=>await SwitchCategory(LibraryCategory.Artists);
    private async void ShowFolders(object sender,RoutedEventArgs e)=>await SwitchCategory(LibraryCategory.Folders);
    private async Task SwitchCategory(LibraryCategory category)
    {
        navigation.Navigate(DesktopPage.Library);
        categoryMode=category;selectedCategory=null;activePlaylist=null;favoritesOnly=false;
        PlaylistList.SelectedItem=null;
        CategoryHomeTitle.Text=LibraryCategories.Title(category);
        TrackView.Visibility=Visibility.Collapsed;PlaylistHome.Visibility=Visibility.Collapsed;CategoryHome.Visibility=Visibility.Visible;
        LibraryToolbar.Visibility=Visibility.Visible;LibraryActionRow.Visibility=Visibility.Visible;
        CategoryBackButton.Visibility=Visibility.Collapsed;
        PlaylistManagementPanel.Visibility=Visibility.Collapsed;
        RenameEditor.Visibility=Visibility.Collapsed;
        ViewTitle.Text=LibraryCategories.Title(category);
        RemovePlaylistButton.IsEnabled=false;
        UpdateAddMusicLabel();
        UpdateHomeTabs(false);
        await Guard(Reload);
    }
    private async Task SwitchView(long? playlist,bool favorites)
    {
        navigation.Navigate(DesktopPage.Library);
        categoryMode=LibraryCategory.None;selectedCategory=null;activePlaylist=playlist;favoritesOnly=favorites;
        TrackView.Visibility=Visibility.Visible;PlaylistHome.Visibility=Visibility.Collapsed;CategoryHome.Visibility=Visibility.Collapsed;
        LibraryToolbar.Visibility=Visibility.Visible;LibraryActionRow.Visibility=Visibility.Visible;
        CategoryBackButton.Visibility=Visibility.Collapsed;
        PlaylistManagementPanel.Visibility=playlist is null?Visibility.Collapsed:Visibility.Visible;
        RenameEditor.Visibility=Visibility.Collapsed;
        if(playlist is null)PlaylistList.SelectedItem=null;
        else if(PlaylistList.SelectedItem is Playlist selected && selected.Id==playlist)RenamePlaylistName.Text=selected.Name;
        ViewTitle.Text=favorites?"我的收藏":playlist is null?"全部音乐":(PlaylistList.SelectedItem as Playlist)?.Name ?? "歌单";
        RemovePlaylistButton.IsEnabled=playlist is not null;
        UpdateAddMusicLabel();
        UpdateHomeTabs(false);
        await Guard(Reload);
    }
    private async void PlaylistSelected(object sender,SelectionChangedEventArgs e)
    {if(PlaylistList.SelectedItem is Playlist p && p.Id!=activePlaylist)await SwitchView(p.Id,false);}
    private void PlaylistHomeSelected(object sender,SelectionChangedEventArgs e)
    {
        if(PlaylistHomeList.SelectedItem is not Playlist playlist)return;
        PlaylistHomeList.SelectedItem=null;
        PlaylistList.SelectedItem=playlist;
    }
    private void CategorySelected(object sender,SelectionChangedEventArgs e)
    {
        if(changingCategorySelection || categoryMode==LibraryCategory.None)return;
        var entry=CategoryList.SelectedItem as CategoryEntry;
        selectedCategory=entry?.Key;
        ViewTitle.Text=entry is null?LibraryCategories.Title(categoryMode):$"{LibraryCategories.Title(categoryMode)} · {entry.Name}";
        TrackView.Visibility=entry is null?Visibility.Collapsed:Visibility.Visible;
        CategoryHome.Visibility=entry is null?Visibility.Visible:Visibility.Collapsed;
        CategoryBackButton.Visibility=entry is null?Visibility.Collapsed:Visibility.Visible;
        ApplySearch();
    }
    private void ReturnToCategory(object sender,RoutedEventArgs e)
    {
        CategoryList.SelectedItem=null;
        selectedCategory=null;
        TrackView.Visibility=Visibility.Collapsed;
        CategoryHome.Visibility=Visibility.Visible;
        CategoryBackButton.Visibility=Visibility.Collapsed;
    }
    private void UpdateCategories()
    {
        if(categoryMode==LibraryCategory.None)return;
        var entries=LibraryCategories.Build(allTracks,categoryMode);
        changingCategorySelection=true;
        CategoryList.ItemsSource=entries;
        var selected=entries.FirstOrDefault(entry=>entry.Key.Equals(selectedCategory,StringComparison.OrdinalIgnoreCase));
        CategoryList.SelectedItem=selected;
        changingCategorySelection=false;
        selectedCategory=selected?.Key;
        ViewTitle.Text=selected is null?LibraryCategories.Title(categoryMode):$"{LibraryCategories.Title(categoryMode)} · {selected.Name}";
        TrackView.Visibility=selected is null?Visibility.Collapsed:Visibility.Visible;
        CategoryHome.Visibility=selected is null?Visibility.Visible:Visibility.Collapsed;
        CategoryBackButton.Visibility=selected is null?Visibility.Collapsed:Visibility.Visible;
    }
    private async Task Guard(Func<Task> action)
    {
        try{if(ready && !closing)await action();}
        catch(Exception ex)
        {
            if(closing)return;
            var message="操作未完成："+ex.Message;
            if(PlaylistHome.Visibility==Visibility.Visible)SetPlaylistHomeFeedback(message);
            else LibraryStatus.Text=message;
        }
    }
    private void SetBusy(bool busy)
    {
        AddMusicButton.IsEnabled=!busy && ready;
        FolderButton.IsEnabled=FilesButton.IsEnabled=RescanButton.IsEnabled=!busy && ready;
        CancelButton.IsEnabled=busy && scan is not null;
        CancelButton.Visibility=busy && scan is not null?Visibility.Visible:Visibility.Collapsed;
        ScanBar.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
    }
    private async Task RunScan(Func<LibraryScanner,IProgress<ScanProgress>,CancellationToken,Task<ScanResult>> work,
        Func<LibraryStore,long,PlaylistImportResult>? addToPlaylist=null)
    {
        if(store is null || scan is not null || closing)return;
        var destination=activePlaylist;
        using var cancellation=new CancellationTokenSource();scan=cancellation;SetBusy(true);
        var progress=new Progress<ScanProgress>(p=>
        {if(!closing && scan==cancellation)LibraryStatus.Text=$"已检查 {p.Visited} 首，更新 {p.Updated} 首，提示 {p.Warnings} 项 · {p.FileName}";});
        try
        {
            var result=await work(new LibraryScanner(store,preferences.ExcludedFolders),progress,cancellation.Token);
            if(!closing)
            {
                if(destination is long playlist && addToPlaylist is not null)
                {
                    var imported=await Task.Run(()=>addToPlaylist(store,playlist));
                    LibraryStatus.Text=imported.Matched==0
                        ?"没有找到可加入歌单的音频文件。"
                        :$"已将 {imported.Added} 首歌曲加入歌单（匹配 {imported.Matched} 首，已有歌曲自动跳过）。";
                }
                else LibraryStatus.Text=$"扫描完成：检查 {result.Visited} 首，更新 {result.Updated} 首，提示 {result.Warnings} 项。";
            }
        }
        catch(OperationCanceledException){if(!closing)LibraryStatus.Text="扫描已取消，已保存的歌曲保留。";}
        catch(Exception ex){if(!closing)LibraryStatus.Text="扫描未完成："+ex.Message;}
        finally
        {
            scan=null;
            if(!closing){SetBusy(false);await Guard(Reload);await Guard(ReloadPlaylists);}
        }
    }
    private void OpenMusicMenu(object sender,RoutedEventArgs e)
    {
        if(sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget=button;
            menu.Placement=PlacementMode.Bottom;
            menu.HorizontalOffset=button.ActualWidth-menu.MinWidth;
            menu.IsOpen=true;
        }
    }
    private async void AddFolder(object sender,RoutedEventArgs e)
    {
        if(!ready)return;
        var dialog=new OpenFolderDialog{Title="选择音乐文件夹（包含子文件夹）"};
        if(dialog.ShowDialog(this)==true)
        {
            var folder=dialog.FolderName;
            await RunScan((s,p,t)=>s.ScanFolderAsync(folder,p,t),(db,id)=>PlaylistImporter.AddFolder(db,id,folder));
        }
    }
    private async void OpenFiles(object sender,RoutedEventArgs e)
    {
        if(!ready)return;
        var dialog=new OpenFileDialog{Multiselect=true,Filter="音频|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.wma;*.opus;*.aiff;*.aif"};
        if(dialog.ShowDialog(this)==true)
        {
            var files=dialog.FileNames;
            await Import(files,(db,id)=>PlaylistImporter.AddFiles(db,id,files));
        }
    }
    private Task Import(string[] files,Func<LibraryStore,long,PlaylistImportResult>? addToPlaylist=null)
        =>RunScan((s,p,t)=>s.ImportFilesAsync(files,p,t),addToPlaylist);
    private void UpdateAddMusicLabel()
        =>AddMusicButton.Content=activePlaylist is null?"＋  添加音乐  ▾":"＋  添加到歌单  ▾";
    private async void Rescan(object sender,RoutedEventArgs e)=>await Guard(async()=>
    {
        var folders=await Task.Run(store!.Folders);
        if(folders.Length==0){LibraryStatus.Text="还没有保存的文件夹，请先添加一个音乐文件夹。";return;}
        await RunScan(async(s,p,t)=>
        {
            int visited=0,updated=0,warnings=0;
            foreach(var folder in folders)
            {
                t.ThrowIfCancellationRequested();
                if(!Directory.Exists(folder)){warnings++;continue;}
                var r=await s.ScanFolderAsync(folder,p,t);visited+=r.Visited;updated+=r.Updated;warnings+=r.Warnings;
            }
            await Task.Run(()=>store!.RefreshAvailability(t),t);
            return new(visited,updated,warnings);
        });
    });
    private void CancelScan(object sender,RoutedEventArgs e)=>scan?.Cancel();
    private async void CreatePlaylist(object sender,RoutedEventArgs e)=>await Guard(async()=>
    {
        PlaylistHomeFeedback.Visibility=Visibility.Collapsed;
        var existing=(await Task.Run(store!.Playlists)).Select(p=>p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name=PromptPlaylistName(existing);
        if(name is null)return;
        await Task.Run(()=>store!.CreatePlaylist(name));await ReloadPlaylists();SetPlaylistHomeFeedback("歌单已创建，可在下方打开。");
    });
    private string? PromptPlaylistName(IReadOnlySet<string> existingNames)
    {
        var dialog=new Window{Title="创建歌单",Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=System.Windows.Media.Brushes.Transparent,
            ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Width=420,Height=250};
        var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text="创建歌单",Foreground=System.Windows.Media.Brushes.White,
            FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,7)});
        panel.Children.Add(new TextBlock{Text="给你的歌单取一个名字",Foreground=(Brush)FindResource("Muted"),
            FontSize=14,Margin=new Thickness(0,0,0,14)});
        var nameBox=new TextBox{MaxLength=80,Style=(Style)FindResource(typeof(TextBox))};
        System.Windows.Automation.AutomationProperties.SetName(nameBox,"新歌单名称");
        panel.Children.Add(nameBox);
        var error=new TextBlock{Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF,0xB9,0xB9)),
            FontSize=13,Height=20,Visibility=Visibility.Hidden,Margin=new Thickness(2,7,0,10)};
        panel.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="取消",Width=88,IsCancel=true,Margin=new Thickness(0,0,8,0),Style=(Style)FindResource(typeof(Button))};
        var confirm=new Button{Content="创建",Width=96,IsDefault=true,Style=(Style)FindResource("PrimaryButton")};
        string? result=null;
        confirm.Click+=(_,_)=>
        {
            var name=nameBox.Text.Trim();
            if(name.Length==0){error.Text="请输入歌单名称。";error.Visibility=Visibility.Visible;return;}
            if(existingNames.Contains(name)){error.Text="这个歌单名称已经存在。";error.Visibility=Visibility.Visible;return;}
            result=name;dialog.DialogResult=true;
        };
        nameBox.TextChanged+=(_,_)=>error.Visibility=Visibility.Hidden;
        buttons.Children.Add(cancel);buttons.Children.Add(confirm);panel.Children.Add(buttons);
        dialog.Content=new Border{CornerRadius=new CornerRadius(22),Padding=new Thickness(24),BorderThickness=new Thickness(1),
            BorderBrush=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x62,0xFF,0xFF,0xFF)),
            Background=new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromArgb(0xF0,0x3B,0x46,0x52),System.Windows.Media.Color.FromArgb(0xF5,0x1B,0x21,0x29),45),
            Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=System.Windows.Media.Colors.Black,BlurRadius=28,ShadowDepth=8,Opacity=0.45},Child=panel};
        dialog.Loaded+=(_,_)=>nameBox.Focus();
        return dialog.ShowDialog()==true?result:null;
    }
    private void SetPlaylistHomeFeedback(string message)
    {
        LibraryStatus.Text=message;
        PlaylistHomeFeedback.Text=message;
        PlaylistHomeFeedback.Visibility=Visibility.Visible;
    }
    private void BeginRenamePlaylist(object sender,RoutedEventArgs e)
    {
        if(activePlaylist is null || PlaylistList.SelectedItem is not Playlist selected)return;
        RenamePlaylistName.Text=selected.Name;
        RenameEditor.Visibility=Visibility.Visible;
        RenamePlaylistName.Focus();RenamePlaylistName.SelectAll();
    }
    private void CancelRenamePlaylist(object sender,RoutedEventArgs e)=>RenameEditor.Visibility=Visibility.Collapsed;
    private async void RenamePlaylist(object sender,RoutedEventArgs e)=>await Guard(async()=>
    {
        if(activePlaylist is not long id)return;
        var name=RenamePlaylistName.Text.Trim();
        if(name.Length==0){LibraryStatus.Text="请先输入新名称。";return;}
        if((await Task.Run(store!.Playlists)).Any(p=>p.Id!=id && p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))
        {LibraryStatus.Text="这个歌单名称已经存在。";return;}
        await Task.Run(()=>store!.RenamePlaylist(id,name));
        await ReloadPlaylists();RenameEditor.Visibility=Visibility.Collapsed;
        ViewTitle.Text=name;LibraryStatus.Text="歌单已重命名。";
    });
    private async void DeletePlaylist(object sender,RoutedEventArgs e)
    {
        if(activePlaylist is not long id || PlaylistList.SelectedItem is not Playlist selected || selected.Id!=id)return;
        if(!ConfirmGlass($"确定删除歌单「{selected.Name}」吗？","歌曲仍保留在音乐库，原始音频文件不会删除。"))return;
        await Guard(async()=>
        {
            await Task.Run(()=>store!.DeletePlaylist(id));
            activePlaylist=null;await ReloadPlaylists();await SwitchView(null,false);
            LibraryStatus.Text="歌单已删除，歌曲和原文件保留。";
        });
    }
    private bool ConfirmGlass(string title,string message)
    {
        var dialog=new Window{Title=title,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,WindowStyle=WindowStyle.None,
            AllowsTransparency=true,Background=System.Windows.Media.Brushes.Transparent,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Width=430,Height=225};
        var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text=title,Foreground=System.Windows.Media.Brushes.White,FontSize=19,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        panel.Children.Add(new TextBlock{Text=message,Foreground=(Brush)FindResource("Muted"),FontSize=14,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,24)});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="取消",Width=92,IsCancel=true,Margin=new Thickness(0,0,8,0),Style=(Style)FindResource(typeof(Button))};
        var confirm=new Button{Content="删除歌单",Width=112,IsDefault=true,Style=(Style)FindResource("PrimaryButton"),Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xA0,0xA2,0x57,0x60)),BorderBrush=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80,0xFF,0xCF,0xCF))};
        confirm.Click+=(_,_)=>dialog.DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(confirm);panel.Children.Add(buttons);
        dialog.Content=new Border{CornerRadius=new CornerRadius(22),Padding=new Thickness(24),BorderThickness=new Thickness(1),BorderBrush=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x62,0xFF,0xFF,0xFF)),
            Background=new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromArgb(0xF0,0x3B,0x46,0x52),System.Windows.Media.Color.FromArgb(0xF5,0x1B,0x21,0x29),45),
            Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=System.Windows.Media.Colors.Black,BlurRadius=28,ShadowDepth=8,Opacity=0.45},Child=panel};
        return dialog.ShowDialog()==true;
    }
    private async void FavoriteSelected(object sender,RoutedEventArgs e)=>await Guard(async()=>
    {
        var rows=Songs.SelectedItems.Cast<LibraryTrack>().ToArray();
        if(rows.Length==0){LibraryStatus.Text="请先选中歌曲。";return;}
        var favorite=rows.Any(t=>!t.Favorite);
        await Task.Run(()=>{foreach(var row in rows)store!.SetFavorite(row.Id,favorite);});
        await Reload();LibraryStatus.Text=favorite?"已加入收藏。":"已取消收藏。";
    });
    private async void AddToPlaylist(object sender,RoutedEventArgs e)=>await UpdatePlaylist(true);
    private async void RemoveFromPlaylist(object sender,RoutedEventArgs e)=>await UpdatePlaylist(false);
    private Task UpdatePlaylist(bool add)=>Guard(async()=>
    {
        var playlist=add?(PlaylistTarget.SelectedItem as Playlist)?.Id:activePlaylist;
        var ids=Songs.SelectedItems.Cast<LibraryTrack>().Select(t=>t.Id).ToArray();
        if(playlist is null || ids.Length==0){LibraryStatus.Text="请选择歌曲和目标歌单。";return;}
        await Task.Run(()=>store!.ChangePlaylistTracks(playlist.Value,ids,add));await Reload();await ReloadPlaylists();
        LibraryStatus.Text=add?"歌曲已加入歌单。":"歌曲已移出歌单，原文件保留。";
    });
    private void PlaySelected(object sender,RoutedEventArgs e)=>StartSelection();
    private void SelectTrack(object sender,MouseButtonEventArgs e)
    {if(ItemsControl.ContainerFromElement(Songs,e.OriginalSource as DependencyObject) is ListBoxItem)StartSelection();}
    private void SongKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){StartSelection();e.Handled=true;}}
    private void StartSelection()
    {
        if(Songs.SelectedItem is not LibraryTrack selected || view is null)return;
        playbackQueue=view.Cast<LibraryTrack>().ToList();shuffleOrder.Reset();UpdateQueue();
        Play(playbackQueue.FindIndex(t=>t.Id==selected.Id));
        OpenNowPlaying(this,new RoutedEventArgs());
    }
    private void UpdateQueue()
    {
        QueueList.ItemsSource=null;
        QueueList.ItemsSource=playbackQueue;
        QueueList.SelectedIndex=index;
        QueueCount.Text=$"{playbackQueue.Count} 首歌曲";
        _=UpdateNextPreview();
    }
    private int ResolveAdjacentTarget(int direction)
    {
        if(playbackQueue.Count==0 || index<0)return -1;
        var target=index+direction;
        if(target>=0 && target<playbackQueue.Count)return target;
        return playbackMode==PlaybackMode.RepeatPlaylist?direction>0?0:playbackQueue.Count-1:-1;
    }
    private async Task<BitmapSource?> LoadCoverBitmap(long id,int width)
    {
        if(store is null)return null;
        try
        {
            var bytes=await Task.Run(()=>store.Cover(id));
            if(bytes is not {Length:>0})return null;
            using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth=width;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
        }
        catch{return null;}
    }
    private async Task ShowCover(long id,bool animateEntrance=true)
    {
        var request=++playerArtworkRequest;
        var bitmap=await LoadCoverBitmap(id,600);
        if(closing || request!=playerArtworkRequest || index<0 || playbackQueue[index].Id!=id)return;
        Artwork.Source=bitmap;
        if(animateEntrance){HeroArtwork.Source=bitmap;if(bitmap is not null)AnimateHeroArtwork();}
        else SetHeroArtworkWithoutEntrance(bitmap);
        if(bitmap is null)ResetCoverTint();else ApplyCoverTint(bitmap);
    }
    private async Task UpdateNextPreview()
    {
        var request=++playerNextPreviewRequest;
        var target=ResolveAdjacentTarget(1);
        if(target<0)
        {
            NextTrackTitle.Text="队列中没有下一首";NextTrackArtist.Text="";NextPreviewArtwork.Source=null;return;
        }
        var track=playbackQueue[target];
        NextTrackTitle.Text=track.Title;NextTrackArtist.Text=track.Artist;
        var bitmap=await LoadCoverBitmap(track.Id,96);
        if(!closing && request==playerNextPreviewRequest && target<playbackQueue.Count && playbackQueue[target].Id==track.Id)NextPreviewArtwork.Source=bitmap;
    }
    private async Task PrepareNeighbor(int target,int direction)
    {
        if(target<0 || target>=playbackQueue.Count)return;
        playerNeighborIndex=target;playerNeighborDirection=direction;
        var track=playbackQueue[target];
        NeighborTitle.Text=track.Title;NeighborArtist.Text=track.Artist;NeighborAlbum.Text=track.Album;
        NeighborWaveform.SetTrack(track.Id);NeighborWaveform.SetProgress(0,Math.Max(1,track.DurationMs));
        NeighborArtwork.Source=null;NeighborStage.Visibility=Visibility.Visible;
        NeighborStageTranslate.X=direction*PlayerPages.ActualWidth+playerGestureOffset;
        var bitmap=await LoadCoverBitmap(track.Id,600);
        if(!closing && playerNeighborIndex==target)NeighborArtwork.Source=bitmap;
    }
    private async void BeginPlayerSwitch(int direction)
    {
        if(playerTransitioning || playerSwitchPreparing)return;
        var target=direction>0?
            QueueNavigator.Next(playbackQueue.Count,index,playbackMode,shuffleEnabled,false,shuffleOrder):
            QueueNavigator.Previous(playbackQueue.Count,index,shuffleEnabled,shuffleOrder);
        if(target<0 || target>=playbackQueue.Count)return;
        if(NowPlayingPanel.Visibility!=Visibility.Visible){Play(target);return;}
        var sourceIndex=index;
        playerSwitchPreparing=true;
        try{await PrepareNeighbor(target,direction);}
        finally{playerSwitchPreparing=false;}
        if(closing || index!=sourceIndex || NowPlayingPanel.Visibility!=Visibility.Visible)
        {NeighborStage.Visibility=Visibility.Collapsed;return;}
        AnimatePlayerPages(target,direction,0);
    }
    private void AnimatePlayerPages(int target,int direction,double fromOffset)
    {
        if(playerTransitioning)return;
        playerTransitioning=true;
        var commit=target>=0;
        var width=Math.Max(280,PlayerPages.ActualWidth);
        var destination=commit?-direction*width:0;
        var neighborDestination=commit?0:direction*width;
        var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
        var duration=TimeSpan.FromMilliseconds(commit?260:210);
        var currentAnimation=new DoubleAnimation(fromOffset,destination,duration){EasingFunction=ease};
        var neighborFrom=direction*width+fromOffset;
        var neighborAnimation=new DoubleAnimation(neighborFrom,neighborDestination,duration){EasingFunction=ease};
        currentAnimation.Completed+=(_,_)=>
        {
            PlayerStageTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,null);
            NeighborStageTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,null);
            if(commit)SetHeroArtworkWithoutEntrance(NeighborArtwork.Source as BitmapSource);
            PlayerStageTranslate.X=0;NeighborStageTranslate.X=0;NeighborStage.Visibility=Visibility.Collapsed;
            var selected=target;playerNeighborIndex=-1;playerNeighborDirection=0;playerGestureOffset=0;playerGestureHorizontal=false;playerTransitioning=false;
            if(commit)Play(selected,0,false);
        };
        PlayerStageTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,currentAnimation);
        if(NeighborStage.Visibility==Visibility.Visible)NeighborStageTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,neighborAnimation);
    }
    private void ResetCoverTint()=>PlayerGlow.Fill=new System.Windows.Media.RadialGradientBrush(System.Windows.Media.Color.FromArgb(0x62,0x5A,0x8A,0x91),System.Windows.Media.Color.FromArgb(0,0x5A,0x8A,0x91));
    private void ApplyCoverTint(BitmapSource bitmap)
    {
        try
        {
            var converted=new FormatConvertedBitmap(bitmap,System.Windows.Media.PixelFormats.Bgra32,null,0);
            var stride=converted.PixelWidth*4;var pixels=new byte[stride*converted.PixelHeight];converted.CopyPixels(pixels,stride,0);
            long red=0,green=0,blue=0,count=0;var stepX=Math.Max(1,converted.PixelWidth/28);var stepY=Math.Max(1,converted.PixelHeight/28);
            for(var y=0;y<converted.PixelHeight;y+=stepY)for(var x=0;x<converted.PixelWidth;x+=stepX)
            {
                var offset=y*stride+x*4;if(pixels[offset+3]<80)continue;
                blue+=pixels[offset];green+=pixels[offset+1];red+=pixels[offset+2];count++;
            }
            if(count==0){ResetCoverTint();return;}
            var r=(byte)Math.Clamp(red/count,0,255);var g=(byte)Math.Clamp(green/count,0,255);var b=(byte)Math.Clamp(blue/count,0,255);
            PlayerGlow.Fill=new System.Windows.Media.RadialGradientBrush
            {
                Center=new Point(0.42,0.38),GradientOrigin=new Point(0.36,0.32),RadiusX=0.78,RadiusY=0.78,
                GradientStops={new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0x70,r,g,b),0),new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0x00,r,g,b),1)}
            };
        }
        catch{ResetCoverTint();}
    }
    private void CoverArtworkSizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(sender is not Image image || image.ActualWidth<=0 || image.ActualHeight<=0)return;
        image.Clip=new System.Windows.Media.RectangleGeometry(new Rect(0,0,image.ActualWidth,image.ActualHeight),22,22);
    }
    private void AnimateHeroArtwork()
    {
        HeroArtwork.Opacity=0;HeroArtwork.RenderTransformOrigin=new Point(0.5,0.5);
        var scale=new System.Windows.Media.ScaleTransform(0.92,0.92);HeroArtwork.RenderTransform=scale;
        var ease=new QuadraticEase{EasingMode=EasingMode.EaseOut};
        HeroArtwork.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(240)){EasingFunction=ease});
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,new DoubleAnimation(0.92,1,TimeSpan.FromMilliseconds(280)){EasingFunction=ease});
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,new DoubleAnimation(0.92,1,TimeSpan.FromMilliseconds(280)){EasingFunction=ease});
    }
    private void SetHeroArtworkWithoutEntrance(BitmapSource? bitmap)
    {
        HeroArtwork.BeginAnimation(UIElement.OpacityProperty,null);
        HeroArtwork.RenderTransform=System.Windows.Media.Transform.Identity;
        HeroArtwork.Opacity=1;
        HeroArtwork.Source=bitmap;
    }
    private void ArtworkSizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(Artwork.ActualWidth<=0 || Artwork.ActualHeight<=0)return;
        Artwork.Clip=new System.Windows.Media.RectangleGeometry(new Rect(0,0,Artwork.ActualWidth,Artwork.ActualHeight),14,14);
    }
    private async void TrackArtworkLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not Image image || image.DataContext is not LibraryTrack track || store is null)return;
        image.Tag=track.Id;
        if(artworkCache.TryGetValue(track.Id,out var cached)){image.Source=cached;return;}
        byte[]? bytes;
        try{bytes=await Task.Run(()=>store.Cover(track.Id));}
        catch{return;}
        if(closing || image.Tag is not long id || id!=track.Id || image.DataContext is not LibraryTrack current || current.Id!=id)return;
        BitmapSource? bitmap=null;
        if(bytes is {Length:>0})
        {
            try
            {
                using var stream=new MemoryStream(bytes);var decoded=new BitmapImage();decoded.BeginInit();decoded.CacheOption=BitmapCacheOption.OnLoad;decoded.DecodePixelWidth=72;decoded.StreamSource=stream;decoded.EndInit();decoded.Freeze();bitmap=decoded;
            }
            catch{}
        }
        artworkCache[id]=bitmap;artworkCacheOrder.Enqueue(id);
        while(artworkCacheOrder.Count>128){var old=artworkCacheOrder.Dequeue();artworkCache.Remove(old);}
        image.Source=bitmap;
    }
    private void TrackArtworkUnloaded(object sender,RoutedEventArgs e)
    {if(sender is Image image){image.Tag=null;image.Source=null;}}
    private void Play(int target,long position=0,bool animateArtwork=true)
    {
        if(player is null || engine is null || target<0 || target>=playbackQueue.Count)return;
        try
        {
            player.Stop();index=target;QueueList.SelectedIndex=index;restored=false;rememberedPosition=position;pendingSeek=position>0?position:null;
            seeking=false;Seek.Value=0;
            var track=playbackQueue[index];
            SetHeroTrack(track);
            _=ShowLyrics(track);
            if(!File.Exists(track.Path)){Status.Text=track.Title;MiniState.Text="文件不可用";return;}
            using var media=new Media(engine,new Uri(track.Path));
            Status.Text=track.Title;
            MiniState.Text=player.Play(media)?"正在播放":"无法开始播放";
            appliedPlaybackSpeed=targetPlaybackSpeed=playbackSpeed;
            speedRampTimer.Stop();player.SetRate((float)playbackSpeed);
            _=ShowCover(track.Id,animateArtwork);
        }
        catch(Exception ex){Status.Text="播放失败";MiniState.Text=ex.Message;}
    }
    private void Toggle(object sender,RoutedEventArgs e)
    {
        if(player is null)return;
        if(index<0){StartSelection();return;}
        if(player.IsPlaying){player.SetPause(true);MiniState.Text="已暂停";}
        else if(player.State==VLCState.Paused){player.SetPause(false);MiniState.Text="正在播放";}
        else Play(index,restored?rememberedPosition:0,false);
        UpdatePlayerPlayButton();
    }
    private void Previous(object sender,RoutedEventArgs e)=>BeginPlayerSwitch(-1);
    private void Next(object sender,RoutedEventArgs e)=>BeginPlayerSwitch(1);
    private bool PlayNext(bool automatic)
    {
        var target=QueueNavigator.Next(playbackQueue.Count,index,playbackMode,shuffleEnabled,automatic,shuffleOrder);
        if(target<0 || target>=playbackQueue.Count)return false;
        Play(target,0,NowPlayingPanel.Visibility!=Visibility.Visible);return true;
    }
    private void ToggleShuffle(object sender,RoutedEventArgs e)
    {
        shuffleEnabled=!shuffleEnabled;shuffleOrder.Reset();UpdateShuffleButton();
    }
    private void UpdateShuffleButton()
    {
        var label=shuffleEnabled?"随机：开":"随机：关";
        ShuffleButton.Content=label;
    }
    private void TogglePlaybackMode(object sender,RoutedEventArgs e)
    {
        playbackMode=playbackMode switch
        {
            PlaybackMode.RepeatOff=>PlaybackMode.RepeatPlaylist,
            PlaybackMode.RepeatPlaylist=>PlaybackMode.RepeatTrack,
            PlaybackMode.RepeatTrack=>PlaybackMode.StopAfterCurrent,
            _=>PlaybackMode.RepeatOff
        };
        UpdatePlaybackModeButton();
    }
    private void UpdatePlaybackModeButton()
    {
        var label=playbackMode switch
        {
            PlaybackMode.RepeatPlaylist=>"列表循环",
            PlaybackMode.RepeatTrack=>"单曲循环",
            PlaybackMode.StopAfterCurrent=>"播完本曲停止",
            _=>"顺序播放"
        };
        PlaybackModeButton.Content=label;_=UpdateNextPreview();
    }
    private void VolumeChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(player is not null)player.Volume=(int)e.NewValue;}
    private void BeginSeek(object sender,MouseButtonEventArgs e)=>seeking=true;
    private void EndSeek(object sender,MouseButtonEventArgs e){ApplySeek();seeking=false;}
    private void SeekLostCapture(object sender,MouseEventArgs e){if(seeking){ApplySeek();seeking=false;}}
    private void KeySeek(object sender,KeyEventArgs e)=>ApplySeek();
    private void ApplySeek()
    {
        if(restored){rememberedPosition=(long)Seek.Value;return;}
        if(player?.IsSeekable==true){player.Time=(long)Seek.Value;rememberedPosition=(long)Seek.Value;}
    }
    private void SeekBySeconds(int seconds)
    {
        if(player is null || index<0 || index>=playbackQueue.Count || !restored && !player.IsSeekable)return;
        var length=restored?playbackQueue[index].DurationMs:player.Length;
        if(length<=0)length=playbackQueue[index].DurationMs;
        if(length<=0)return;
        var current=restored?rememberedPosition:Math.Max(0,player.Time);
        var target=Math.Clamp(current+seconds*1000L,0,Math.Max(0,length-500));
        Seek.Maximum=Math.Max(1,length);
        Seek.Value=target;
        ApplySeek();Refresh();
    }
    private void PlayerWaveformSeekRequested(object? sender,double fraction)
    {
        Seek.Value=Math.Clamp(fraction,0,1)*Math.Max(1,Seek.Maximum);ApplySeek();Refresh();
    }
    private void UpdatePlayerPlayButton()
    {
        if(PlayerPlayButton is null)return;
        var playing=player?.IsPlaying==true;
        PlayerPlayButton.Content=playing?"暂停":"播放";
        PlayerPlayButton.ToolTip=playing?"暂停":"播放";
        MiniPlayButton.Content=playing?"暂停":"播放";
        MiniPlayButton.ToolTip=playing?"暂停":"播放";
    }
    private void Refresh()
    {
        if(player is null)return;
        if(pendingSeek is long target && player.IsSeekable && player.Length>0)
        {player.Time=Math.Min(target,Math.Max(0,player.Length-500));pendingSeek=null;}
        var length=restored && index>=0?playbackQueue[index].DurationMs:player.Length;
        var time=restored?rememberedPosition:Math.Max(0,player.Time);
        Seek.Maximum=Math.Max(1,length);
        if(!seeking)Seek.Value=time;
        TimeLabel.Text=$"{Format(time)} / {Format(length)}";
        PlayerWaveform.SetProgress(time,length);
        MiniWaveform.SetProgress(time,length);
        PlayerCurrentTime.Text=FormatShort(time);PlayerDuration.Text=FormatShort(length);
        UpdatePlayerPlayButton();
        UpdateLyricPosition(time);
    }
    private void OpenNowPlaying(object sender,RoutedEventArgs e)
    {
        navigation.Navigate(DesktopPage.Player);
        ShowLyricsPane(sender,e);
        CenterActiveLyric(LyricList);
    }
    private void OpenQueue(object sender,RoutedEventArgs e)
    {
        navigation.Navigate(DesktopPage.Player);
        LyricsPane.Visibility=Visibility.Visible;
        var opening=QueuePane.Visibility!=Visibility.Visible;
        QueuePane.Visibility=Visibility.Visible;
        if(opening)
        {
            var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
            QueuePane.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(230)){EasingFunction=ease});
            QueuePaneTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,new DoubleAnimation(48,0,TimeSpan.FromMilliseconds(260)){EasingFunction=ease});
        }
        QueueList.SelectedIndex=index;
        if(index>=0 && index<playbackQueue.Count)QueueList.ScrollIntoView(playbackQueue[index]);
    }
    private void ShowLyricsPane(object sender,RoutedEventArgs e)
    {
        QueuePane.Visibility=Visibility.Collapsed;LyricsPane.Visibility=Visibility.Visible;
    }
    private void PlayQueueItem(object sender,MouseButtonEventArgs e)
    {
        if((e.OriginalSource as FrameworkElement)?.DataContext is LibraryTrack track)PlayQueueTrack(track);
    }
    private void QueueKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Enter || QueueList.SelectedItem is not LibraryTrack track)return;
        PlayQueueTrack(track);e.Handled=true;
    }
    private void PlayQueueSelected(object sender,RoutedEventArgs e)
    {
        if(QueueList.SelectedItem is LibraryTrack track)PlayQueueTrack(track);
    }
    private void PlayQueueTrack(LibraryTrack track)
    {
        var target=playbackQueue.FindIndex(t=>t.Id==track.Id);
        if(target<0)return;
        shuffleOrder.Reset();Play(target,0,NowPlayingPanel.Visibility!=Visibility.Visible);
    }
    private void MoveQueueUp(object sender,RoutedEventArgs e)=>MoveSelectedQueueTrack(-1);
    private void MoveQueueDown(object sender,RoutedEventArgs e)=>MoveSelectedQueueTrack(1);
    private void MoveSelectedQueueTrack(int direction)
    {
        if(QueueList.SelectedItem is not LibraryTrack selected)return;
        var from=playbackQueue.FindIndex(t=>t.Id==selected.Id);
        MoveQueueTrack(from,from+direction);
    }
    private void MoveQueueTrack(int from,int to)
    {
        if(from<0 || from>=playbackQueue.Count || to<0 || to>=playbackQueue.Count || from==to)return;
        var moved=playbackQueue[from];
        index=QueueOrder.Move(playbackQueue,from,to,index);
        shuffleOrder.Reset();UpdateQueue();
        QueueList.SelectedItem=moved;
        QueueList.ScrollIntoView(moved);
    }
    private void QueueDragStart(object sender,MouseButtonEventArgs e)
    {
        queueDragTrack=(ItemsControl.ContainerFromElement(QueueList,e.OriginalSource as DependencyObject) as ListBoxItem)?.Content as LibraryTrack;
        queueDragOrigin=queueDragTrack is null?null:e.GetPosition(QueueList);
    }
    private void QueueDragEnd(object sender,MouseButtonEventArgs e)
    {
        queueDragTrack=null;queueDragOrigin=null;
    }
    private void QueueDragMove(object sender,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed || queueDragTrack is null || queueDragOrigin is not Point start)return;
        var position=e.GetPosition(QueueList);
        if(Math.Abs(position.X-start.X)<SystemParameters.MinimumHorizontalDragDistance &&
           Math.Abs(position.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        var track=queueDragTrack;queueDragTrack=null;queueDragOrigin=null;
        DragDrop.DoDragDrop(QueueList,track,DragDropEffects.Move);
    }
    private void QueueDragOver(object sender,DragEventArgs e)
    {
        e.Effects=e.Data.GetData(typeof(LibraryTrack)) is LibraryTrack track &&
            playbackQueue.Any(t=>t.Id==track.Id)?DragDropEffects.Move:DragDropEffects.None;
        e.Handled=true;
    }
    private void QueueDrop(object sender,DragEventArgs e)
    {
        if(e.Data.GetData(typeof(LibraryTrack)) is not LibraryTrack track)return;
        var from=playbackQueue.FindIndex(t=>t.Id==track.Id);
        var target=(ItemsControl.ContainerFromElement(QueueList,e.OriginalSource as DependencyObject) as ListBoxItem)?.Content as LibraryTrack;
        var to=target is null?playbackQueue.Count-1:playbackQueue.FindIndex(t=>t.Id==target.Id);
        MoveQueueTrack(from,to);e.Handled=true;
    }
    private void CloseNowPlaying(object sender,RoutedEventArgs e)
    {
        navigation.Navigate(DesktopPage.Library);
    }
    private void SetHeroTrack(LibraryTrack track)
    {
        HeroTitle.Text=track.Title;
        HeroArtist.Text=track.Artist;
        HeroAlbum.Text=track.Album;
        PlayerWaveform.SetTrack(track.Id);PlayerWaveform.SetProgress(0,Math.Max(1,track.DurationMs));
        MiniWaveform.SetTrack(track.Id);MiniWaveform.SetProgress(0,Math.Max(1,track.DurationMs));
        PlayerCurrentTime.Text="00:00";PlayerDuration.Text=FormatShort(track.DurationMs);
        FullscreenSongTitle.Text=track.Title;FullscreenSongArtist.Text=track.Artist;
        _=UpdateNextPreview();
    }
    private async Task ShowLyrics(LibraryTrack track)
    {
        StopLyricScrollAnimation();
        lyricCenterRequest++;
        var request=++lyricRequest;
        lyricLines=[];activeLyric=-1;browsingLyrics=false;
        changingLyricSelection=true;LyricList.ItemsSource=null;FullscreenLyricList.ItemsSource=null;LyricList.SelectedIndex=-1;FullscreenLyricList.SelectedIndex=-1;changingLyricSelection=false;
        FullscreenLyricList.SelectedIndex=-1;LyricSource.Text="正在读取歌词…";PlayerLyricPreview.Text="正在读取歌词…";
        LyricsPlaceholder.Text="还没有歌词。可点击「导入 LRC」添加。";
        LyricsPlaceholder.Visibility=Visibility.Visible;FullscreenLyricsPlaceholder.Visibility=Visibility.Visible;
        try
        {
            var result=await Task.Run(()=>lyricsRepository.Load(track.Path,track.Title,track.Artist,track.Id));
            if(closing || request!=lyricRequest || index<0 || playbackQueue[index].Id!=track.Id)return;
            lyricLines=result.Lines;
            LyricList.ItemsSource=lyricLines;
            FullscreenLyricList.ItemsSource=lyricLines;
            LyricSource.Text=result.Source+(lyricLines.Count>0 && lyricLines[0].TimeMs is null?" · 无时间轴":"");
            LyricsPlaceholder.Visibility=lyricLines.Count==0?Visibility.Visible:Visibility.Collapsed;
            FullscreenLyricsPlaceholder.Visibility=lyricLines.Count==0?Visibility.Visible:Visibility.Collapsed;
            if(lyricLines.Count==0)PlayerLyricPreview.Text="暂无歌词，点击打开歌词页";
            UpdateLyricPosition(restored?rememberedPosition:Math.Max(0,player?.Time??0));
        }
        catch(Exception ex)
        {
            if(!closing && request==lyricRequest)
            {LyricSource.Text="歌词读取失败";PlayerLyricPreview.Text="歌词读取失败，点击查看";LyricsPlaceholder.Text=ex.Message;LyricsPlaceholder.Visibility=Visibility.Visible;FullscreenLyricsPlaceholder.Text=ex.Message;FullscreenLyricsPlaceholder.Visibility=Visibility.Visible;}
        }
    }
    private void UpdateLyricPosition(long time)
    {
        var next=LrcParser.ActiveIndex(lyricLines,time);
        var resumeFollow=browsingLyrics && DateTime.UtcNow>=lyricBrowseUntil;
        if(resumeFollow)browsingLyrics=false;
        if(next==activeLyric)
        {
            SetLyricSelection(next);
            if(next>=0)PlayerLyricPreview.Text=lyricLines[next].Text;
            if(resumeFollow)CenterVisibleLyric();
            return;
        }
        activeLyric=next;
        if(next<0){if(lyricLines.Count>0)PlayerLyricPreview.Text=lyricLines[0].Text;return;}
        PlayerLyricPreview.Text=lyricLines[next].Text;
        SetLyricSelection(next);
        if(!browsingLyrics)CenterVisibleLyric();
    }
    private void LyricSelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(changingLyricSelection || index<0)return;
        var selected=(sender as ListBox)?.SelectedItem as LyricLine;
        if(selected is null)return;
        var selectedIndex=(sender as ListBox)?.SelectedIndex ?? -1;
        changingLyricSelection=true;
        if(sender==FullscreenLyricList)LyricList.SelectedIndex=selectedIndex;else FullscreenLyricList.SelectedIndex=selectedIndex;
        changingLyricSelection=false;
        if(selected is not LyricLine line || line.TimeMs is not long time)return;
        if(restored){rememberedPosition=time;Refresh();return;}
        if(player?.IsSeekable==true)
        {
            player.Time=time;rememberedPosition=time;
            activeLyric=-1;UpdateLyricPosition(time);
        }
    }
    private void SetLyricSelection(int selectedIndex)
    {changingLyricSelection=true;LyricList.SelectedIndex=selectedIndex;FullscreenLyricList.SelectedIndex=selectedIndex;changingLyricSelection=false;}
    private void LyricsUserScroll(object sender,MouseWheelEventArgs e)
    {PauseLyricFollow();}
    private void LyricsPointerDown(object sender,MouseButtonEventArgs e)
    {
        for(var source=e.OriginalSource as DependencyObject;source is not null && source!=sender;source=source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(source)
            : LogicalTreeHelper.GetParent(source))
            if(source is ScrollBar){PauseLyricFollow();return;}
    }
    private void PauseLyricFollow()
    {
        StopLyricScrollAnimation();
        lyricCenterRequest++;
        browsingLyrics=true;
        lyricBrowseUntil=DateTime.UtcNow.AddSeconds(4);
    }
    private void FollowLyrics(object sender,RoutedEventArgs e)
    {
        browsingLyrics=false;
        CenterVisibleLyric();
    }
    private void CenterVisibleLyric()
    {
        if(FullscreenLyricsPanel.Visibility==Visibility.Visible)CenterActiveLyric(FullscreenLyricList);
        else if(NowPlayingPanel.Visibility==Visibility.Visible)CenterActiveLyric(LyricList);
    }
    private void LyricListSizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(!browsingLyrics && sender is ListBox list)CenterActiveLyric(list);
    }
    private void CenterActiveLyric(ListBox list)
    {
        if(activeLyric<0 || activeLyric>=list.Items.Count || list.Visibility!=Visibility.Visible)return;
        var request=++lyricCenterRequest;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,() =>
        {
            if(request!=lyricCenterRequest || browsingLyrics || activeLyric<0 || activeLyric>=list.Items.Count || !list.IsVisible)return;
            list.ApplyTemplate();list.UpdateLayout();
            var scroll=FindVisualChild<ScrollViewer>(list);
            if(scroll is null || scroll.ViewportHeight<=0)return;
            var presenter=FindVisualChild<ItemsPresenter>(list);
            presenter?.ApplyTemplate();
            if(presenter is not null && FindVisualChild<Panel>(presenter) is { } panel)
            {
                var edgeSpace=Math.Ceiling(scroll.ViewportHeight/2);
                if(Math.Abs(panel.Margin.Top-edgeSpace)>1)
                {
                    panel.Margin=new Thickness(0,edgeSpace,0,edgeSpace);
                    list.UpdateLayout();
                }
            }
            if(list.ItemContainerGenerator.ContainerFromIndex(activeLyric) is not FrameworkElement line)return;
            var center=line.TranslatePoint(new Point(0,line.ActualHeight/2),scroll).Y;
            var target=Math.Clamp(scroll.VerticalOffset+center-scroll.ViewportHeight/2,0,scroll.ScrollableHeight);
            StartLyricScrollAnimation(scroll,target);
        });
    }
    private void StartLyricScrollAnimation(ScrollViewer scroll,double target)
    {
        StopLyricScrollAnimation();
        var distance=Math.Abs(target-scroll.VerticalOffset);
        if(distance<1){scroll.ScrollToVerticalOffset(target);return;}
        animatedLyricScroll=scroll;
        lyricScrollFrom=scroll.VerticalOffset;
        lyricScrollTo=target;
        lyricScrollDurationMs=Math.Clamp(260+distance*0.11,300,600);
        lyricScrollStarted=Stopwatch.GetTimestamp();
        System.Windows.Media.CompositionTarget.Rendering+=AnimateLyricScroll;
    }
    private void AnimateLyricScroll(object? sender,EventArgs e)
    {
        if(animatedLyricScroll is not { } scroll || browsingLyrics || !scroll.IsVisible)
        {StopLyricScrollAnimation();return;}
        var progress=Math.Clamp(Stopwatch.GetElapsedTime(lyricScrollStarted).TotalMilliseconds/lyricScrollDurationMs,0,1);
        // Smoothstep keeps the movement gentle at both ends, even across distant lines.
        var eased=progress*progress*(3-2*progress);
        scroll.ScrollToVerticalOffset(lyricScrollFrom+(lyricScrollTo-lyricScrollFrom)*eased);
        if(progress>=1)StopLyricScrollAnimation();
    }
    private void StopLyricScrollAnimation()
    {
        System.Windows.Media.CompositionTarget.Rendering-=AnimateLyricScroll;
        animatedLyricScroll=null;
    }
    private static T? FindVisualChild<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);
            if(child is T match)return match;
            if(FindVisualChild<T>(child) is { } nested)return nested;
        }
        return null;
    }
    private async void ImportLyrics(object sender,RoutedEventArgs e)
    {
        if(index<0){LyricSource.Text="先播放或恢复一首歌曲，再导入歌词。";return;}
        var dialog=new OpenFileDialog{Filter="LRC 歌词|*.lrc|文本文件|*.txt"};
        if(dialog.ShowDialog(this)!=true)return;
        var track=playbackQueue[index];
        try
        {
            await Task.Run(()=>lyricsRepository.Import(track.Id,dialog.FileName));
            if(!closing && index>=0 && playbackQueue[index].Id==track.Id)await ShowLyrics(track);
        }
        catch(Exception ex){if(!closing)LyricSource.Text="导入失败："+ex.Message;}
    }
    private static string Format(long ms)=>TimeSpan.FromMilliseconds(Math.Max(0,ms)).ToString(@"hh\:mm\:ss");
    private static string FormatShort(long ms)
    {
        var value=TimeSpan.FromMilliseconds(Math.Max(0,ms));
        return value.TotalHours>=1?value.ToString(@"h\:mm\:ss"):value.ToString(@"m\:ss");
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        SavePreferences();
        try
        {
            if(ready && store is not null)
            {
                var position=restored?rememberedPosition:pendingSeek ?? Math.Max(0,player?.Time ?? 0);
                if(player?.State==VLCState.Ended)position=0;
                store.SavePlayback(new(playbackQueue.Select(t=>t.Id).ToArray(),index,position,(int)Volume.Value,shuffleEnabled,playbackMode));
            }
        }
        catch(Exception ex){MessageBox.Show(this,"播放位置未能保存："+ex.Message,"Tuno");}
        closing=true;scan?.Cancel();base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        StopLyricScrollAnimation();timer.Stop();searchTimer.Stop();speedRampTimer.Stop();speedSaveTimer.Stop();player?.Stop();player?.Dispose();engine?.Dispose();base.OnClosed(e);
    }
}
