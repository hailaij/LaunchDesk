using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace LaunchDesk;

internal static class Accessibility
{
    private static bool initialized;
    public static void Initialize()
    {
        if (initialized) return; initialized = true;
        EventManager.RegisterClassHandler(typeof(Control), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
            ((Control)s).SetResourceReference(Control.FocusVisualStyleProperty, "KeyboardFocus")));
        EventManager.RegisterClassHandler(typeof(Button), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => NameButton((Button)s)));
        EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => NameInput((TextBox)s)));
    }
    private static IEnumerable<TextBlock> Text(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock block && block.FontFamily.Source != "Segoe MDL2 Assets") yield return block;
            foreach (var descendant in Text(child)) yield return descendant;
        }
    }
    public static void Apply(DependencyObject parent)
    {
        if (parent is Button button) NameButton(button);
        if (parent is TextBox input) NameInput(input);
        if (parent is Control control) control.SetResourceReference(Control.FocusVisualStyleProperty, "KeyboardFocus");
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Apply(VisualTreeHelper.GetChild(parent, i));
    }
    private static void NameButton(Button button)
    {
        if (!string.IsNullOrEmpty(AutomationProperties.GetName(button))) return;
        // Bind tooltips rather than copying them so language changes remain visible to screen readers.
        if (button.ToolTip is string)
            button.SetBinding(AutomationProperties.NameProperty, new System.Windows.Data.Binding("ToolTip") { Source = button });
        else if (button.Content is string name) AutomationProperties.SetName(button, name);
        else if (Text(button).FirstOrDefault() is { } text)
            button.SetBinding(AutomationProperties.NameProperty, new System.Windows.Data.Binding("Text") { Source = text });
        if (button.DataContext is ItemRow)
            button.SetBinding(AutomationProperties.HelpTextProperty, new System.Windows.Data.Binding("Name"));
    }
    private static void NameInput(TextBox input)
    {
        if (input.ToolTip is string && string.IsNullOrEmpty(AutomationProperties.GetName(input)))
            input.SetBinding(AutomationProperties.NameProperty, new System.Windows.Data.Binding("ToolTip") { Source = input });
    }
}
