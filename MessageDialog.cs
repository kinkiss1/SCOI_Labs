using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SCOI_Lab_1;

public sealed class MessageDialog : Window
{
    public MessageDialog(string title, string message)
    {
        Title = title;
        Width = 420;
        MinWidth = 320;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Button okButton = new()
        {
            Content = "OK",
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        okButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 360
                },
                okButton
            }
        };
    }

    public static Task ShowAsync(Window owner, string title, string message)
    {
        return new MessageDialog(title, message).ShowDialog(owner);
    }
}
