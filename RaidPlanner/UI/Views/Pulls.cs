namespace RaidPlanner.UI;

/// <summary>The Pulls tab (0.8.0): every kept pull. A stub until it's built.</summary>
public sealed partial class MainWindow
{
    private void DrawPulls()
    {
        Frame.BeginHeader();
        NavTabs();
        Frame.EndHeader();
        Frame.BeginBody("##pulls", null);
        Frame.EndBody();
    }
}
