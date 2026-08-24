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
    private StatsWindow? _stats;
    private SettingsWindow? _settings;
    private MenuManagerWindow? _menuManager;
    private CustomersWindow? _customers;
    private ConsumptionWindow? _consumption;
    private TableDetailWindow? _tableDetail;

    /// <summary>
    /// Στενό παράθυρο; Η κεφαλίδα κουβαλά πολλά (λογότυπο, τίτλο, τέσσερα κουμπιά, παραγγελίες, ώρα,
    /// βάρδια, ρυθμίσεις, κουμπιά παραθύρου) και σε μικρό παράθυρο στριμώχνονταν το ένα πάνω στο άλλο.
    /// Όταν ανάψει αυτό, τα διακοσμητικά υποχωρούν (τίτλος, ώρα, κείμενα βάρδιας) και μένουν μόνο όσα
    /// χρειάζεται πραγματικά ο ταμίας. Μεγιστοποιημένο — που είναι και η κανονική χρήση — δεν αλλάζει τίποτα.
    /// </summary>
    public static readonly DependencyProperty IsNarrowProperty =
        DependencyProperty.Register(nameof(IsNarrow), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public bool IsNarrow
    {
        get => (bool)GetValue(IsNarrowProperty);
        set => SetValue(IsNarrowProperty, value);
    }

    /// <summary>Κάτω από αυτό το πλάτος η κεφαλίδα δεν χωράει άνετα — μετρημένο: στα 1280 το κουμπί
    /// «ΠΑΡΑΓΓΕΛΙΕΣ» έβγαινε ήδη κομμένο σε «ΠΑΡΑ».</summary>
    private const double NarrowWidth = 1420;

    public MainWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        DataContext = _wizard;

        // Χωρίς πλαίσιο παραθύρου, το μεγιστοποιημένο παράθυρο ξεχείλιζε 7px σε κάθε πλευρά και τα
        // κουμπιά πάνω δεξιά κόβονταν (βλ. MaximizeFix).
        MaximizeFix.Attach(this);
        SizeChanged += (_, _) => IsNarrow = ActualWidth < NarrowWidth;

        // Ανοίγει σε όλη την οθόνη. Ορίζεται ΕΔΩ και όχι στο XAML: με WindowStyle="None" το WPF αγνοούσε
        // το WindowState="Maximized" της δήλωσης και το παράθυρο άνοιγε στο μικρό του μέγεθος (μετρημένο).
        Loaded += (_, _) => WindowState = WindowState.Maximized;
        _wizard.AutoPrintRequested += AutoPrintReceipt;
        _wizard.MissingFieldFocusRequested += FocusFirstMissingStep2Field;
        _wizard.TableDetailRequested += OpenTableDetail;
        _wizard.PersonsAskRequested += table => PersonsDialog.Ask(this, table);

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

    /// <summary>
    /// Το ✕ πάνω δεξιά κλείνει ΟΛΟ το ταμείο — και μαζί σταματούν οι Ζωντανές Παραγγελίες, ο εκτυπωτής
    /// και το API του σερβιτόρου. Πολύ ακριβό για ένα κατά λάθος κλικ, οπότε ρωτάει πρώτα.
    /// Προεπιλογή το ΟΧΙ: με Enter/κενό πάτημα το πρόγραμμα μένει ανοιχτό.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
            return;

        var answer = MessageBox.Show(this,
            "Θέλεις σίγουρα να κλείσεις το πρόγραμμα;",
            "Κλείσιμο " + AppIdentity.StoreName,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        e.Cancel = answer != MessageBoxResult.Yes;
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

    // ---- δική μας μπάρα τίτλου (βλ. WindowChrome στο MainWindow.xaml) ----
    //
    // Με WindowStyle="None" το παράθυρο δεν έχει πια τη μπάρα των Windows, οπότε το σύρσιμο και το διπλό
    // κλικ για μεγιστοποίηση γίνονται εδώ, πάνω στην κεφαλίδα της εφαρμογής. Τα κουμπιά μέσα στην
    // κεφαλίδα δεν επηρεάζονται: το WPF σταματά το δικό τους κλικ πριν φτάσει ως εδώ.

    /// <summary>Σύρσιμο του παραθύρου από την κεφαλίδα, και διπλό κλικ για μεγιστοποίηση/επαναφορά —
    /// ό,τι ακριβώς κάνει και η κανονική μπάρα τίτλου των Windows.</summary>
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        // Μεγιστοποιημένο δεν σέρνεται. Το DragMove σε μεγιστοποιημένο παράθυρο δεν το επαναφέρει όπως
        // κάνουν τα Windows — το κουβαλάει ολόκληρο, και ένα κατά λάθος τράβηγμα στη βάρδια θα έστελνε
        // το ταμείο μισό εκτός οθόνης. Και επειδή έτσι δουλεύει σχεδόν πάντα (ανοίγει μεγιστοποιημένο),
        // ο ταμίας δεν χάνει τίποτα: για μετακίνηση υπάρχει πρώτα η επαναφορά με το ▢.
        if (WindowState == WindowState.Maximized)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Το DragMove θέλει το κουμπί ΑΚΟΜΑ πατημένο· αν προλάβει να αφεθεί (γρήγορο κλικ) πετάει.
            // Δεν είναι σφάλμα: απλά δεν υπάρχει τίποτα να συρθεί.
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

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
    /// Αν λείπει κάτι υποχρεωτικό (ο αριθμός παραγγελίας της πλατφόρμας, ή τα στοιχεία πελάτη στο BOX)
    /// δεν προχωράει — ίδιος ακριβώς κανόνας με το κουμπί ΣΥΝΕΧΕΙΑ, γιατί περνάει από την ίδια εντολή:
    /// εκείνη κοκκινίζει τα άδεια πεδία αντί να προχωρήσει (βλ. OrderWizardViewModel.ContinueStep2).
    /// </summary>
    private void AppMethod_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_wizard.ContinueStep2Command.CanExecute(null))
            return;

        _wizard.ContinueStep2Command.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Κρυφή είσοδος στα ΣΤΑΤΙΣΤΙΚΑ: το κουμπί έφυγε από την μπάρα (ζητήθηκε να μη φαίνεται) και τα
    /// ανοίγει πλέον το ίδιο το λογότυπο. Ο κωδικός συνεχίζει να ζητείται κανονικά.
    /// </summary>
    private void Logo_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Χωρίς αυτό το πάτημα συνέχιζε στη μπάρα και ξεκινούσε σύρσιμο του παραθύρου
        // (βλ. Header_MouseLeftButtonDown) — τα Στατιστικά δεν άνοιγαν ποτέ.
        e.Handled = true;
        OpenStats_Click(sender, e);
    }

    private void OpenStats_Click(object sender, RoutedEventArgs e)
    {
        if (_stats is null || !_stats.IsLoaded)
        {
            // Ο κωδικός που άνοιξε τα Στατιστικά ταξιδεύει μαζί τους: αν ανοίγει και το Ιστορικό, το
            // κουμπί εκεί μέσα δεν θα ξαναρωτήσει (βλ. StatsWindow).
            var pin = PinDialog.RequirePin(this, StaffRight.Stats);
            if (pin is null)
                return;
            _stats = new StatsWindow(pin);
            _stats.Closed += (_, _) => _stats = null;
            _stats.Show();
        }
        else
        {
            _stats.Activate();
        }
    }

    private void OpenMenuManager_Click(object sender, RoutedEventArgs e)
    {
        if (_menuManager is null || !_menuManager.IsLoaded)
        {
            if (!PinDialog.Require(this, StaffRight.Menu))
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
            if (!PinDialog.Require(this, StaffRight.Customers))
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

    /// <summary>Πόσο κρέας τρώει κάθε προϊόν, και πόσο έχει φύγει σήμερα. Πίσω από τον κωδικό όπως
    /// Στατιστικά/Ιστορικό/Πελάτες — δείχνει τζίρο σε υλικά.</summary>
    private void OpenConsumption_Click(object sender, RoutedEventArgs e)
    {
        if (_consumption is null || !_consumption.IsLoaded)
        {
            if (!PinDialog.Require(this, StaffRight.Consumption))
                return;
            _consumption = new ConsumptionWindow();
            _consumption.Closed += (_, _) => _consumption = null;
            _consumption.Show();
        }
        else
        {
            _consumption.Activate();
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

    /// <summary>
    /// Πατήθηκε ΣΥΝΕΧΕΙΑ στο Βήμα 2 με κάτι κενό. Τα άδεια κουτιά κοκκινίζουν μόνα τους, αλλά η φόρμα
    /// είναι μακρύτερη από την οθόνη — αν το κόκκινο είναι κύλισμα πιο πάνω (π.χ. ο ταμίας μόλις
    /// πρόσθεσε νέα διεύθυνση, στο κάτω μέρος της σελίδας), το μόνο που βλέπει είναι ένα κουμπί που δεν
    /// κάνει τίποτα. Κυλάμε στο πρώτο κόκκινο κουτί και βάζουμε μέσα τον κέρσορα.
    /// </summary>
    private void FocusFirstMissingStep2Field()
    {
        if (FindMissingField(Step2Scroll) is not { } field)
            return;
        field.BringIntoView();
        field.Focus();
    }

    /// <summary>Το πρώτο ορατό κουτί που έχει σημαδευτεί ως κενό. Τα ίδια τα κουτιά το λένε: κουβαλούν
    /// <c>Tag="{Binding Missing…}"</c> για να κοκκινίσουν, οπότε δεν χρειάζεται δεύτερη λίστα πεδίων
    /// εδώ που θα ξεχνιόταν να ενημερωθεί. Η σειρά του visual tree είναι η σειρά της οθόνης.</summary>
    private static Control? FindMissingField(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Control { Tag: true, IsVisible: true } field)
                return field;
            if (FindMissingField(child) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>Αυτόματη εκτύπωση στον προεπιλεγμένο εκτυπωτή μόλις ολοκληρωθεί παραγγελία, χωρίς dialog.</summary>
    private void AutoPrintReceipt(CompletedOrder order) => ReceiptPrinter.PrintOrder(order);
}
