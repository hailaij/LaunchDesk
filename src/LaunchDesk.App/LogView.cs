using static LaunchDesk.UiText;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LaunchDesk.Core;

namespace LaunchDesk;

public sealed class LogView : ListBox
{
    private sealed class Line(string value)
    {
        public string Text { get; } = value;
        public int AsciiCount { get; } = value.Count(c => c <= 127);
        public int WideCount => Text.Length - AsciiCount;
    }
    private sealed class LineCollection : IList, INotifyCollectionChanged
    {
        private readonly List<Line> values = [];
        private int head;
        public IEnumerable<Line> Values => values.Skip(head);
        public int Resets { get; private set; }
        public void Replace(string[] values)
        {
            // Match the old suffix to the new prefix, including repeated/blank lines.
            // Prefix links keep repeated output linear rather than trying every match.
            var prefix = new int[values.Length];
            for (var i = 1; i < values.Length; i++)
            {
                var matched = prefix[i - 1];
                while (matched > 0 && values[i] != values[matched]) matched = prefix[matched - 1];
                if (values[i] == values[matched]) matched++;
                prefix[i] = matched;
            }
            var retained = 0; var beforeLast = 0;
            for (var i = 0; i < Count && values.Length > 0; i++)
            {
                beforeLast = retained;
                var current = this.values[head + i].Text;
                while (retained > 0 && (retained == values.Length || current != values[retained])) retained = prefix[retained - 1];
                if (current == values[retained]) retained++;
            }
            var end = Count;
            if (Count > 0 && this.values[^1].Text.Length == 0 && beforeLast > retained)
            { end--; retained = beforeLast; }
            var start = end - retained;
            if (retained == 0)
            {
                this.values.Clear(); head = 0;
                this.values.AddRange(values.Select(x => new Line(x)));
                Resets++; CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Reset));
                return;
            }
            // WPF does not support range notifications. The head offset avoids shifting
            // the entire retained buffer for each individual removal.
            while (start-- > 0)
            {
                var removed = this.values[head++];
                CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Remove, removed, 0));
            }
            while (Count > retained)
            {
                var index = Count - 1; var removed = this.values[^1]; this.values.RemoveAt(this.values.Count - 1);
                CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Remove, removed, index));
            }
            for (var i = retained; i < values.Length; i++)
            {
                var index = Count; var added = new Line(values[i]); this.values.Add(added);
                CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Add, added, index));
            }
            if (head > 4096) { this.values.RemoveRange(0, head); head = 0; }
        }
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public int Count => values.Count - head;
        public object? this[int index] { get => values[head + index]; set => throw new NotSupportedException(); }
        public bool IsReadOnly => true;
        public bool IsFixedSize => false;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public int IndexOf(object? value) => value is Line line && values.IndexOf(line, head) is var index && index >= 0 ? index - head : -1;
        public bool Contains(object? value) => IndexOf(value) >= 0;
        public void CopyTo(Array array, int index) { foreach (var value in Values) array.SetValue(value, index++); }
        public IEnumerator GetEnumerator() => Values.GetEnumerator();
        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }
    private readonly LineCollection lines = new();
    private string? text = "";
    private ScrollViewer? scroll;
    private int scrollGeneration;
    private int selectionAnchor;
    private LogLineView? characterOwner;
    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(nameof(RowHeight), typeof(double), typeof(LogView), new PropertyMetadata(18d));
    public static readonly DependencyProperty ContentWidthProperty = DependencyProperty.Register(nameof(ContentWidth), typeof(double), typeof(LogView), new PropertyMetadata(1d));
    public double RowHeight { get => (double)GetValue(RowHeightProperty); private set => SetValue(RowHeightProperty, value); }
    public double ContentWidth { get => (double)GetValue(ContentWidthProperty); private set => SetValue(ContentWidthProperty, value); }
    public double VerticalOffset => scroll?.VerticalOffset ?? 0;
    public double HorizontalOffset => scroll?.HorizontalOffset ?? 0;
    public double ViewportHeight => scroll?.ViewportHeight ?? 0;
    public double ExtentHeight => scroll?.ExtentHeight ?? 0;
    public int RealizedLineCount => VisualChildren<LogLineView>(this).Count();
    public bool HasRenderedLink => VisualChildren<LogLineView>(this).Any(x => x.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()).Any());
    public int ResetCount => lines.Resets;
    public string Text
    {
        get => text ??= string.Join('\n', lines.Values.Select(x => x.Text));
        set => SetLines(value.Length == 0 ? [] : value.Split('\n'));
    }
    public void SetLines(string[] incoming)
    {
        if (incoming.Length == lines.Count && !incoming.Where((value, index) => value != ((Line)lines[index]!).Text).Any()) return;
        var offset = VerticalOffset;
        var top = Math.Clamp((int)offset, 0, Math.Max(0, lines.Count - 1));
        var anchor = lines.Count == 0 ? null : (Line)lines[top]!;
        var selected = SelectedItems.Cast<Line>().ToHashSet();
        text = null;
        lines.Replace(incoming);
        UpdateContentWidth();
        foreach (var retained in lines.Values.Where(selected.Contains)) if (!SelectedItems.Contains(retained)) SelectedItems.Add(retained);
        var nextTop = anchor is null ? -1 : lines.IndexOf(anchor);
        // Preserve the inspected row when the bounded history drops old lines.
        if (nextTop >= 0) offset += nextTop - top;
        var restore = Math.Max(0, offset); var generation = ++scrollGeneration;
        scroll?.ScrollToVerticalOffset(restore);
        _ = Dispatcher.InvokeAsync(() => { if (generation == scrollGeneration) scroll?.ScrollToVerticalOffset(restore); }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
    public LogView()
    {
        SelectionMode = SelectionMode.Extended; ItemsSource = lines;
        ScrollViewer.SetCanContentScroll(this, true);
        ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(this, true);
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Item);
        VirtualizingPanel.SetCacheLength(this, new VirtualizationCacheLength(0));
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        var content = new FrameworkElementFactory(typeof(LogLineView));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        content.SetBinding(LogLineView.ValueProperty, new Binding("Text"));
        content.SetBinding(ForegroundProperty, Ancestor(nameof(Foreground)));
        ItemTemplate = new DataTemplate { VisualTree = content };
        var container = new Style(typeof(ListBoxItem));
        container.Setters.Add(new Setter(HeightProperty, 18d));
        container.Setters.Add(new Setter(MinWidthProperty, Ancestor(nameof(ContentWidth))));
        container.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        container.Setters.Add(new Setter(MarginProperty, new Thickness(0)));
        container.Setters.Add(new Setter(ForegroundProperty, Ancestor(nameof(Foreground))));
        container.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        container.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));
        container.Triggers.Add(new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true, Setters = { new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(150, 37, 94, 128))) } });
        ItemContainerStyle = container;
        CommandBindings.Add(new(ApplicationCommands.Copy, (_, _) => CopySelection(), (_, e) => e.CanExecute = SelectedItems.Count > 0 || VisualChildren<LogLineView>(this).Any(x => !x.Selection.IsEmpty)));
        CommandBindings.Add(new(ApplicationCommands.SelectAll, (_, _) => SelectAll(), (_, e) => e.CanExecute = Items.Count > 0));
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = T("复制"), Command = ApplicationCommands.Copy, CommandTarget = this });
        menu.Items.Add(new MenuItem { Header = T("全选"), Command = ApplicationCommands.SelectAll, CommandTarget = this });
        menu.Opened += (_, _) => { ((MenuItem)menu.Items[0]).Header = T("复制"); ((MenuItem)menu.Items[1]).Header = T("全选"); };
        ContextMenu = menu;
        PreviewGotKeyboardFocus += (_, e) => { if (e.NewFocus is LogLineView line) characterOwner = line; };
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            var line = VisualChildren<LogLineView>(this).FirstOrDefault(x => x.IsMouseOver);
            if (line is null || ItemsControl.ContainerFromElement(this, line) is not ListBoxItem container) return;
            var index = ItemContainerGenerator.IndexFromContainer(container);
            var modifiers = Keyboard.Modifiers;
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
            { selectionAnchor = index; UnselectAll(); return; }
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                var parent = line.GetPositionFromPoint(e.GetPosition(line), true)?.Parent as FrameworkContentElement;
                while (parent is not null)
                { if (parent is Hyperlink) return; parent = parent.Parent as FrameworkContentElement; }
            }
            e.Handled = true; ClearCharacterSelection(); Focus();
            SelectRow(index, modifiers.HasFlag(ModifierKeys.Shift), modifiers.HasFlag(ModifierKeys.Control));
        };
    }
    private static Binding Ancestor(string property) => new(property) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(LogView), 1) };
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontSizeProperty)
        {
            RowHeight = Math.Ceiling(FontSize * 1.5);
            foreach (var container in VisualChildren<ListBoxItem>(this)) container.Height = RowHeight;
        }
        if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty) UpdateContentWidth();
    }
    private void UpdateContentWidth()
    {
        if (lines is null) return;
        var cell = new FormattedText("M", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        var wideCell = FontSize * 1.2;
        // Stable horizontal extent without shaping the thousands of off-screen lines.
        ContentWidth = Math.Max(1, lines.Values.Select(line => line.AsciiCount * cell + line.WideCount * wideCell).DefaultIfEmpty(0).Max() + 16);
    }
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    { base.PrepareContainerForItemOverride(element, item); ((ListBoxItem)element).Height = RowHeight; }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate(); scroll = VisualChildren<ScrollViewer>(this).FirstOrDefault();
    }
    internal static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }
    public string SelectedText
    {
        get
        {
            var characterSelection = VisualChildren<LogLineView>(this).FirstOrDefault(x => x == characterOwner && !x.Selection.IsEmpty);
            if (characterSelection is not null) return new TextRange(characterSelection.Selection.Start, characterSelection.Selection.End).Text;
            var selected = SelectedItems.Cast<Line>().ToHashSet(); return string.Join('\n', lines.Values.Where(selected.Contains).Select(x => x.Text));
        }
    }
    private void ClearCharacterSelection()
    { foreach (var line in VisualChildren<LogLineView>(this)) line.Selection.Select(line.Document.ContentStart, line.Document.ContentStart); }
    public new void SelectAll() { ClearCharacterSelection(); base.SelectAll(); }
    internal void SelectRow(int index, bool extend, bool toggle)
    {
        if (index < 0 || index >= lines.Count) return;
        if (!toggle) UnselectAll();
        if (extend)
        {
            for (var i = Math.Max(0, Math.Min(selectionAnchor, index)); i <= Math.Min(lines.Count - 1, Math.Max(selectionAnchor, index)); i++)
                if (!SelectedItems.Contains(lines[i])) SelectedItems.Add(lines[i]);
        }
        else
        {
            selectionAnchor = index;
            if (toggle && SelectedItems.Contains(lines[index])) SelectedItems.Remove(lines[index]);
            else SelectedItems.Add(lines[index]);
        }
    }
    private void CopySelection()
    {
        try { if (SelectedText.Length > 0) Clipboard.SetText(SelectedText); }
        catch (System.Runtime.InteropServices.ExternalException error) { Debug.WriteLine(error); }
    }
    public void Clear() => Text = "";
    public void ScrollToEnd() { scrollGeneration++; if (Items.Count > 0) ScrollIntoView(Items[^1]); scroll?.ScrollToEnd(); }
    public void ScrollToVerticalOffset(double offset) { scrollGeneration++; scroll?.ScrollToVerticalOffset(offset); }
    public void ScrollToHorizontalOffset(double offset) => scroll?.ScrollToHorizontalOffset(offset);
}

public sealed class LogLineView : RichTextBox
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(LogLineView), new PropertyMetadata("", (d, e) => ((LogLineView)d).Render((string)e.NewValue)));
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public LogLineView()
    {
        IsReadOnly = true; IsDocumentEnabled = true; IsUndoEnabled = false;
        BorderThickness = new Thickness(0); Padding = new Thickness(0); Background = Brushes.Transparent;
        HorizontalScrollBarVisibility = VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Document = new FlowDocument { PagePadding = new Thickness(0) };
        ContextMenu = null;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.A || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            if (ItemsControl.ItemsControlFromItemContainer(FindContainer(this)) is LogView owner)
            { Selection.Select(Document.ContentStart, Document.ContentStart); owner.Focus(); owner.SelectAll(); e.Handled = true; }
        };
    }
    private static DependencyObject FindContainer(DependencyObject value)
    {
        while (value is not ListBoxItem && VisualTreeHelper.GetParent(value) is { } parent) value = parent;
        return value;
    }
    private void Render(string value)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) }; var offset = 0;
        Document.Blocks.Clear(); Document.Blocks.Add(paragraph);
        foreach (var link in LogLinks.Find(value))
        {
            paragraph.Inlines.Add(new Run(value[offset..link.Start]));
            var hyperlink = new Hyperlink(new Run(value.Substring(link.Start, link.Length)))
            { NavigateUri = link.Uri, Foreground = new SolidColorBrush(Color.FromRgb(113, 199, 255)), ToolTip = link.Uri.AbsoluteUri + T("\nCtrl+点击打开"), Cursor = Cursors.Hand };
            hyperlink.Click += (_, e) =>
            {
                e.Handled = true;
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
                try { Process.Start(new ProcessStartInfo(link.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception error) { MessageBox.Show(Window.GetWindow(this), ErrorText(error.Message), T("无法打开链接"), MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            hyperlink.RequestNavigate += (_, e) => e.Handled = true;
            paragraph.Inlines.Add(hyperlink); offset = link.Start + link.Length;
        }
        paragraph.Inlines.Add(new Run(value[offset..].TrimEnd('\r')));
        MeasureWidth();
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty) MeasureWidth();
    }
    private void MeasureWidth()
    {
        if (Document is null) return;
        Document.FontFamily = FontFamily; Document.FontSize = FontSize; Document.FontWeight = FontWeight;
        Document.FontStyle = FontStyle; Document.LineHeight = Math.Ceiling(FontSize * 1.5);
        var width = new FormattedText(Value.TrimEnd('\r'), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        Width = Math.Max(16, width + 16); Document.PageWidth = Width;
        Document.ColumnWidth = Width; Document.ColumnGap = 0;
    }
}
