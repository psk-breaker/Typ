using System.IO;
using Microsoft.Win32;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace Writing_App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly string DataFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project_data.json");
    public ObservableCollection<Novel> Novels { get; set; } = new ObservableCollection<Novel>();
    private DraftSection activeSection = null;
    private Chapter activeChapter = null;
    private object structuralClipboard = null;
    private Point chapterDragStartPoint;
    private int dropIndicatorIndex = -1;

    private enum SelectionContext
    {
        Novel,
        Section,
        Chapter
    }

    private SelectionContext currentSelectionContext = SelectionContext.Novel;

    private TextPointer lockedPosition;
    private bool isMovingCaret = false;
    private DispatcherTimer countdownTimer;
    private int remainingSeconds = 0;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTimer();
        //LoadSampleData();
        LoadProjectData();

        // Start with the settings sidebar invisible (opacity 0) but present so hover works
        Sidebar.Visibility = Visibility.Visible;
        Sidebar.Opacity = 0.0;

        Editor.Focus();
        lockedPosition = Editor.Document.ContentStart;

        NovelsListBox.PreviewMouseLeftButtonDown += (s, e) => currentSelectionContext = SelectionContext.Novel;
        SectionsListBox.PreviewMouseLeftButtonDown += (s, e) => currentSelectionContext = SelectionContext.Section;
        ChaptersListBox.PreviewMouseLeftButtonDown += (s, e) => currentSelectionContext = SelectionContext.Chapter;

        Editor.PreviewKeyDown += Editor_PreviewKeyDown;
        Editor.PreviewMouseDown += Editor_PreviewMouseDown;
        Editor.SelectionChanged += Editor_SelectionChanged;
        Editor.ContextMenu = null;

        DataObject.AddPastingHandler(Editor, OnPaste);
        CommandManager.AddPreviewExecutedHandler(Editor, OnPreviewCommandExecuted);

        Editor.TextChanged += Editor_TextChanged;
        TargetWordBox.TextChanged += TargetWordBox_TextChanged;
        MinutesBox.TextChanged += MinutesBox_TextChanged;

        this.StateChanged += MainWindow_StateChanged;
        this.Closing += MainWindow_Closing;
    }

    private void ChaptersListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        chapterDragStartPoint = e.GetPosition(null);
    }

    private void ChaptersListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        Point currentPosition = e.GetPosition(null);
        Vector diff = chapterDragStartPoint - currentPosition;

        if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance && Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
            return;

        var source = e.OriginalSource as DependencyObject;
        if (GetListBoxItemUnderMouse(ChaptersListBox, source) is not ListBoxItem listBoxItem)
            return;

        if (listBoxItem.DataContext is not Chapter draggedChapter)
            return;

        ChaptersListBox.SelectedItem = draggedChapter;
        DragDrop.DoDragDrop(ChaptersListBox, draggedChapter, DragDropEffects.Move);
    }

    private void ChaptersListBox_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Chapter)) && sender == ChaptersListBox)
        {
            e.Effects = DragDropEffects.Move;
            
            // Get the position relative to the listbox
            Point dropPosition = e.GetPosition(ChaptersListBox);
            UpdateDropIndicatorPosition(dropPosition);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            HideDropIndicator();
        }

        e.Handled = true;
    }

    private void ChaptersListBox_DragLeave(object sender, DragEventArgs e)
    {
        HideDropIndicator();
    }

    private void UpdateDropIndicatorPosition(Point dropPosition)
    {
        if (ChaptersListBox.Items.Count == 0)
        {
            HideDropIndicator();
            return;
        }

        DropIndicatorBar.Visibility = Visibility.Visible;
        int targetIndex = -1;

        // Iterate through visible items to find which one we're hovering over
        for (int i = 0; i < ChaptersListBox.Items.Count; i++)
        {
            var item = ChaptersListBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            if (item == null)
                continue;

            var itemBounds = item.TransformToAncestor(ChaptersListBox).Transform(new Point(0, 0));
            double itemHeight = item.ActualHeight;
            double itemMiddle = itemBounds.Y + itemHeight / 2;

            if (dropPosition.Y < itemMiddle)
            {
                targetIndex = i;
                break;
            }
        }

        // If we didn't find an item (past all items), insert at the end
        if (targetIndex < 0)
            targetIndex = ChaptersListBox.Items.Count;

        dropIndicatorIndex = targetIndex;

        // Position the indicator bar
        if (targetIndex < ChaptersListBox.Items.Count)
        {
            var item = ChaptersListBox.ItemContainerGenerator.ContainerFromIndex(targetIndex) as ListBoxItem;
            if (item != null)
            {
                var itemBounds = item.TransformToAncestor(ChaptersListBox).Transform(new Point(0, 0));
                DropIndicatorBar.Margin = new Thickness(5, itemBounds.Y - 1, 5, 0);
            }
        }
        else
        {
            // Position at the end
            if (ChaptersListBox.Items.Count > 0)
            {
                var lastItem = ChaptersListBox.ItemContainerGenerator.ContainerFromIndex(ChaptersListBox.Items.Count - 1) as ListBoxItem;
                if (lastItem != null)
                {
                    var itemBounds = lastItem.TransformToAncestor(ChaptersListBox).Transform(new Point(0, 0));
                    DropIndicatorBar.Margin = new Thickness(5, itemBounds.Y + lastItem.ActualHeight - 1, 5, 0);
                }
            }
        }
    }

    private void HideDropIndicator()
    {
        DropIndicatorBar.Visibility = Visibility.Collapsed;
        dropIndicatorIndex = -1;
    }

    private void ChaptersListBox_Drop(object sender, DragEventArgs e)
    {
        HideDropIndicator();

        if (!e.Data.GetDataPresent(typeof(Chapter)) || sender != ChaptersListBox)
            return;

        var droppedChapter = e.Data.GetData(typeof(Chapter)) as Chapter;
        if (droppedChapter == null || activeSection == null)
            return;

        var targetItem = GetListBoxItemUnderMouse(ChaptersListBox, e.OriginalSource as DependencyObject);
        var targetChapter = targetItem?.DataContext as Chapter;

        int oldIndex = activeSection.Chapters.IndexOf(droppedChapter);
        int newIndex = targetChapter != null
            ? activeSection.Chapters.IndexOf(targetChapter)
            : activeSection.Chapters.Count - 1;

        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
            return;

        SaveActiveChapterContent();
        activeSection.Chapters.Move(oldIndex, newIndex);
        ChaptersListBox.SelectedItem = droppedChapter;
        SaveProjectData();
    }

    private static ListBoxItem? GetListBoxItemUnderMouse(ListBox listBox, DependencyObject? originalSource)
    {
        DependencyObject? current = originalSource;
        while (current != null && current != listBox)
        {
            if (current is ListBoxItem item)
                return item;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void FilesToggleButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Toggle the outer column width that holds the files navigation
            if (SidebarColumn.Width.Value == 0)
            {
                SidebarColumn.Width = GridLength.Auto;
                FilesSidebar.Visibility = Visibility.Visible;
            }
            else
            {
                SidebarColumn.Width = new GridLength(0);
                FilesSidebar.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            // Swallow any exceptions to avoid disrupting UI; toggling is non-critical
        }
    }

    private void TargetPopupButton_Click(object sender, RoutedEventArgs e)
    {
        CountdownPopup.IsOpen = false;
        TargetPopup.IsOpen = !TargetPopup.IsOpen;
        if (TargetPopup.IsOpen)
        {
            TargetWordBox.Focus();
        }
    }

    private void CountdownPopupButton_Click(object sender, RoutedEventArgs e)
    {
        TargetPopup.IsOpen = false;
        CountdownPopup.IsOpen = !CountdownPopup.IsOpen;
        if (CountdownPopup.IsOpen)
        {
            MinutesBox.Focus();
        }
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();

        if (activeSection != null && !activeSection.IsDeletionAllowed)
        {
        
            if (e.Key == Key.Back)
            {
                if (Editor.CaretPosition.CompareTo(lockedPosition) <= 0)
                {
                    e.Handled = true;
                }
            }

            if (e.Key == Key.Space)
            {
                TextPointer nextPosition = Editor.CaretPosition.GetPositionAtOffset(1);
                if (nextPosition != null)
                {
                    lockedPosition = nextPosition;
                }
            }

            if (e.Key == Key.Enter)
            {
                TextPointer nextLinePosition = Editor.CaretPosition.GetPositionAtOffset(2);
                if (nextLinePosition != null)
                {
                    lockedPosition = nextLinePosition;
                }
                else
                {
                    // Fallback to end of document if offset math fails at document boundaries
                    lockedPosition = Editor.Document.ContentEnd;
                }
            }
        }        
    }

    private void Editor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (activeSection != null && !activeSection.IsDeletionAllowed)
        {
            if (!isMovingCaret)
            {
                MoveCaretToEnd();
            }

            e.Handled = true;
        }
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (isMovingCaret) return;

        if (activeSection != null && !activeSection.IsDeletionAllowed)
        {
            // If user tries to click/arrow behind the lock, snap them back
            if (Editor.CaretPosition.CompareTo(lockedPosition) < 0)
            {
                MoveCaretToEnd();
            }
        } 
    }

    private void MoveCaretToEnd()
    {
        isMovingCaret = true;
        Editor.CaretPosition = Editor.Document.ContentEnd;
        Editor.Selection.Select(Editor.Document.ContentEnd, Editor.Document.ContentEnd);
        Editor.CaretPosition = Editor.Document.ContentEnd;
        Editor.Focus();
        isMovingCaret = false;
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (activeSection != null && !activeSection.IsDeletionAllowed)
        {
            if (Editor.CaretPosition.CompareTo(lockedPosition) <= 0)
            {
                e.CancelCommand();
                e.Handled = true;
            }
        }
    }

    private void OnPreviewCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (activeSection != null && !activeSection.IsDeletionAllowed)
        {
            if (e.Command == ApplicationCommands.Cut || e.Command == ApplicationCommands.Delete)
            {
                if (Editor.CaretPosition.CompareTo(lockedPosition) <= 0 || !Editor.Selection.IsEmpty)
                {
                    e.Handled = true;
                }
            }
        }
    }

    private string GetEditorText()
    {
        TextRange textRange = new TextRange(
            Editor.Document.ContentStart,
            Editor.Document.ContentEnd);

        return textRange.Text;
    }

    private List<T> GetSelectedItems<T>(ListBox listBox)
    {
        return listBox.SelectedItems.Cast<T>().ToList();
    }

    private void MoveSelectedItemsUp<T>(IList<T> items, List<T> selectedItems)
    {
        var selectedSet = new HashSet<T>(selectedItems);

        foreach (var entry in selectedItems
            .Select(item => (Item: item, Index: items.IndexOf(item)))
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index))
        {
            int currentIndex = items.IndexOf(entry.Item);
            if (currentIndex > 0 && !selectedSet.Contains(items[currentIndex - 1]))
            {
                items.RemoveAt(currentIndex);
                items.Insert(currentIndex - 1, entry.Item);
            }
        }
    }

    private void MoveSelectedItemsDown<T>(IList<T> items, List<T> selectedItems)
    {
        var selectedSet = new HashSet<T>(selectedItems);

        foreach (var entry in selectedItems
            .Select(item => (Item: item, Index: items.IndexOf(item)))
            .Where(x => x.Index >= 0)
            .OrderByDescending(x => x.Index))
        {
            int currentIndex = items.IndexOf(entry.Item);
            if (currentIndex < items.Count - 1 && !selectedSet.Contains(items[currentIndex + 1]))
            {
                items.RemoveAt(currentIndex);
                items.Insert(currentIndex + 1, entry.Item);
            }
        }
    }

    private void DeleteSelectedChapters()
    {
        if (activeSection == null) return;

        var selectedChapters = GetSelectedItems<Chapter>(ChaptersListBox);
        if (selectedChapters.Count == 0) return;

        if (!activeSection.IsDeletionAllowed)
        {
            var result = MessageBox.Show(
                        $"Delete {selectedChapters.Count} chapter(s) from this protected section? This will remove protected content permanently."
                        );
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        SaveActiveChapterContent();
        foreach (var chapter in selectedChapters.ToList())
        {
            activeSection.Chapters.Remove(chapter);
        }

        if (activeSection.Chapters.Count > 0)
        {
            ChaptersListBox.SelectedIndex = 0;
        }
        else
        {
            Editor.Document.Blocks.Clear();
        }
    }

    private void DeleteSelectedSections()
    {
        if (NovelsListBox.SelectedItem is not Novel currentNovel) return;

        var selectedSections = GetSelectedItems<DraftSection>(SectionsListBox);
        if (selectedSections.Count == 0) return;

        var protectedSections = selectedSections.Where(s => !s.IsDeletionAllowed).ToList();
        if (protectedSections.Any())
        {
            var result = MessageBox.Show(
                $"Delete {protectedSections.Count} protected section(s) and their chapters? This action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        foreach (var section in selectedSections.ToList())
        {
            currentNovel.Sections.Remove(section);
        }

        SectionsListBox.SelectedIndex = currentNovel.Sections.Count > 0 ? 0 : -1;
    }

    private void DeleteSelectedNovels()
    {
        var selectedNovels = GetSelectedItems<Novel>(NovelsListBox);
        if (selectedNovels.Count == 0) return;

        var result = MessageBox.Show($"Delete {selectedNovels.Count} selected novel(s)?", "Confirm Delete", MessageBoxButton.YesNo);
        if (result != MessageBoxResult.Yes) return;

        foreach (var novel in selectedNovels.ToList())
        {
            Novels.Remove(novel);
        }

        NovelsListBox.SelectedIndex = Novels.Count > 0 ? 0 : -1;
    }

    private void CopySelectedNovels()
    {
        var selectedNovels = GetSelectedItems<Novel>(NovelsListBox);
        if (selectedNovels.Count == 0) return;
        if (selectedNovels.Count == 1)
        {
            structuralClipboard = selectedNovels[0].Clone();
            return;
        }

        structuralClipboard = selectedNovels.Select(n => n.Clone()).ToList();
    }

    private void CopySelectedSections()
    {
        var selectedSections = GetSelectedItems<DraftSection>(SectionsListBox);
        if (selectedSections.Count == 0) return;
        if (selectedSections.Count == 1)
        {
            structuralClipboard = selectedSections[0].Clone();
            return;
        }

        structuralClipboard = selectedSections.Select(s => s.Clone()).ToList();
    }

    private void CopySelectedChapters()
    {
        var selectedChapters = GetSelectedItems<Chapter>(ChaptersListBox);
        if (selectedChapters.Count == 0) return;
        if (selectedChapters.Count == 1)
        {
            structuralClipboard = selectedChapters[0].Clone();
            return;
        }

        structuralClipboard = selectedChapters.Select(c => c.Clone()).ToList();
    }

    private void CopyNovel_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedNovels();
    }

    private void CopySection_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedSections();
    }

    private void CopyChapter_Click(object sender, RoutedEventArgs e)
    {
        SaveActiveChapterContent();
        CopySelectedChapters();
    }

    private void PasteNovel_Click(object sender, RoutedEventArgs e)
    {
        if (structuralClipboard is List<Novel> copiedNovels)
        {
            int insertIndex = Novels.Count;
            if (NovelsListBox.SelectedItems.Count > 0)
            {
                var selected = GetSelectedItems<Novel>(NovelsListBox);
                insertIndex = Novels.IndexOf(selected.Last()) + 1;
            }

            foreach (var copied in copiedNovels)
            {
                var pasted = copied.Clone();
                if (insertIndex >= 0 && insertIndex <= Novels.Count)
                {
                    Novels.Insert(insertIndex++, pasted);
                }
                else
                {
                    Novels.Add(pasted);
                }
            }

            return;
        }

        if (structuralClipboard is Novel copiedNovel)
        {
            var pasted = copiedNovel.Clone();
            if (NovelsListBox.SelectedItem is Novel selectedNovel)
            {
                int insertIndex = Novels.IndexOf(selectedNovel) + 1;
                Novels.Insert(insertIndex, pasted);
            }
            else
            {
                Novels.Add(pasted);
            }
        }
    }

    private void PasteSection_Click(object sender, RoutedEventArgs e)
    {
        if (NovelsListBox.SelectedItem is not Novel currentNovel) return;

        if (structuralClipboard is List<DraftSection> copiedSections)
        {
            int insertIndex = currentNovel.Sections.Count;
            if (SectionsListBox.SelectedItems.Count > 0)
            {
                var selected = GetSelectedItems<DraftSection>(SectionsListBox);
                insertIndex = currentNovel.Sections.IndexOf(selected.Last()) + 1;
            }

            foreach (var copied in copiedSections)
            {
                var pasted = copied.Clone();
                if (insertIndex >= 0 && insertIndex <= currentNovel.Sections.Count)
                {
                    currentNovel.Sections.Insert(insertIndex++, pasted);
                }
                else
                {
                    currentNovel.Sections.Add(pasted);
                }
            }

            return;
        }

        if (structuralClipboard is DraftSection copiedSection)
        {
            var pasted = copiedSection.Clone();
            if (SectionsListBox.SelectedItem is DraftSection selectedSection)
            {
                int insertIndex = currentNovel.Sections.IndexOf(selectedSection) + 1;
                currentNovel.Sections.Insert(insertIndex, pasted);
            }
            else
            {
                currentNovel.Sections.Add(pasted);
            }
        }
    }

    private void PasteChapter_Click(object sender, RoutedEventArgs e)
    {
        if (activeSection == null) return;

        if (structuralClipboard is List<Chapter> copiedChapters)
        {
            int insertIndex = activeSection.Chapters.Count;
            if (ChaptersListBox.SelectedItems.Count > 0)
            {
                var selected = GetSelectedItems<Chapter>(ChaptersListBox);
                insertIndex = activeSection.Chapters.IndexOf(selected.Last()) + 1;
            }

            foreach (var copied in copiedChapters)
            {
                var pasted = copied.Clone();
                if (insertIndex >= 0 && insertIndex <= activeSection.Chapters.Count)
                {
                    activeSection.Chapters.Insert(insertIndex++, pasted);
                }
                else
                {
                    activeSection.Chapters.Add(pasted);
                }
            }

            return;
        }

        if (structuralClipboard is Chapter copiedChapter)
        {
            var pasted = copiedChapter.Clone();
            if (ChaptersListBox.SelectedItem is Chapter selectedChapter)
            {
                int insertIndex = activeSection.Chapters.IndexOf(selectedChapter) + 1;
                activeSection.Chapters.Insert(insertIndex, pasted);
            }
            else
            {
                activeSection.Chapters.Add(pasted);
            }
        }
    }

    private void UpdateWordCount()
    {
        string text = GetEditorText();

        string[] words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        int currentWords = words.Length;

        int targetWords = 0;

        int.TryParse(TargetWordBox.Text, out targetWords);

        WordCountText.Text = $"{currentWords} / {targetWords}";
        FloatingWordCountText.Text = $"{currentWords} words";
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateWordCount();
        if (GetEditorText().Length > 5 && Sidebar.Opacity > 0 && !Sidebar.IsMouseOver && !TargetPopup.IsOpen && !CountdownPopup.IsOpen)
        {
            // Smoothly fade to 0 opacity over 0.5 seconds
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.5)
            };
            fadeOut.Completed += (s, a) => 
            {
                // Keep the settings sidebar present but fully transparent so hover can reveal it
                Sidebar.Opacity = 0.0;
                Sidebar.IsHitTestVisible = true;
            };
            Sidebar.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
}

    private void TargetPopup_Opened(object sender, EventArgs e)
    {
        // Stop any running opacity animation and ensure the settings sidebar stays visible
        Sidebar.BeginAnimation(UIElement.OpacityProperty, null);
        Sidebar.Opacity = 1.0;
        Sidebar.IsHitTestVisible = true;
    }

    private void TargetPopup_Closed(object sender, EventArgs e)
    {
        // When popup closes, allow normal fade behaviour if the mouse isn't over
        if (!Sidebar.IsMouseOver && GetEditorText().Length > 5)
        {
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.5)
            };
            Sidebar.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }

    private void CountdownPopup_Opened(object sender, EventArgs e)
    {
        Sidebar.BeginAnimation(UIElement.OpacityProperty, null);
        Sidebar.Opacity = 1.0;
        Sidebar.IsHitTestVisible = true;
    }

    private void CountdownPopup_Closed(object sender, EventArgs e)
    {
        if (!Sidebar.IsMouseOver && GetEditorText().Length > 5)
        {
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.5)
            };
            Sidebar.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }

    private void Sidebar_MouseEnter(object sender, MouseEventArgs e)
    {
        Sidebar.BeginAnimation(UIElement.OpacityProperty, null);
        Sidebar.Opacity = 1.0;
        Sidebar.IsHitTestVisible = true;
    }

    private void Sidebar_MouseLeave(object sender, MouseEventArgs e)
    {
        // If either popup is open, keep the sidebar visible
        if (TargetPopup.IsOpen || CountdownPopup.IsOpen) return;

        if (GetEditorText().Length > 5)
        {
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.5)
            };
            fadeOut.Completed += (s, a) =>
            {
                Sidebar.Opacity = 0.0;
                Sidebar.IsHitTestVisible = true;
            };
            Sidebar.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }

    private void TargetWordBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateWordCount();
    }


// ============================================================================================


    private void InitializeTimer()
    {
        int minutes = 30;

        int.TryParse(MinutesBox.Text, out minutes);

        remainingSeconds = minutes * 60;

        countdownTimer = new DispatcherTimer();

        countdownTimer.Interval = TimeSpan.FromSeconds(1);

        countdownTimer.Tick += CountdownTimer_Tick;

        countdownTimer.Start();

        UpdateTimerDisplay();
    }

    private void CountdownTimer_Tick(object sender, EventArgs e)
    {
        if (remainingSeconds > 0)
        {
            remainingSeconds--;

            UpdateTimerDisplay();
        }
    }

    private void UpdateTimerDisplay()
    {
        TimeSpan time = TimeSpan.FromSeconds(remainingSeconds);

        TimerText.Text = time.ToString(@"mm\:ss");
    }

    private void ResetTimer()
    {
        int minutes = 30;

        int.TryParse(MinutesBox.Text, out minutes);

        remainingSeconds = minutes * 60;

        UpdateTimerDisplay();
    }

    private void MinutesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ResetTimer();
    }

    private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        // No-op: external toggle removed. Files toggle lives in settings overlay.
    }

    private void WindowTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MainWindow_StateChanged(object sender, EventArgs e)
    {
        if (MaximizeButton != null)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
        }
    }

// =================================================================================================
//             --- SAVE FUNCTIONALITY ---
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveWork();
    }

    private void SaveWork()
    {
        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt",
            FileName = $"Writing_{DateTime.Now:yyyyMMdd_HHmm}.txt"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            File.WriteAllText(saveFileDialog.FileName, GetEditorText());
            MessageBox.Show("Work saved!");
        }
        
        SaveProjectData();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveWork();
        }
    }

// =========================================================================================

    private void LoadSampleData()
    {
        // Setup default system structures matching your constraints
        Novel myNovel = new Novel { Title = "Epic Novel" };
        
        myNovel.Sections.Add(CreateDraftSection("Notes", true));
        myNovel.Sections.Add(CreateDraftSection("Sketch", true));
        myNovel.Sections.Add(CreateDraftSection("Logic", true));
        myNovel.Sections.Add(CreateDraftSection("Prose", true));
        myNovel.Sections.Add(CreateDraftSection("Final", true));

        // Generate an opening element inside Sketch
        myNovel.Sections[1].Chapters[0].Title = "Chapter 1";
        myNovel.Sections[1].Chapters[0].Content = "This is a starting point for your sketch...";
        myNovel.Sections[1].Chapters.Add(new Chapter { Title = "Chapter 2", Content = "Beware. . ." });

        Novels.Add(myNovel);
        NovelsListBox.ItemsSource = Novels;
        NovelsListBox.SelectedIndex = 0;
    }

    private void NovelsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NovelsListBox.SelectedItem is Novel selectedNovel)
        {
            SectionsListBox.ItemsSource = selectedNovel.Sections;
            SectionsListBox.SelectedIndex = 0;
        }
    }

    private void SectionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Save current progress before switching tracks
        SaveActiveChapterContent();

        if (SectionsListBox.SelectedItem is DraftSection selectedSection)
        {
            activeSection = selectedSection;
            ChaptersListBox.ItemsSource = selectedSection.Chapters;
            ChaptersListBox.SelectedIndex = 0;

            PromoteButton.Visibility = CanPromoteActiveSection() ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private bool CanPromoteActiveSection()
    {
        if (NovelsListBox.SelectedItem is not Novel currentNovel || activeSection == null)
        {
            return false;
        }

        int currentIndex = currentNovel.Sections.IndexOf(activeSection);
        return currentIndex >= 0 && currentIndex < currentNovel.Sections.Count - 1;
    }

    private void ChaptersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveActiveChapterContent();

        if (ChaptersListBox.SelectedItem is Chapter selectedChapter)
        {
            activeChapter = selectedChapter;
            
            // Swap active text document
            FlowDocument doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run(activeChapter.Content)));
            Editor.Document = doc;

            // Reset strict typing barriers to index zero for the fresh sheet
            lockedPosition = Editor.Document.ContentStart;
            MoveCaretToEnd();
        }
    }

    private void NovelsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (NovelsListBox.SelectedItem is Novel selectedNovel)
        {
            RenameItem("Rename Novel", selectedNovel.Title, newTitle => selectedNovel.Title = newTitle);
            NovelsListBox.Items.Refresh();
        }
    }

    private void SectionsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SectionsListBox.SelectedItem is DraftSection selectedSection)
        {
            RenameItem("Rename Draft Section", selectedSection.Title, newTitle => selectedSection.Title = newTitle);
            SectionsListBox.Items.Refresh();
        }
    }

    private void ChaptersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ChaptersListBox.SelectedItem is Chapter selectedChapter)
        {
            RenameItem("Rename Chapter", selectedChapter.Title, newTitle => selectedChapter.Title = newTitle);
            ChaptersListBox.Items.Refresh();
        }
    }

    private void RenameItem(string prompt, string currentName, System.Action<string> applyName)
    {
        var dialog = new RenameDialog(prompt, currentName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            applyName(dialog.ResponseText);
        }
    }

    private void SaveActiveChapterContent()
    {
        if (activeChapter != null)
        {
            activeChapter.Content = GetEditorText();
        }
    }

    private void PromoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (activeSection == null) return;

        if (NovelsListBox.SelectedItem is not Novel currentNovel) return;

        int currentSectionIndex = currentNovel.Sections.IndexOf(activeSection);
        if (currentSectionIndex < 0 || currentSectionIndex >= currentNovel.Sections.Count - 1)
        {
            return;
        }

        var nextSection = currentNovel.Sections[currentSectionIndex + 1];
        if (nextSection == null) return;

        SaveActiveChapterContent();

        var chaptersToPromote = GetSelectedItems<Chapter>(ChaptersListBox);
        if (chaptersToPromote.Count == 0 && activeChapter != null)
        {
            chaptersToPromote.Add(activeChapter);
        }

        if (chaptersToPromote.Count == 0) return;

        chaptersToPromote = chaptersToPromote
            .Where(ch => activeSection.Chapters.Contains(ch))
            .OrderBy(ch => activeSection.Chapters.IndexOf(ch))
            .ToList();

        foreach (var chapter in chaptersToPromote)
        {
            var promotedChapter = new Chapter
            {
                Title = chapter.Title,
                Content = chapter.Content
            };

            nextSection.Chapters.Add(promotedChapter);
        }
    }

// =============================================================================================================
//                            NOVEL LEVEL MANAGEMENT

    private void AddNovel_Click(object sender, RoutedEventArgs e)
    {
        string title = "New Novel " + (Novels.Count + 1);
        var newNovel = new Novel { Title = title };
        
        // Always pre-populate the pipeline sections
        newNovel.Sections.Add(CreateDraftSection("Notes", true));
        newNovel.Sections.Add(CreateDraftSection("Sketch", true));
        newNovel.Sections.Add(CreateDraftSection("Logic", true));
        newNovel.Sections.Add(CreateDraftSection("Prose", true));
        newNovel.Sections.Add(CreateDraftSection("Final", true));
        
        Novels.Add(newNovel);
        NovelsListBox.SelectedItem = newNovel;
    }

    private void DeleteNovel_Click(object sender, RoutedEventArgs e)
    {
        if (NovelsListBox.SelectedItem is Novel selectedNovel)
        {
            var result = MessageBox.Show($"Are you sure you want to delete '{selectedNovel.Title}'?", "Confirm Delete", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                Novels.Remove(selectedNovel);
            }
        }
    }

    // ==========================================
    // 2. DRAFT SECTION LEVEL MANAGEMENT

    private DraftSection CreateDraftSection(string title, bool isDeletionAllowed)
    {
        var section = new DraftSection
        {
            Title = title,
            IsDeletionAllowed = isDeletionAllowed
        };

        section.Chapters.Add(new Chapter { Title = "Chapter 1", Content = "" });
        return section;
    }

    private void AddSection_Click(object sender, RoutedEventArgs e)
    {
        if (NovelsListBox.SelectedItem is Novel currentNovel)
        {
            string name = "Draft " + (currentNovel.Sections.Count + 1);
            var sec = CreateDraftSection(name, true);

            if (SectionsListBox.SelectedItem is DraftSection selectedSection)
            {
                int insertIndex = currentNovel.Sections.IndexOf(selectedSection) + 1;
                if (insertIndex >= 0 && insertIndex <= currentNovel.Sections.Count)
                {
                    currentNovel.Sections.Insert(insertIndex, sec);
                    SectionsListBox.SelectedItem = sec;
                    return;
                }
            }

            currentNovel.Sections.Add(sec);
            SectionsListBox.SelectedItem = sec;
        }
    }

    private void DeleteSection_Click(object sender, RoutedEventArgs e)
    {
        if (NovelsListBox.SelectedItem is Novel currentNovel && SectionsListBox.SelectedItem is DraftSection selectedSection)
        {
            if (!selectedSection.IsDeletionAllowed)
            {
                var result = MessageBox.Show(
                    $"Delete '{selectedSection.Title}' and all its chapters? Draft 1 and Draft 1 copies can be removed with confirmation.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            currentNovel.Sections.Remove(selectedSection);
        }
    }

    // ==========================================
    // 3. CHAPTER LEVEL MANAGEMENT

    private void AddChapter_Click(object sender, RoutedEventArgs e)
    {
        if (activeSection != null)
        {
            string name = "Chapter " + (activeSection.Chapters.Count + 1);
            var ch = new Chapter { Title = name, Content = "" };

            if (ChaptersListBox.SelectedItem is Chapter selectedChapter)
            {
                int insertIndex = activeSection.Chapters.IndexOf(selectedChapter) + 1;
                if (insertIndex >= 0 && insertIndex <= activeSection.Chapters.Count)
                {
                    activeSection.Chapters.Insert(insertIndex, ch);
                    ChaptersListBox.SelectedItem = ch;
                    return;
                }
            }

            activeSection.Chapters.Add(ch);
            ChaptersListBox.SelectedItem = ch;
        }
    }

    private void DeleteChapter_Click(object sender, RoutedEventArgs e)
    {
        if (activeSection != null && ChaptersListBox.SelectedItem is Chapter selectedChapter)
        {
            if (!activeSection.IsDeletionAllowed)
            {
                var result = MessageBox.Show(
                            $"Delete '{selectedChapter.Title}' from this protected section? This will permanently remove the chapter."
                            );
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            activeSection.Chapters.Remove(selectedChapter);
            Editor.Document.Blocks.Clear();
        }
    }

    // ==========================================
    // 4. CHAPTER REORDERING ENGINE (INDEX SHIFTING)

    private void MoveChapterUp_Click(object sender, RoutedEventArgs e)
    {
        if (activeSection == null || ChaptersListBox.SelectedItem is not Chapter selectedChapter) return;

        int index = activeSection.Chapters.IndexOf(selectedChapter);
        if (index > 0) // Cannot move the top item up
        {
            activeSection.Chapters.RemoveAt(index);
            activeSection.Chapters.Insert(index - 1, selectedChapter);
            ChaptersListBox.SelectedItem = selectedChapter;
        }
    }

    private void MoveChapterDown_Click(object sender, RoutedEventArgs e)
    {
        if (activeSection == null || ChaptersListBox.SelectedItem is not Chapter selectedChapter) return;

        int index = activeSection.Chapters.IndexOf(selectedChapter);
        if (index < activeSection.Chapters.Count - 1) // Cannot move the bottom item down
        {
            activeSection.Chapters.RemoveAt(index);
            activeSection.Chapters.Insert(index + 1, selectedChapter);
            ChaptersListBox.SelectedItem = selectedChapter;
        }
    }

    private void GenericAdd_Click(object sender, RoutedEventArgs e)
    {
        switch (currentSelectionContext)
        {
            case SelectionContext.Chapter:
                if (ChaptersListBox.SelectedItem is Chapter)
                {
                    AddChapter_Click(sender, e);
                    return;
                }
                break;
            case SelectionContext.Section:
                if (SectionsListBox.SelectedItem is DraftSection)
                {
                    AddSection_Click(sender, e);
                    return;
                }
                break;
            case SelectionContext.Novel:
                AddNovel_Click(sender, e);
                return;
        }

        if (ChaptersListBox.SelectedItem is Chapter)
        {
            AddChapter_Click(sender, e);
            return;
        }

        if (SectionsListBox.SelectedItem is DraftSection)
        {
            AddSection_Click(sender, e);
            return;
        }

        AddNovel_Click(sender, e);
    }

    private void GenericDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ChaptersListBox.SelectedItems.Count > 1)
        {
            DeleteSelectedChapters();
            return;
        }

        if (SectionsListBox.SelectedItems.Count > 1)
        {
            DeleteSelectedSections();
            return;
        }

        if (NovelsListBox.SelectedItems.Count > 1)
        {
            DeleteSelectedNovels();
            return;
        }

        switch (currentSelectionContext)
        {
            case SelectionContext.Chapter:
                if (ChaptersListBox.SelectedItem is Chapter)
                {
                    DeleteChapter_Click(sender, e);
                    return;
                }
                break;
            case SelectionContext.Section:
                if (SectionsListBox.SelectedItem is DraftSection)
                {
                    DeleteSection_Click(sender, e);
                    return;
                }
                break;
            case SelectionContext.Novel:
                DeleteNovel_Click(sender, e);
                return;
        }

        if (ChaptersListBox.SelectedItem is Chapter)
        {
            DeleteChapter_Click(sender, e);
            return;
        }

        if (SectionsListBox.SelectedItem is DraftSection)
        {
            DeleteSection_Click(sender, e);
            return;
        }

        DeleteNovel_Click(sender, e);
    }

    private void GenericCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ChaptersListBox.SelectedItems.Count > 1)
        {
            CopySelectedChapters();
            return;
        }

        if (SectionsListBox.SelectedItems.Count > 1)
        {
            CopySelectedSections();
            return;
        }

        if (NovelsListBox.SelectedItems.Count > 1)
        {
            CopySelectedNovels();
            return;
        }

        switch (currentSelectionContext)
        {
            case SelectionContext.Chapter:
                if (ChaptersListBox.SelectedItem is Chapter)
                {
                    CopyChapter_Click(sender, e);
                    return;
                }
                break;
            case SelectionContext.Section:
                if (SectionsListBox.SelectedItem is DraftSection)
                {
                    CopySection_Click(sender, e);
                    return;
                }
                break;
            case SelectionContext.Novel:
                CopyNovel_Click(sender, e);
                return;
        }

        if (ChaptersListBox.SelectedItem is Chapter)
        {
            CopyChapter_Click(sender, e);
            return;
        }

        if (SectionsListBox.SelectedItem is DraftSection)
        {
            CopySection_Click(sender, e);
            return;
        }

        CopyNovel_Click(sender, e);
    }

    private void GenericPaste_Click(object sender, RoutedEventArgs e)
    {
        if (structuralClipboard is List<Chapter>)
        {
            PasteChapter_Click(sender, e);
            return;
        }

        if (structuralClipboard is List<DraftSection>)
        {
            PasteSection_Click(sender, e);
            return;
        }

        if (structuralClipboard is List<Novel>)
        {
            PasteNovel_Click(sender, e);
            return;
        }

        switch (currentSelectionContext)
        {
            case SelectionContext.Chapter:
                if (ChaptersListBox.SelectedItem is Chapter)
                {
                    PasteChapter_Click(sender, e);
                    return;
                }
                break;
            case SelectionContext.Section:
                if (SectionsListBox.SelectedItem is DraftSection)
                {
                    PasteSection_Click(sender, e);
                    return;
                }
                break;
            case SelectionContext.Novel:
                PasteNovel_Click(sender, e);
                return;
        }

        if (ChaptersListBox.SelectedItem is Chapter)
        {
            PasteChapter_Click(sender, e);
            return;
        }

        if (SectionsListBox.SelectedItem is DraftSection)
        {
            PasteSection_Click(sender, e);
            return;
        }

        PasteNovel_Click(sender, e);
    }

    private void GenericMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (ChaptersListBox.SelectedItems.Count > 1 && activeSection != null)
        {
            MoveSelectedItemsUp(activeSection.Chapters, GetSelectedItems<Chapter>(ChaptersListBox));
            return;
        }

        if (SectionsListBox.SelectedItems.Count > 1 && NovelsListBox.SelectedItem is Novel currentNovel)
        {
            MoveSelectedItemsUp(currentNovel.Sections, GetSelectedItems<DraftSection>(SectionsListBox));
            return;
        }

        if (NovelsListBox.SelectedItems.Count > 1)
        {
            MoveSelectedItemsUp(Novels, GetSelectedItems<Novel>(NovelsListBox));
            return;
        }

        if (ChaptersListBox.SelectedItem is Chapter)
        {
            MoveChapterUp_Click(sender, e);
            return;
        }

        if (SectionsListBox.SelectedItem is DraftSection)
        {
            if (NovelsListBox.SelectedItem is Novel parentNovel && SectionsListBox.SelectedItem is DraftSection sel)
            {
                int idx = parentNovel.Sections.IndexOf(sel);
                if (idx > 0)
                {
                    parentNovel.Sections.RemoveAt(idx);
                    parentNovel.Sections.Insert(idx - 1, sel);
                    SectionsListBox.SelectedItem = sel;
                }
            }
            return;
        }

        if (NovelsListBox.SelectedItem is Novel selectedNovel)
        {
            int idx = Novels.IndexOf(selectedNovel);
            if (idx > 0)
            {
                Novels.RemoveAt(idx);
                Novels.Insert(idx - 1, selectedNovel);
                NovelsListBox.SelectedItem = selectedNovel;
            }
        }
    }

    private void GenericMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (ChaptersListBox.SelectedItems.Count > 1 && activeSection != null)
        {
            MoveSelectedItemsDown(activeSection.Chapters, GetSelectedItems<Chapter>(ChaptersListBox));
            return;
        }

        if (SectionsListBox.SelectedItems.Count > 1 && NovelsListBox.SelectedItem is Novel currentNovel)
        {
            MoveSelectedItemsDown(currentNovel.Sections, GetSelectedItems<DraftSection>(SectionsListBox));
            return;
        }

        if (NovelsListBox.SelectedItems.Count > 1)
        {
            MoveSelectedItemsDown(Novels, GetSelectedItems<Novel>(NovelsListBox));
            return;
        }

        if (ChaptersListBox.SelectedItem is Chapter)
        {
            MoveChapterDown_Click(sender, e);
            return;
        }

        if (SectionsListBox.SelectedItem is DraftSection)
        {
            if (NovelsListBox.SelectedItem is Novel parentNovel && SectionsListBox.SelectedItem is DraftSection sel)
            {
                int idx = parentNovel.Sections.IndexOf(sel);
                if (idx < parentNovel.Sections.Count - 1)
                {
                    parentNovel.Sections.RemoveAt(idx);
                    parentNovel.Sections.Insert(idx + 1, sel);
                    SectionsListBox.SelectedItem = sel;
                }
            }
            return;
        }

        if (NovelsListBox.SelectedItem is Novel selectedNovel)
        {
            int idx = Novels.IndexOf(selectedNovel);
            if (idx < Novels.Count - 1)
            {
                Novels.RemoveAt(idx);
                Novels.Insert(idx + 1, selectedNovel);
                NovelsListBox.SelectedItem = selectedNovel;
            }
        }
    }

// ======================================================================================================
//                                       --- PERSISTENCE: LOAD DATA ---
    private void LoadProjectData()
    {
        try
        {
            if (File.Exists(DataFilePath))
            {
                string json = File.ReadAllText(DataFilePath);
                var token = JToken.Parse(json);

                if (token.Type == JTokenType.Object)
                {
                    var projectData = token.ToObject<ProjectData>();
                    if (projectData?.Novels != null && projectData.Novels.Count > 0)
                    {
                        Novels = projectData.Novels;
                        ApplyFileSidebarState(projectData);
                        NovelsListBox.ItemsSource = Novels;
                        NovelsListBox.SelectedIndex = 0;
                        return;
                    }
                }

                if (token.Type == JTokenType.Array)
                {
                    var deserializedNovels = token.ToObject<ObservableCollection<Novel>>();
                    if (deserializedNovels != null && deserializedNovels.Count > 0)
                    {
                        Novels = deserializedNovels;
                        SetDefaultFileSidebarState();
                        NovelsListBox.ItemsSource = Novels;
                        NovelsListBox.SelectedIndex = 0;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading saved workspace data: {ex.Message}\nLoading fallback default setup.", "Load Error");
        }

        LoadDefaultStructure();
    }

    private void LoadDefaultStructure()
    {
        Novel myNovel = new Novel { Title = "My First Novel" };
        
        myNovel.Sections.Add(CreateDraftSection("Notes", true));
        myNovel.Sections.Add(CreateDraftSection("Sketch", true));
        myNovel.Sections.Add(CreateDraftSection("Logic", true));
        myNovel.Sections.Add(CreateDraftSection("Prose", true));
        myNovel.Sections.Add(CreateDraftSection("Final", true));

        myNovel.Sections[1].Chapters[0].Title = "Chapter 1";
        myNovel.Sections[1].Chapters[0].Content = "Welcome to your new sketch...";

        Novels.Add(myNovel);
        NovelsListBox.ItemsSource = Novels;
        NovelsListBox.SelectedIndex = 0;
        SetDefaultFileSidebarState();
    }

    private void ApplyFileSidebarState(ProjectData data)
    {
        if (data.FileSidebarColumnWidths?.Length == 3)
        {
            FilesSidebar.ColumnDefinitions[0].Width = new GridLength(data.FileSidebarColumnWidths[0], GridUnitType.Pixel);
            FilesSidebar.ColumnDefinitions[2].Width = new GridLength(data.FileSidebarColumnWidths[1], GridUnitType.Pixel);
            FilesSidebar.ColumnDefinitions[4].Width = new GridLength(data.FileSidebarColumnWidths[2], GridUnitType.Pixel);
        }
        else
        {
            FilesSidebar.ColumnDefinitions[0].Width = new GridLength(220);
            FilesSidebar.ColumnDefinitions[2].Width = new GridLength(220);
            FilesSidebar.ColumnDefinitions[4].Width = new GridLength(240);
        }

        if (data.IsFilesSidebarVisible)
        {
            SidebarColumn.Width = GridLength.Auto;
            FilesSidebar.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarColumn.Width = new GridLength(0);
            FilesSidebar.Visibility = Visibility.Collapsed;
        }

        if (data.WindowWidth > 0 && data.WindowHeight > 0)
        {
            Width = data.WindowWidth;
            Height = data.WindowHeight;
        }

        if (!string.IsNullOrEmpty(data.WindowStateName) && Enum.TryParse(data.WindowStateName, out WindowState restoredState))
        {
            WindowState = restoredState;
        }
    }

    private void SetDefaultFileSidebarState()
    {
        FilesSidebar.ColumnDefinitions[0].Width = new GridLength(220);
        FilesSidebar.ColumnDefinitions[2].Width = new GridLength(220);
        FilesSidebar.ColumnDefinitions[4].Width = new GridLength(240);
        SidebarColumn.Width = GridLength.Auto;
        FilesSidebar.Visibility = Visibility.Visible;
    }

    // --- PERSISTENCE: SAVE DATA ---
    private void SaveProjectData()
    {
        try
        {
            // Crucial: Capture whatever is currently typed in the editor box right now
            SaveActiveChapterContent();

            var data = new ProjectData
            {
                Novels = Novels,
                FileSidebarColumnWidths = GetFileSidebarColumnWidths(),
                IsFilesSidebarVisible = SidebarColumn.Width.Value != 0 && FilesSidebar.Visibility == Visibility.Visible,
                WindowStateName = WindowState.ToString(),
                WindowWidth = WindowState == WindowState.Normal ? Width : RestoreBounds.Width,
                WindowHeight = WindowState == WindowState.Normal ? Height : RestoreBounds.Height
            };

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(DataFilePath, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to auto-save structural progress: {ex.Message}", "Save Warning");
        }
    }

    private double[] GetFileSidebarColumnWidths()
    {
        return new[]
        {
            FilesSidebar.ColumnDefinitions[0].Width.Value,
            FilesSidebar.ColumnDefinitions[2].Width.Value,
            FilesSidebar.ColumnDefinitions[4].Width.Value
        };
    }

    // Triggered automatically whenever the user closes the app (X button, Alt+F4, etc.)
    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveProjectData();
    }

    private class ProjectData
    {
        public ObservableCollection<Novel> Novels { get; set; } = new ObservableCollection<Novel>();
        public double[] FileSidebarColumnWidths { get; set; } = new[] { 220.0, 220.0, 240.0 };
        public bool IsFilesSidebarVisible { get; set; } = true;
        public double WindowWidth { get; set; } = 1280.0;
        public double WindowHeight { get; set; } = 800.0;
        public string WindowStateName { get; set; } = System.Windows.WindowState.Normal.ToString();
    }

    // ==========================================
    // CONTEXT MENU HANDLERS

    private void DeleteContextMenu_Click(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        if (menuItem?.Tag is string tag)
        {
            switch (tag)
            {
                case "Novel":
                    DeleteSelectedNovels();
                    break;
                case "Section":
                    DeleteSelectedSections();
                    break;
                case "Chapter":
                    DeleteSelectedChapters();
                    break;
            }
        }
        SaveProjectData();
    }

    private void CopyContextMenu_Click(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        if (menuItem?.Tag is string tag)
        {
            switch (tag)
            {
                case "Novel":
                    CopySelectedNovels();
                    break;
                case "Section":
                    CopySelectedSections();
                    break;
                case "Chapter":
                    SaveActiveChapterContent();
                    CopySelectedChapters();
                    break;
            }
        }
    }

    private void PasteContextMenu_Click(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        if (menuItem?.Tag is string tag)
        {
            switch (tag)
            {
                case "Novel":
                    PasteNovel_Click(sender, e);
                    break;
                case "Section":
                    PasteSection_Click(sender, e);
                    break;
                case "Chapter":
                    PasteChapter_Click(sender, e);
                    break;
            }
        }
        SaveProjectData();
    }
}