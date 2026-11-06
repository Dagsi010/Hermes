using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Hermes.Core;
using Hermes.Data;

namespace Hermes.Overlay;

public partial class WorkWindow : OverlayWindow
{
    readonly ObservableCollection<AssignmentVM> _work = new();

    /// <summary>An assignment row was clicked (assignment id).</summary>
    public event Action<string>? AssignmentToggled;

    public WorkWindow()
    {
        InitializeComponent();
        Part = "work";
        WorkList.ItemsSource = _work;
        InitParts(Body, Chrome);
    }

    public void Render(SchoolData data, LocalState local, IReadOnlyList<NoticeVM> notices, DateTime now)
    {
        WorkHeading.Text = Loc.T("ov.work");
        NoticesHeading.Text = Loc.T("ov.notices");
        NoWork.Text = Loc.T("ov.noWork");

        _work.Clear();
        foreach (var a in data.Assignments.OrderBy(a => a.Due))
        {
            if (local.IsHidden(a.Id, now)) continue;
            // Handed in on Inna: nothing left to do.
            if (a.Status == "handedIn") continue;
            // Work that is long overdue is Inna's business, not the desktop's.
            if (a.Due < now.Date.AddDays(-7)) continue;
            _work.Add(new AssignmentVM(a) { IsDone = local.Done.ContainsKey(a.Id) });
        }

        NoticeList.ItemsSource = notices;
        NoticesHeading.Visibility = notices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Tick(now, local);
    }

    public void Tick(DateTime now, LocalState local)
    {
        for (var i = _work.Count - 1; i >= 0; i--)
        {
            if (local.IsHidden(_work[i].Assignment.Id, now)) _work.RemoveAt(i);
            else _work[i].Update(now);
        }
        NoWork.Visibility = _work.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetDone(string id, bool done)
    {
        var vm = _work.FirstOrDefault(w => w.Assignment.Id == id);
        if (vm != null) vm.IsDone = done;
    }

    void Assignment_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AssignmentVM vm) AssignmentToggled?.Invoke(vm.Assignment.Id);
    }

    void Notice_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NoticeVM { IsLink: true } vm) vm.Action?.Invoke();
    }
}
