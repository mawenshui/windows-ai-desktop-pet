using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AiPet.Shortcuts;
using Microsoft.Win32;

namespace AiPet.ToolWindow;

public partial class ShortcutManagerWindow : Window
{
    private readonly ShortcutStore _store;
    private readonly Action<Guid,string> _relocate;
    private readonly Action _changed;
    private CancellationTokenSource? _scan;
    private HashSet<Guid> _invalid=new();
    private Point _dragStart;
    private bool _refreshing;
    private sealed record GroupFilter(string Key,string DisplayName);
    public ShortcutManagerWindow(Window owner,ShortcutStore store,Action<Guid,string> relocate,Action changed)
    {
        InitializeComponent(); Owner=owner; Resources=owner.Resources; _store=store; _relocate=relocate; _changed=changed;
        Background=(Brush)owner.FindResource("Canvas"); Foreground=(Brush)owner.FindResource("Ink");
        Groups.DisplayMemberPath=nameof(GroupFilter.DisplayName);
        var groupStyle=new Style(typeof(ListBoxItem),(Style)owner.FindResource("SegmentInlineChoiceItem"));
        groupStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,6,10,6)));
        Groups.ItemContainerStyle=groupStyle;
        Closed+=(_,_)=> { _scan?.Cancel(); _changed(); };
        Reload();
    }
    private Guid[] SelectedIds()=>Entries.SelectedItems.Cast<ShortcutItem>().Select(item=>item.Id).ToArray();
    private void Reload(IEnumerable<Guid>? selected=null)
    {
        var ids=(selected??SelectedIds()).ToHashSet(); var filter=(Groups.SelectedItem as GroupFilter)?.Key??"all";
        var all=_store.Load(); _refreshing=true;
        try
        {
            Groups.ItemsSource=new[] {new GroupFilter("all","全部"),new GroupFilter("pinned","已固定"),new GroupFilter("none","未分组"),new GroupFilter("invalid","失效结果")}
                .Concat(all.Select(item=>item.Group).Where(group=>!string.IsNullOrEmpty(group)).Distinct().OrderBy(group=>group).Select(group=>new GroupFilter("group:"+group,group))).ToArray();
            Groups.SelectedItem=Groups.Items.Cast<GroupFilter>().FirstOrDefault(group=>group.Key==filter)??Groups.Items[0];
            Entries.ItemsSource=all.Where(item=>filter switch { "all"=>true,"pinned"=>item.Pinned,"none"=>string.IsNullOrEmpty(item.Group),"invalid"=>_invalid.Contains(item.Id),_=>"group:"+item.Group==filter }).ToArray();
            foreach (var item in Entries.Items.Cast<ShortcutItem>().Where(item=>ids.Contains(item.Id))) Entries.SelectedItems.Add(item);
            UndoButton.IsEnabled=_store.CanUndoRemoval;
        }
        finally { _refreshing=false; }
        _changed();
    }
    private void Groups_SelectionChanged(object sender,SelectionChangedEventArgs e) { if (!_refreshing && IsInitialized && _store is not null) Reload(); }
    private void Run(Action action)
    {
        try { action(); Reload(); }
        catch (InvalidOperationException ex) { StatusText.Text=ex.Message; }
        catch { StatusText.Text="操作未完成，请检查数据和文件权限。"; }
    }
    private void Group_Click(object sender,RoutedEventArgs e)=>Run(()=>_store.Organize(SelectedIds(),group:GroupName.Text));
    private void Pin_Click(object sender,RoutedEventArgs e)=>Run(()=>_store.Organize(SelectedIds(),pinned:!Entries.SelectedItems.Cast<ShortcutItem>().All(item=>item.Pinned)));
    private void Remove_Click(object sender,RoutedEventArgs e)
    {
        var preview=_store.PreviewRemoval(SelectedIds()); if (preview.Items.Count==0) return;
        var panel=new DockPanel {Margin=new Thickness(16)};
        var confirm=new Button {Content=$"移除 {preview.Items.Count} 个入口",Style=(Style)FindResource("PrimaryButton"),Margin=new Thickness(0,12,0,0)}; DockPanel.SetDock(confirm,Dock.Bottom); panel.Children.Add(confirm);
        var explanation=new TextBlock {Text="仅移除快捷入口引用；原文件保留。本次运行期间可撤销。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)}; DockPanel.SetDock(explanation,Dock.Top); panel.Children.Add(explanation);
        panel.Children.Add(new ListBox {ItemsSource=preview.Items.Select(item=>$"{item.DisplayName}\n{item.TargetPath}").ToArray(),BorderThickness=new Thickness(0)});
        var dialog=new Window {Owner=this,Title="批量移除预览",Width=480,Height=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=panel,Background=Background,Foreground=Foreground};
        confirm.Click+=(_,_)=>dialog.DialogResult=true;
        if (dialog.ShowDialog()==true) Run(()=> { var count=_store.RemoveMany(preview); StatusText.Text=$"已移除 {count} 个引用，可撤销。"; });
    }
    private void Undo_Click(object sender,RoutedEventArgs e)=>Run(()=>StatusText.Text=$"已恢复 {_store.UndoRemoval()} 个入口。");
    private async void Scan_Click(object sender,RoutedEventArgs e)
    {
        _scan?.Cancel(); var cancellation=new CancellationTokenSource(); _scan=cancellation;
        StatusText.Text="正在检查目标…";
        try
        {
            var invalid=await _store.ScanInvalidAsync(cancellation.Token);
            if (!ReferenceEquals(_scan,cancellation)||!IsVisible) return;
            _invalid=invalid.ToHashSet(); Reload(); Groups.SelectedItem=Groups.Items.Cast<GroupFilter>().First(group=>group.Key=="invalid");
            StatusText.Text=$"找到 {_invalid.Count} 个失效入口；可重新定位或预览移除。";
        }
        catch (OperationCanceledException) { if (ReferenceEquals(_scan,cancellation)) StatusText.Text="检查已取消，入口未改变。"; }
        catch { StatusText.Text="检查未完成，请重试。"; }
        finally { if (ReferenceEquals(_scan,cancellation)) _scan=null; cancellation.Dispose(); }
    }
    private void CancelScan_Click(object sender,RoutedEventArgs e)=>_scan?.Cancel();
    private void Relocate_Click(object sender,RoutedEventArgs e)
    {
        if (Entries.SelectedItems.Count!=1 || Entries.SelectedItem is not ShortcutItem item) { StatusText.Text="请选择一个入口重新定位。"; return; }
        string? target=null;
        if (item.Kind==ShortcutKind.Folder) { var dialog=new OpenFolderDialog(); if (dialog.ShowDialog(this)==true) target=dialog.FolderName; }
        else { var dialog=new OpenFileDialog {CheckFileExists=true}; if (dialog.ShowDialog(this)==true) target=dialog.FileName; }
        if (target is null) return;
        Run(()=> { _relocate(item.Id,target); _invalid.Remove(item.Id); });
    }
    private void Move(int offset)
    {
        if (Entries.SelectedItems.Count!=1 || Entries.SelectedItem is not ShortcutItem source) return;
        var displayed=Entries.Items.Cast<ShortcutItem>().ToList(); var targetIndex=displayed.FindIndex(item=>item.Id==source.Id)+offset;
        if (targetIndex<0||targetIndex>=displayed.Count) return;
        MoveTo(source.Id,displayed[targetIndex].Id);
    }
    private void MoveTo(Guid sourceId,Guid targetId)
    {
        if (sourceId==targetId) return;
        var all=_store.Load().ToList(); var from=all.FindIndex(item=>item.Id==sourceId); var to=all.FindIndex(item=>item.Id==targetId);
        if (from<0||to<0) return;
        if (all[from].Pinned!=all[to].Pinned) { StatusText.Text="固定项始终置顶；先调整固定状态再跨区移动。"; return; }
        var item=all[from]; all.RemoveAt(from); all.Insert(to,item);
        Run(()=>_store.Reorder(all.Select(entry=>entry.Id))); Reload(new[]{sourceId}); Entries.Focus();
    }
    private void MoveUp_Click(object sender,RoutedEventArgs e)=>Move(-1);
    private void MoveDown_Click(object sender,RoutedEventArgs e)=>Move(1);
    private void Entries_PreviewKeyDown(object sender,KeyEventArgs e)
    { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.SystemKey is Key.Up or Key.Down) { Move(e.SystemKey==Key.Up?-1:1); e.Handled=true; } }
    private void Entries_PreviewMouseLeftButtonDown(object sender,MouseButtonEventArgs e)=>_dragStart=e.GetPosition(Entries);
    private void Entries_PreviewMouseMove(object sender,MouseEventArgs e)
    {
        var point=e.GetPosition(Entries);
        if (e.LeftButton==MouseButtonState.Pressed && Entries.SelectedItems.Count==1 && Entries.SelectedItem is ShortcutItem item &&
            (Math.Abs(point.X-_dragStart.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(point.Y-_dragStart.Y)>SystemParameters.MinimumVerticalDragDistance))
            DragDrop.DoDragDrop(Entries,new DataObject("AiPet.ShortcutId",item.Id),DragDropEffects.Move);
    }
    private void Entries_Drop(object sender,DragEventArgs e)
    {
        if (e.Data.GetData("AiPet.ShortcutId") is not Guid source) return;
        var current=e.OriginalSource as DependencyObject;
        while (current is not null && current is not ListBoxItem) current=VisualTreeHelper.GetParent(current);
        if (current is ListBoxItem {DataContext:ShortcutItem target}) { MoveTo(source,target.Id); e.Handled=true; }
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
