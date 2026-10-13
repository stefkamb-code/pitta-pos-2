using System.Windows;
using System.Windows.Input;
using PittaPos.App.Services;
using PittaPos.Core.Models;

namespace PittaPos.App.Views;

/// <summary>
/// «Η ΚΑΡΤΑ ΔΕΝ ΠΕΡΑΣΕ» — μόνο με πάροχο ΑΑΔΕ, όταν το τερματικό δεν ενέκρινε την πληρωμή (βλ. FiscalService). Η απόδειξη
/// ΔΕΝ ξαναστέλνεται μόνη της στο τερματικό: ο πελάτης στέκεται μπροστά του (ή έχει φύγει) — το αποφασίζει ο ταμίας.
/// </summary>
public partial class CardDeclinedDialog : Window
{
    private bool? _answer;

    private CardDeclinedDialog(decimal amount, string reason)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        AmountText.Text = Order.FormatPrice(amount);
        ReasonText.Text = reason;
    }

    /// <summary>true = ξανά στο τερματικό, false = μετρητά, null = έκλεισε χωρίς απάντηση (ξαναρωτάει σε λίγο).</summary>
    public static bool? Ask(Window? owner, decimal amount, string reason)
    {
        var dialog = new CardDeclinedDialog(amount, reason);
        if (owner is { IsVisible: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog._answer;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        _answer = true;
        Close();
    }

    private void Cash_Click(object sender, RoutedEventArgs e)
    {
        _answer = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
