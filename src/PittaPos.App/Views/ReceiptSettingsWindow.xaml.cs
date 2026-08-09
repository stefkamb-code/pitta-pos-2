using System.Windows;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Ρυθμίσεις απόδειξης με ζωντανή προεπισκόπηση.</summary>
public partial class ReceiptSettingsWindow : Window
{
    private readonly SettingsStore _store = SettingsStore.Instance;
    private double _titleFontSize;
    private double _itemsFontSize;
    private double _totalFontSize;
    private double _metaFontSize;

    public ReceiptSettingsWindow()
    {
        InitializeComponent();

        var s = _store.Settings;
        _titleFontSize = s.ReceiptTitleFontSize;
        _itemsFontSize = s.ReceiptItemsFontSize;
        _totalFontSize = s.ReceiptTotalFontSize;
        _metaFontSize = s.ReceiptMetaFontSize;
        ReceiptTitleBox.Text = s.ReceiptTitle;
        ReceiptInfoBox.Text = s.ReceiptInfo;
        ReceiptFooterBox.Text = s.ReceiptFooter;
        ShowDateTimeCheck.IsChecked = s.ReceiptShowDateTime;
        ShowCustomerCheck.IsChecked = s.ReceiptShowCustomer;
        ShowDetailsCheck.IsChecked = s.ReceiptShowDetails;
        UpdatePreview();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void Receipt_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    private void DecTitleFontSize_Click(object sender, RoutedEventArgs e) { _titleFontSize = Math.Max(8, _titleFontSize - 1); UpdatePreview(); }
    private void IncTitleFontSize_Click(object sender, RoutedEventArgs e) { _titleFontSize = Math.Min(30, _titleFontSize + 1); UpdatePreview(); }
    private void DecItemsFontSize_Click(object sender, RoutedEventArgs e) { _itemsFontSize = Math.Max(8, _itemsFontSize - 1); UpdatePreview(); }
    private void IncItemsFontSize_Click(object sender, RoutedEventArgs e) { _itemsFontSize = Math.Min(24, _itemsFontSize + 1); UpdatePreview(); }
    private void DecTotalFontSize_Click(object sender, RoutedEventArgs e) { _totalFontSize = Math.Max(8, _totalFontSize - 1); UpdatePreview(); }
    private void IncTotalFontSize_Click(object sender, RoutedEventArgs e) { _totalFontSize = Math.Min(32, _totalFontSize + 1); UpdatePreview(); }
    private void DecMetaFontSize_Click(object sender, RoutedEventArgs e) { _metaFontSize = Math.Max(6, _metaFontSize - 1); UpdatePreview(); }
    private void IncMetaFontSize_Click(object sender, RoutedEventArgs e) { _metaFontSize = Math.Min(20, _metaFontSize + 1); UpdatePreview(); }

    /// <summary>Ενημερώνει ζωντανά τη μικρογραφία απόδειξης καθώς αλλάζουν τα πεδία — τέσσερις
    /// ανεξάρτητες ενότητες μεγέθους, ίδια λογική με το ReceiptWindow (βλ. εκεί).</summary>
    private void UpdatePreview()
    {
        if (PvTitle is null)
            return; // κατά την αρχικοποίηση, πριν φτιαχτούν τα στοιχεία

        PvTitle.Text = ReceiptTitleBox.Text;
        PvInfo.Text = ReceiptInfoBox.Text;
        PvInfo.Visibility = ReceiptInfoBox.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PvFooter.Text = ReceiptFooterBox.Text;
        PvFooter.Visibility = ReceiptFooterBox.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PvDateTime.Visibility = ShowDateTimeCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PvCustomer.Visibility = ShowCustomerCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PvDetails.Visibility = ShowDetailsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        TitleFontSizeText.Text = _titleFontSize.ToString("0") + "pt";
        ItemsFontSizeText.Text = _itemsFontSize.ToString("0") + "pt";
        TotalFontSizeText.Text = _totalFontSize.ToString("0") + "pt";
        MetaFontSizeText.Text = _metaFontSize.ToString("0") + "pt";

        PvTitle.FontSize = _titleFontSize;

        PvItemName.FontSize = _itemsFontSize;
        PvItemPrice.FontSize = _itemsFontSize;
        PvDetails.FontSize = _itemsFontSize * (10.5 / 13.0);

        PvTotalAmount.FontSize = _totalFontSize;
        PvTotalLabel.FontSize = _totalFontSize * (17.0 / 20.0);

        PvInfo.FontSize = _metaFontSize;
        PvOrderLabel.FontSize = _metaFontSize;
        PvDateTime.FontSize = _metaFontSize;
        PvTypeLabel.FontSize = _metaFontSize * (12.5 / 11.0);
        PvTypeValue.FontSize = _metaFontSize * (12.5 / 11.0);
        PvWhoLabel.FontSize = _metaFontSize * (12.5 / 11.0);
        PvWhoValue.FontSize = _metaFontSize * (12.5 / 11.0);
        PvFooter.FontSize = _metaFontSize * (12.0 / 11.0);
    }

    private void SaveReceipt_Click(object sender, RoutedEventArgs e)
    {
        _store.SetReceipt(ReceiptTitleBox.Text, ReceiptInfoBox.Text, ReceiptFooterBox.Text,
            ShowDateTimeCheck.IsChecked == true, ShowCustomerCheck.IsChecked == true,
            ShowDetailsCheck.IsChecked == true,
            _titleFontSize, _itemsFontSize, _totalFontSize, _metaFontSize);
        Close();
    }
}
