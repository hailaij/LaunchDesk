using static LaunchDesk.UiText;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LaunchDesk.Core;

namespace LaunchDesk;

public sealed class LogView : RichTextBox
{
    private string text = "";
    public string Text
    {
        get => text;
        set
        {
            text = value;
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            var offset = 0;
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
                paragraph.Inlines.Add(hyperlink); offset = link.Start + link.Length;
                hyperlink.RequestNavigate += (_, e) => e.Handled = true;
            }
            paragraph.Inlines.Add(new Run(value[offset..]));
            Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
            UpdatePageWidth();
        }
    }
    private void UpdatePageWidth()
    {
        var width = 0d;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0) continue;
            var formatted = new FormattedText(line, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            width = Math.Max(width, formatted.WidthIncludingTrailingWhitespace);
        }
        Document.PageWidth = Math.Max(Math.Max(100, ActualWidth - Padding.Left - Padding.Right - 24), width + 16);
    }
    public LogView() { IsReadOnly = true; IsDocumentEnabled = true; SizeChanged += (_, _) => UpdatePageWidth(); }
    public void Clear() => Text = "";
}
