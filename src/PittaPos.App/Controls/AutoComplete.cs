using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace PittaPos.App.Controls;

/// <summary>
/// Προτάσεις καθώς γράφεις, πάνω σε οποιοδήποτε TextBox:
/// <c>ac:AutoComplete.Suggestions="{Binding IngredientSuggestions}"</c>.
///
/// Φτιάχτηκε για τη Διαχείριση Καταλόγου: ένα υλικό ή ένα έξτρα που γράφτηκε μία φορά σε κάποιο
/// προϊόν δεν χρειάζεται να ξαναγραφτεί σωστά από την αρχή στο επόμενο — αρκούν δύο γράμματα.
/// Το ταίριασμα αγνοεί τόνους, πεζά/κεφαλαία και τελικό σίγμα («τζατζικι» βρίσκει «Τζατζίκι»),
/// γιατί αλλιώς η λίστα γεμίζει διπλοεγγραφές που διαφέρουν μόνο στον τόνο.
///
/// Η λίστα δεν ανοίγει ποτέ με τίποτα προεπιλεγμένο: το Enter παίρνει πρόταση ΜΟΝΟ αφού την
/// διαλέξεις με τα βελάκια. Διαφορετικά ένα καινούριο όνομα που τυχαίνει να μοιάζει με παλιό θα
/// αντικαθιστούσε σιωπηλά ό,τι μόλις πληκτρολόγησες.
/// </summary>
public static class AutoComplete
{
    /// <summary>Πάνω από τόσες προτάσεις η λίστα γίνεται δυσανάγνωστη — και σημαίνει ότι πρέπει να
    /// γράψεις άλλο ένα γράμμα.</summary>
    private const int MaxShown = 8;

    public static readonly DependencyProperty SuggestionsProperty =
        DependencyProperty.RegisterAttached("Suggestions", typeof(IEnumerable<string>), typeof(AutoComplete),
            new PropertyMetadata(null, OnSuggestionsChanged));

    public static void SetSuggestions(DependencyObject target, IEnumerable<string>? value) =>
        target.SetValue(SuggestionsProperty, value);

    public static IEnumerable<string>? GetSuggestions(DependencyObject target) =>
        (IEnumerable<string>?)target.GetValue(SuggestionsProperty);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(State), typeof(AutoComplete));

    /// <summary>Ανοιχτή λίστα υπάρχει μόνο μία σε όλη την εφαρμογή — αλλιώς μια ξεχασμένη λίστα από
    /// άλλο πεδίο κάθεται πάνω από αυτό που γράφεις τώρα και το σκεπάζει.</summary>
    private static State? _open;

    private sealed class State
    {
        public required TextBox Box { get; init; }
        public required Popup Popup { get; init; }
        public required ListBox List { get; init; }
        /// <summary>Όσο είναι σηκωμένο, η αλλαγή κειμένου δεν ξανανοίγει τη λίστα — αλλιώς το ίδιο το
        /// γέμισμα του πεδίου με την πρόταση θα την ξανάνοιγε αμέσως από κάτω.</summary>
        public bool Suppress { get; set; }
    }

    private static void OnSuggestionsChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not TextBox box || box.GetValue(StateProperty) is State)
            return;

        var list = new ListBox
        {
            Focusable = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            MaxHeight = 240,
            ItemContainerStyle = Application.Current?.TryFindResource("SuggestItem") as Style,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);

        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(4),
            Child = list,
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Bg");
        frame.SetResourceReference(Border.BorderBrushProperty, "Divider");
        frame.SetResourceReference(Border.EffectProperty, "SoftShadowSmall");
        // Τουλάχιστον όσο φαρδύ είναι το πεδίο: μια στενή λίστα κάτω από φαρδύ πλαίσιο δείχνει σαν
        // να ανήκει σε άλλο control.
        frame.SetBinding(FrameworkElement.MinWidthProperty,
            new Binding(nameof(FrameworkElement.ActualWidth)) { Source = box });

        var popup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Focusable = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = frame,
        };

        var state = new State { Box = box, Popup = popup, List = list };
        box.SetValue(StateProperty, state);

        box.TextChanged += (_, _) => Refresh(state, showAllWhenEmpty: false);
        box.PreviewKeyDown += (_, args) => OnKeyDown(state, args);
        box.LostKeyboardFocus += (_, _) => Close(state);
        // Το πάτημα «τρώγεται» εδώ επίτηδες: αν το πάρει το ListBoxItem, φεύγει η εστίαση από το
        // πεδίο, η λίστα κλείνει από το LostKeyboardFocus και το κλικ χάνεται στο κενό.
        list.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (ItemsControl.ContainerFromElement(list, (DependencyObject)args.OriginalSource) is not ListBoxItem item)
                return;
            args.Handled = true;
            Commit(state, (string)item.Content);
        };
    }

    private static void OnKeyDown(State state, KeyEventArgs args)
    {
        switch (args.Key)
        {
            case Key.Down when !state.Popup.IsOpen:
                // Κάτω βελάκι σε άδειο πεδίο = «δείξε μου τι υπάρχει ήδη», χωρίς να γράψεις τίποτα.
                Refresh(state, showAllWhenEmpty: true);
                args.Handled = state.Popup.IsOpen;
                break;
            case Key.Down:
                Move(state, 1);
                args.Handled = true;
                break;
            case Key.Up when state.Popup.IsOpen:
                Move(state, -1);
                args.Handled = true;
                break;
            case Key.Enter or Key.Tab when state.Popup.IsOpen && state.List.SelectedItem is string picked:
                Commit(state, picked);
                args.Handled = args.Key == Key.Enter; // το Tab συνεχίζει κανονικά στο επόμενο πεδίο
                break;
            case Key.Escape when state.Popup.IsOpen:
                Close(state);
                args.Handled = true;
                break;
        }
    }

    private static void Move(State state, int delta)
    {
        var count = state.List.Items.Count;
        if (count == 0)
            return;
        var current = state.List.SelectedIndex;
        // Πρώτο βελάκι χωρίς επιλογή: πάνω = τελευταία πρόταση, κάτω = πρώτη. Μετά, ανακύκλωση.
        state.List.SelectedIndex = current < 0
            ? (delta > 0 ? 0 : count - 1)
            : (current + delta + count) % count;
        if (state.List.SelectedItem is not null)
            state.List.ScrollIntoView(state.List.SelectedItem);
    }

    private static void Refresh(State state, bool showAllWhenEmpty)
    {
        if (state.Suppress)
            return;

        // ΜΟΝΟ στο πεδίο που γράφεις εσύ. Το πρόγραμμα γεμίζει μόνο του τα πεδία της φόρμας κάθε φορά
        // που διαλέγεις προϊόν ή κατηγορία — αυτό μετράει κι αυτό ως «άλλαξε το κείμενο», οπότε χωρίς
        // αυτόν τον έλεγχο άνοιγαν λίστες σε πεδία που δεν τα άγγιξες, και σκέπαζαν τα από κάτω.
        if (!state.Box.IsKeyboardFocused)
        {
            Close(state);
            return;
        }

        var all = GetSuggestions(state.Box) ?? [];
        var key = Normalize(state.Box.Text ?? "");

        List<string> matches = key.Length == 0
            ? (showAllWhenEmpty ? all.Take(MaxShown).ToList() : [])
            : all.Select(name => (name, normalized: Normalize(name)))
                .Where(entry => entry.normalized.Contains(key))
                // Όσα ΑΡΧΙΖΟΥΝ από αυτό που γράφεις πρώτα· μέσα στην ίδια ομάδα μένει η σειρά που
                // έδωσε το ViewModel (τα πιο πολυχρησιμοποιημένα μπροστά).
                .OrderBy(entry => entry.normalized.StartsWith(key, StringComparison.Ordinal) ? 0 : 1)
                .Select(entry => entry.name)
                .Take(MaxShown)
                .ToList();

        // Το έγραψες ολόκληρο — δεν έχει τι να προτείνει.
        if (matches.Count == 1 && Normalize(matches[0]) == key)
            matches.Clear();

        state.List.ItemsSource = matches;
        state.List.SelectedIndex = -1;
        if (matches.Count > 0)
            Open(state);
        else
            Close(state);
    }

    private static void Open(State state)
    {
        if (!ReferenceEquals(_open, state))
            Close(_open);
        _open = state;
        state.Popup.IsOpen = true;
    }

    private static void Close(State? state)
    {
        if (state is null)
            return;
        state.Popup.IsOpen = false;
        if (ReferenceEquals(_open, state))
            _open = null;
    }

    private static void Commit(State state, string value)
    {
        state.Suppress = true;
        state.Box.Text = value;
        state.Box.CaretIndex = value.Length;
        state.Suppress = false;
        Close(state);
        state.Box.Focus();
    }

    /// <summary>
    /// Μορφή σύγκρισης: κεφαλαία, χωρίς τόνους, τελικό σίγμα σαν σίγμα. Χρησιμοποιείται και από το
    /// ViewModel για να μη μαζευτούν στη λίστα δύο «ίδια» υλικά που διαφέρουν μόνο στον τόνο.
    /// </summary>
    public static string Normalize(string text)
    {
        var upper = text.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(upper.Length);
        foreach (var ch in upper)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
