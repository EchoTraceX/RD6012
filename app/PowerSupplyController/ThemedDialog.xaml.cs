using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PowerSupplyController;

public partial class ThemedDialog : Window, INotifyPropertyChanged
{
    private string _dialogTitle = "提示";
    private string _message = string.Empty;
    private string _primaryButtonText = "确定";
    private string _secondaryButtonText = "取消";
    private Brush _accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E5E6B"));

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DialogTitle
    {
        get => _dialogTitle;
        set
        {
            _dialogTitle = value;
            OnPropertyChanged();
        }
    }

    public string Message
    {
        get => _message;
        set
        {
            _message = value;
            OnPropertyChanged();
        }
    }

    public string PrimaryButtonText
    {
        get => _primaryButtonText;
        set
        {
            _primaryButtonText = value;
            OnPropertyChanged();
        }
    }

    public string SecondaryButtonText
    {
        get => _secondaryButtonText;
        set
        {
            _secondaryButtonText = value;
            OnPropertyChanged();
        }
    }

    public Brush AccentBrush
    {
        get => _accentBrush;
        set
        {
            _accentBrush = value;
            OnPropertyChanged();
        }
    }

    public bool IsConfirmation { get; private set; }

    public ThemedDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public static void ShowFailure(Window? owner, string title, string message)
    {
        var dialog = new ThemedDialog
        {
            Owner = owner,
            DialogTitle = title,
            Message = message,
            PrimaryButtonText = "知道了",
            AccentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8D3341"))
        };
        dialog.ShowDialog();
    }

    public static void ShowSuccess(Window? owner, string title, string message)
    {
        var dialog = new ThemedDialog
        {
            Owner = owner,
            DialogTitle = title,
            Message = message,
            PrimaryButtonText = "知道了",
            AccentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4FD28E"))
        };
        dialog.ShowDialog();
    }

    public static bool ShowConfirmation(Window? owner, string title, string message, string primaryText = "确定", string secondaryText = "取消")
    {
        var dialog = new ThemedDialog
        {
            Owner = owner,
            DialogTitle = title,
            Message = message,
            PrimaryButtonText = primaryText,
            SecondaryButtonText = secondaryText,
            AccentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB347")),
            IsConfirmation = true
        };
        dialog.SecondaryButton.Visibility = Visibility.Visible;
        return dialog.ShowDialog() == true;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = IsConfirmation ? false : true;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
