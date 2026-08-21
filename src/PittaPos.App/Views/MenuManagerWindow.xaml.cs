using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using PittaPos.App.Services;
using PittaPos.App.ViewModels;
using PittaPos.Core.Models;

namespace PittaPos.App.Views;

public partial class MenuManagerWindow : Window
{
    public MenuManagerWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        DataContext = new MenuManagerViewModel();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void ManageExtras_Click(object sender, RoutedEventArgs e) =>
        new ManageExtrasWindow((MenuManagerViewModel)DataContext) { Owner = this }.ShowDialog();

    /// <summary>Σειρά προϊόντων με σύρσιμο από την ειδική λαβή «⠿» — ξεκινά αμέσως, χωρίς κατώφλι απόστασης.</summary>
    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Product dragged)
            return;
        e.Handled = true; // μην αφήνεις το πάτημα να φτάσει και στο κουμπί της γραμμής (επιλογή)
        DragDrop.DoDragDrop((FrameworkElement)sender, dragged, DragDropEffects.Move);
    }

    private void ProductItem_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Product)) is not Product dragged)
            return;
        var target = (Product)((Button)sender).DataContext;
        (DataContext as MenuManagerViewModel)?.MoveProductTo(dragged, target);
    }

    /// <summary>Σειρά βασικών υλικών ΤΟΥ ΕΠΙΛΕΓΜΕΝΟΥ προϊόντος — ίδιο σύρσιμο από λαβή με τα προϊόντα.</summary>
    private void IngredientHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        StartChipDrag(sender, e);

    private void IngredientChip_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ExtraToggleViewModel)) is not ExtraToggleViewModel dragged
            || ((FrameworkElement)sender).DataContext is not ExtraToggleViewModel target)
            return;
        // Αν σύρθηκε ταμπελάκι έξτρα πάνω σε υλικό, το MoveIngredientTo δεν το βρίσκει στη λίστα και
        // δεν κάνει τίποτα — οι δύο λίστες κρατούν τον ίδιο τύπο, οπότε ο έλεγχος γίνεται εκεί.
        (DataContext as MenuManagerViewModel)?.MoveIngredientTo(dragged, target);
    }

    /// <summary>Σειρά έξτρα ΤΟΥ ΕΠΙΛΕΓΜΕΝΟΥ προϊόντος — δεν αγγίζει τον κοινό κατάλογο.</summary>
    private void ExtraHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        StartChipDrag(sender, e);

    private void ExtraChip_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ExtraToggleViewModel)) is not ExtraToggleViewModel dragged
            || ((FrameworkElement)sender).DataContext is not ExtraToggleViewModel target)
            return;
        (DataContext as MenuManagerViewModel)?.MoveExtraTo(dragged, target);
    }

    private static void StartChipDrag(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ExtraToggleViewModel dragged)
            return;
        e.Handled = true; // να μη φτάσει το πάτημα στο κουτάκι επιλογής από δίπλα
        DragDrop.DoDragDrop((FrameworkElement)sender, dragged, DragDropEffects.Move);
    }

    /// <summary>Εξαγωγή όλου του καταλόγου σε Excel — για γέφυρες πλατφορμών (Wolt κ.λπ.).</summary>
    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = "katalogos-pitta-tou-pappou-" + DateTime.Now.ToString("yyyy-MM-dd") + ".xlsx",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Κατάλογος");

            string[] headers = ["Κατηγορία", "Προϊόν", "Περιγραφή", "Κανονική τιμή (€)",
                "Τιμή εφαρμογών (€)", "Επιλογές υλικών", "Κωδικός"];
            for (var i = 0; i < headers.Length; i++)
                sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Row(1).Style.Font.Bold = true;

            var row = 2;
            foreach (var category in MenuStore.Instance.Categories)
            {
                foreach (var product in category.Products)
                {
                    sheet.Cell(row, 1).Value = category.Name;
                    sheet.Cell(row, 2).Value = product.Name;
                    sheet.Cell(row, 3).Value = product.Description ?? "";
                    sheet.Cell(row, 4).Value = product.Price;
                    // Τελική τιμή πλατφορμών: η τιμή εφαρμογών αν έχει οριστεί, αλλιώς η κανονική
                    sheet.Cell(row, 5).Value = product.DeliveryPrice ?? product.Price;
                    sheet.Cell(row, 6).Value = product.Customizable ? "ΝΑΙ" : "ΟΧΙ";
                    sheet.Cell(row, 7).Value = product.Id;
                    row++;
                }
            }

            sheet.Column(4).Style.NumberFormat.Format = "0.00";
            sheet.Column(5).Style.NumberFormat.Format = "0.00";
            sheet.Columns().AdjustToContents();
            workbook.SaveAs(dialog.FileName);

            MessageBox.Show("Ο κατάλογος αποθηκεύτηκε:\n" + dialog.FileName,
                "Export Excel", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Η εξαγωγή απέτυχε: " + ex.Message,
                "Export Excel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
