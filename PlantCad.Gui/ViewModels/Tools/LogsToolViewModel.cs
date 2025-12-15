using System.Text;
using Avalonia.Threading;
using Dock.Model.Mvvm.Controls;

namespace PlantCad.Gui.ViewModels.Tools;

public class LogsToolViewModel : Tool
{
    private readonly StringBuilder _buffer = new StringBuilder();

    private string _logText = string.Empty;
    public string LogText
    {
        get => _logText;
        private set
        {
            if (_logText == value)
            {
                return;
            }
            _logText = value;
            OnPropertyChanged();
        }
    }

    public LogsToolViewModel()
    {
        Title = "Logs";
        CanClose = false;
        DockGroup = "Tools";
    }

    public void Append(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }
        void AppendInner()
        {
            _buffer.AppendLine(message);
            LogText = _buffer.ToString();
        }
        if (Dispatcher.UIThread.CheckAccess())
        {
            AppendInner();
        }
        else
        {
            Dispatcher.UIThread.Post(AppendInner);
        }
    }
}
