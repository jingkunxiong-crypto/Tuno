using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Tuno.PlaybackProbe.Library;

namespace Tuno.PlaybackProbe;

public sealed class TrackTitleConverter : IMultiValueConverter
{
    public object Convert(object[] values,Type targetType,object parameter,CultureInfo culture)
    {
        if(values.Length<2 || values[0] is not LibraryTrack track)return DependencyProperty.UnsetValue;
        var mode=values[1] is int selected?selected:0;
        return mode switch
        {
            1=>track.FileName,
            2 when !track.Title.Equals(track.FileName,StringComparison.OrdinalIgnoreCase)
                =>$"{track.Title} · {track.FileName}",
            _=>track.Title
        };
    }

    public object[] ConvertBack(object value,Type[] targetTypes,object parameter,CultureInfo culture)
        =>throw new NotSupportedException();
}
