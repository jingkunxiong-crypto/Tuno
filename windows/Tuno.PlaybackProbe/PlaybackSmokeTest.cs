using System.IO;
using LibVLCSharp.Shared;
namespace Tuno.PlaybackProbe;
internal static class PlaybackSmokeTest
{
    public static async Task<int> Run(string directory, string[]? additionalFiles = null)
    {
        Directory.CreateDirectory(directory);
        var log = new List<string>();
        try
        {
            var wav = Path.Combine(Path.GetFullPath(directory), "中文路径 空格 测试.wav");
            using (var writer = new BinaryWriter(File.Create(wav)))
            {
                const int rate = 44100, samples = rate * 12;
                writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
                writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(samples * 2);
                for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 1500));
            }
            Core.Initialize();
            using var engine = new LibVLC("--no-video");
            var mp3Files = new List<string>();
            foreach (var bitrate in new[] { 128, 320 })
            {
                var mp3 = Path.Combine(Path.GetFullPath(directory), $"中文 MP3 {bitrate}kbps.mp3");
                using var encoder = new MediaPlayer(engine);
                using var source = new Media(engine, new Uri(wav));
                source.AddOption($":sout=#transcode{{acodec=mp3,ab={bitrate},channels=2,samplerate=44100}}:std{{access=file,mux=raw,dst='{mp3.Replace('\\', '/')}'}}");
                source.AddOption(":no-sout-all");
                source.AddOption(":sout-keep");
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                encoder.EndReached += (_, _) => done.TrySetResult(true);
                encoder.EncounteredError += (_, _) => done.TrySetException(new Exception("MP3 encode failed"));
                if (!encoder.Play(source)) throw new Exception("Encoder rejected source");
                await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
                encoder.Stop();
                if (!File.Exists(mp3) || new FileInfo(mp3).Length < 1000) throw new Exception("Empty MP3 output");
                mp3Files.Add(mp3);
                log.Add($"PASS generated MP3 {bitrate} kbps");
            }
            mp3Files.AddRange(additionalFiles ?? []);
            using var player = new MediaPlayer(engine) { Volume = 0 };
            using(var equalizer=new Equalizer())
            {
                if(equalizer.BandCount==0 || !equalizer.SetAmp(2f,0) || !player.SetEqualizer(equalizer))throw new Exception("Equalizer initialization failed");
                if(!player.UnsetEqualizer())throw new Exception("Equalizer disable failed");
                log.Add("PASS LibVLC equalizer apply / disable");
            }
            async Task Wait(Func<bool> condition, string name)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(100);
                if (!condition()) throw new Exception(name + " failed; state=" + player.State);
                log.Add("PASS " + name);
            }
            using var media = new Media(engine, new Uri(wav));
            if (!player.Play(media)) throw new Exception("Play rejected");
            await Wait(() => player.IsPlaying && player.Time > 400, "WAV playback / Unicode path / advancing clock");
            if(player.SetRate(1.25f)!=0)throw new Exception("Playback speed change failed");
            log.Add("PASS playback rate change");player.SetRate(1.0f);
            player.SetPause(true);
            await Wait(() => player.State == VLCState.Paused, "pause");
            var paused = player.Time;
            await Task.Delay(700);
            if (Math.Abs(player.Time - paused) > 100) throw new Exception("Paused clock moved");
            log.Add("PASS paused clock stable");
            player.SetPause(false);
            await Wait(() => player.IsPlaying && player.Time > paused + 200, "resume");
            player.Time = 6000;
            await Wait(() => player.Time >= 5700 && player.Time < 9500, "seek to 6 seconds");
            player.Volume = 35;
            await Wait(() => player.Volume == 35, "volume applied");
            player.Volume = 0;
            log.Add("PASS volume control");
            player.Stop();
            using var second = new Media(engine, new Uri(wav));
            player.Play(second);
            await Wait(() => player.IsPlaying && player.Time > 200 && player.Time < 4000, "replace media / restart");
            player.Stop();
            foreach (var path in mp3Files)
            {
                using var mp3 = new Media(engine, new Uri(path));
                await mp3.Parse(MediaParseOptions.ParseLocal);
                if (!mp3.Tracks.Any(t => t.TrackType == TrackType.Audio)) throw new Exception("Missing MP3 audio track");
                player.Play(mp3);
                await Wait(() => player.IsPlaying && player.Time > 400, "MP3 playback " + Path.GetFileName(path));
                player.Time = 6000;
                await Wait(() => player.Time >= 5700 && player.Time < 9500, "MP3 seek");
                player.SetPause(true);
                await Wait(() => player.State == VLCState.Paused, "MP3 pause");
                player.SetPause(false);
                await Wait(() => player.IsPlaying, "MP3 resume");
                player.Stop();
            }
            log.Add($"PASS {mp3Files.Count} distinct MP3 files; actual listening quality not assessed");
            File.WriteAllLines(Path.Combine(directory, "smoke-result.txt"), log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("FAIL " + ex);
            File.WriteAllLines(Path.Combine(directory, "smoke-result.txt"), log);
            return 1;
        }
    }
}

