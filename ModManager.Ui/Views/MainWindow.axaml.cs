using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace ModManager.Ui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        if (double.TryParse(Celsius.Text, out double C))
        {
            double F = (C * 9d / 5d) + 32;
            Fahrenheit.Text = F.ToString("0.0");

        }
        else
        {
            Celsius.Text = "0";
            Fahrenheit.Text = "0";
        }
    }

    private void TextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (double.TryParse(Celsius.Text, out double C))
        {
            double F = (C * 9d / 5d) + 32;
            Fahrenheit.Text = F.ToString("0.0");

        }
        else
        {
            Celsius.Text = "0";
            Fahrenheit.Text = "0";
        }
    }
}