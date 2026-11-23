namespace RaidPlanner.UI;

/// <summary>The first-time setup guide (0.8.0): five steps. A stub until it's built.</summary>
public sealed partial class MainWindow
{
    /// <summary>The guide's step (0–4).</summary>
    private int setupStep;

    private void DrawSetup()
    {
        Frame.BeginBody("##setup", null);
        Frame.EndBody();
    }
}
