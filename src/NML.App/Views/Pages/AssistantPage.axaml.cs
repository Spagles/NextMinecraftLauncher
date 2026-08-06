using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;

namespace NML.App.Views.Pages;

public partial class AssistantPage : UserControl
{
    public AssistantPage()
    {
        InitializeComponent();

        // Auto-scroll to bottom when new chat messages arrive.
        // Subscribe to the Conversation collection's changes after DataContext is set.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.Pages.AssistantPageViewModel vm)
            {
                vm.Conversation.CollectionChanged += OnConversationChanged;
            }
        };
    }

    private void OnConversationChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // Defer the scroll to the next render frame so the new item is laid out.
            DispatcherTimer.RunOnce(() =>
            {
                ChatScroll.ScrollToEnd();
            }, TimeSpan.FromMilliseconds(50));
        }
    }
}
