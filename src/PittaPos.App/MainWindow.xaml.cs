using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PittaPos.App.Services;
using PittaPos.App.ViewModels;
using PittaPos.App.Views;

namespace PittaPos.App;

public partial class MainWindow : Window
{
    private readonly OrderWizardViewModel _wizard = new();
    private readonly DispatcherTimer _clock;
    private LiveOrdersWindow? _liveOrders;
    private StatsWindow? _stats;
    private HistoryWindow? _history;
    private SettingsWindow? _settings;
    private MenuManagerWindow? _menuManager;
    private CustomersWindow? _customers;
    private TableDetailWindow? _tableDetail;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _wizard;
        _wizard.AutoPrintRequested += AutoPrintReceipt;
        _wizard.TableDetailRequested += OpenTableDetail;

        // Φέρνει το ταμείο μπροστά μόλις χτυπήσει το τηλέφωνο, ακόμη κι αν ο ταμίας είναι σε άλλο παράθυρο/εφαρμογή.
        IncomingCallService.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(IncomingCallService.IsActive) || !IncomingCallService.Instance.IsActive)
                return;
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        };

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm");
        _clock.Start();
        ClockText.Text = DateTime.Now.ToString("HH:mm");

        // Διαβάζει το version.txt δίπλα στο exe (Content, CopyToOutputDirectory) — ανεβαίνει ΧΕΙΡΟΚΙΝΗΤΑ
        // μόνο όταν κλείνουμε δουλειά/κάνουμε commit/ανανεώνουμε το setup, όχι σε κάθε dev build.
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "version.txt");
            VersionText.Text = System.IO.File.Exists(path) ? "v" + System.IO.File.ReadAllText(path).Trim() : "";
        }
        catch (Exception)
        {
            VersionText.Text = "";
        }
    }

    /// <summary>Κλικ στο κενό φόντο του Βήματος 1 = deselect (τα κουμπιά καταναλώνουν τα δικά τους κλικ).</summary>
    private void Step1Background_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _wizard.DeselectOrderTypeCommand.Execute(null);
    }

    /// <summary>ESC = επιστροφή στην αρχική οθόνη (καθαρίζει την τρέχουσα παραγγελία).</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            _wizard.GoToStepCommand.Execute(1);
    }

    // ---- κάτοψη τραπεζιών: σύρε-και-άσε σε λειτουργία διάταξης ----

    private TableOptionViewModel? _draggingTable;
    private Point _dragStartMouse;
    private Point _dragStartPos;

    private static Canvas? FindCanvasAncestor(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is Canvas c)
                return c;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void TableCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_wizard.IsArrangingTables)
            return;
        if (sender is not FrameworkElement { DataContext: TableOptionViewModel table } fe)
            return;
        var canvas = FindCanvasAncestor(fe);
        if (canvas is null)
            return;

        _draggingTable = table;
        _dragStartMouse = e.GetPosition(canvas);
        _dragStartPos = new Point(table.X, table.Y);
        fe.CaptureMouse();
        e.Handled = true;
    }

    private void TableCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingTable is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        if (sender is not FrameworkElement fe)
            return;
        var canvas = FindCanvasAncestor(fe);
        if (canvas is null)
            return;

        var pos = e.GetPosition(canvas);
        _draggingTable.X = Math.Max(0, _dragStartPos.X + (pos.X - _dragStartMouse.X));
        _draggingTable.Y = Math.Max(0, _dragStartPos.Y + (pos.Y - _dragStartMouse.Y));
    }

    private void TableCard_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingTable is null)
            return;
        if (sender is FrameworkElement fe)
            fe.ReleaseMouseCapture();
        _wizard.SaveTablePosition(_draggingTable);
        _draggingTable = null;
        e.Handled = true;
    }

    /// <summary>Κλικ σε ανοιχτό τραπέζι στην κάτοψη — δείχνει τι έχει παραγγελθεί και επιτρέπει εξόφληση ανά προϊόν.</summary>
    private void OpenTableDetail(int table)
    {
        if (_tableDetail is { IsLoaded: true })
            _tableDetail.Close();

        _tableDetail = new TableDetailWindow(table) { Owner = this };
        _tableDetail.NewRoundRequested += _wizard.StartNewRoundForTable;
        _tableDetail.Closed += (_, _) => _tableDetail = null;
        _tableDetail.Show();
    }

    /// <summary>
    /// Διπλό κλικ στην πλατφόρμα (Wolt / e-food / BOX) του Βήματος 2 = «διάλεξέ την ΚΑΙ προχώρα»,
    /// χωρίς να ταξιδέψει το χέρι μέχρι το ΣΥΝΕΧΕΙΑ — μετράει σε ώρα αιχμής. Το πρώτο από τα δύο
    /// κλικ έχει ήδη κάνει την επιλογή μέσω του SelectAppMethodCommand.
    ///
    /// Αν λείπει ο υποχρεωτικός αριθμός παραγγελίας της πλατφόρμας, δεν γίνεται τίποτα — ίδιος
    /// ακριβώς κανόνας με το κουμπί ΣΥΝΕΧΕΙΑ (βλ. OrderWizardViewModel.Step2ContinueEnabled), ώστε
    /// να μη γλιστράει μια παραγγελία Wolt/e-food χωρίς τον αριθμό της.
    /// </summary>
    private void AppMethod_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_wizard.Step2ContinueEnabled || !_wizard.ContinueStep2Command.CanExecute(null))
            return;

        _wizard.ContinueStep2Command.Execute(null);
        e.Handled = true;
    }

    private void OpenLiveOrders_Click(object sender, RoutedEventArgs e)
    {
        if (_liveOrders is null || !_liveOrders.IsLoaded)
        {
            // Χωρίς Owner ώστε η αρχική να μπορεί να έρθει μπροστά του για νέα παραγγελία
            _liveOrders = new LiveOrdersWindow();
            _liveOrders.Closed += (_, _) => _liveOrders = null;
            _liveOrders.Show();
        }
        else
        {
            _liveOrders.Activate();
        }
    }

    private void OpenStats_Click(object sender, RoutedEventArgs e)
    {
        if (_stats is null || !_stats.IsLoaded)
        {
            if (!PinDialog.Require(this))
                return;
            _stats = new StatsWindow();
            _stats.Closed += (_, _) => _stats = null;
            _stats.Show();
        }
        else
        {
            _stats.Activate();
        }
    }

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_history is null || !_history.IsLoaded)
        {
            if (!PinDialog.Require(this))
                return;
            _history = new HistoryWindow();
            _history.Closed += (_, _) => _history = null;
            _history.Show();
        }
        else
        {
            _history.Activate();
        }
    }

    private void OpenMenuManager_Click(object sender, RoutedEventArgs e)
    {
        if (_menuManager is null || !_menuManager.IsLoaded)
        {
            if (!PinDialog.Require(this))
                return;
            _menuManager = new MenuManagerWindow();
            _menuManager.Closed += (_, _) => _menuManager = null;
            _menuManager.Show();
        }
        else
        {
            _menuManager.Activate();
        }
    }

    private void OpenCustomers_Click(object sender, RoutedEventArgs e)
    {
        if (_customers is null || !_customers.IsLoaded)
        {
            if (!PinDialog.Require(this))
                return;
            _customers = new CustomersWindow();
            _customers.Closed += (_, _) => _customers = null;
            _customers.Show();
        }
        else
        {
            _customers.Activate();
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || !_settings.IsLoaded)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        else
        {
            _settings.Activate();
        }
    }

    /// <summary>Αυτόματη εκτύπωση στον προεπιλεγμένο εκτυπωτή μόλις ολοκληρωθεί παραγγελία, χωρίς dialog.</summary>
    private void AutoPrintReceipt() => ReceiptPrinter.PrintLatestOrder();
}
