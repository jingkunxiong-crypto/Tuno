using System.Windows;
namespace Tuno.PlaybackProbe;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==3 && e.Args[0]=="--lyrics-test")
        {ShutdownMode=ShutdownMode.OnExplicitShutdown;Shutdown(await Task.Run(()=>LyricsSmokeTest.Run(e.Args[1],e.Args[2])));return;}
        if(e.Args.Length>=2 && e.Args[0]=="--library-test")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await Library.LibrarySmokeTest.Run(e.Args[1],e.Args.Skip(2).ToArray()));return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--smoke-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await PlaybackSmokeTest.Run(e.Args[1], e.Args.Skip(2).ToArray()));
            return;
        }
        var args=e.Args.ToList();string? dataDirectory=null;
        if(args.Count>=2 && args[0]=="--data-dir")
        {dataDirectory=args[1];args.RemoveRange(0,2);}
        new MainWindow(args.ToArray(),dataDirectory).Show();
    }
}
