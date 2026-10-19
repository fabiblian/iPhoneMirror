using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace IPhoneMirror.App;

/// <summary>
/// Drag handles in the gaps beside the preview change the width of the left
/// panel and the settings sidebar. The preview column takes the remainder.
/// </summary>
public partial class MainWindow
{
    private const double DefaultLeftPanelWidth = 300;
    private const double DefaultSettingsPanelWidth = 336;
    private const double MinLeftPanelWidth = 240;
    private const double MaxLeftPanelWidth = 560;
    private const double MinSettingsPanelWidth = 300;
    private const double MaxSettingsPanelWidth = 640;
    private const double MinPreviewWidthWhileResizing = 320;
    private double _leftPanelWidth = DefaultLeftPanelWidth;
    private double _settingsPanelWidth = DefaultSettingsPanelWidth;

    private static double ClampWidth(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private void InitializePanelWidths()
    {
        if (Application.Current is App app)
        {
            _leftPanelWidth = ClampWidth(app.UpdateSettings.LeftPanelWidth,
                MinLeftPanelWidth, MaxLeftPanelWidth, DefaultLeftPanelWidth);
            _settingsPanelWidth = ClampWidth(app.UpdateSettings.SettingsPanelWidth,
                MinSettingsPanelWidth, MaxSettingsPanelWidth, DefaultSettingsPanelWidth);
        }
        LeftPanelHost.ContentWidth = _leftPanelWidth;
        ControlPanel.ContentWidth = _settingsPanelWidth;
        if (LeftPanelHost.Width > 0) LeftPanelHost.Width = _leftPanelWidth;
        if (ControlPanel.Width > 0) ControlPanel.Width = _settingsPanelWidth;
    }

    // Opening and closing animate these widths; resizing waits until they settle.
    private bool CanResizeWorkspacePanels => !_workspaceSurfaceAnimationActive &&
        !_workspacePanelsStacked && _workspaceTransition?.IsRunning != true;

    private double MaxGrowth => Math.Max(0, CenterColumn.ActualWidth - MinPreviewWidthWhileResizing);

    private void OnLeftPanelResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!CanResizeWorkspacePanels || LeftPanelHost.Width <= 0) return;
        var max = Math.Min(MaxLeftPanelWidth, _leftPanelWidth + MaxGrowth);
        SetLeftPanelWidth(Math.Clamp(_leftPanelWidth + e.HorizontalChange,
            MinLeftPanelWidth, Math.Max(MinLeftPanelWidth, max)));
    }

    private void OnSettingsPanelResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!CanResizeWorkspacePanels || ControlPanel.Width <= 0) return;
        var max = Math.Min(MaxSettingsPanelWidth, _settingsPanelWidth + MaxGrowth);
        SetSettingsPanelWidth(Math.Clamp(_settingsPanelWidth - e.HorizontalChange,
            MinSettingsPanelWidth, Math.Max(MinSettingsPanelWidth, max)));
    }

    private void OnLeftPanelResizeReset(object sender, MouseButtonEventArgs e)
    {
        if (!CanResizeWorkspacePanels || LeftPanelHost.Width <= 0) return;
        SetLeftPanelWidth(DefaultLeftPanelWidth);
        SavePanelWidths();
    }

    private void OnSettingsPanelResizeReset(object sender, MouseButtonEventArgs e)
    {
        if (!CanResizeWorkspacePanels || ControlPanel.Width <= 0) return;
        SetSettingsPanelWidth(DefaultSettingsPanelWidth);
        SavePanelWidths();
    }

    private void OnPanelResizeCompleted(object sender, DragCompletedEventArgs e) => SavePanelWidths();

    private void SetLeftPanelWidth(double width)
    {
        _leftPanelWidth = width;
        LeftPanelHost.ContentWidth = width;
        LeftPanelHost.Width = width;
    }

    private void SetSettingsPanelWidth(double width)
    {
        _settingsPanelWidth = width;
        ControlPanel.ContentWidth = width;
        ControlPanel.Width = width;
    }

    private void SavePanelWidths()
    {
        if (Application.Current is not App app) return;
        if (app.UpdateSettings.LeftPanelWidth == _leftPanelWidth &&
            app.UpdateSettings.SettingsPanelWidth == _settingsPanelWidth) return;
        app.UpdateSettings.LeftPanelWidth = _leftPanelWidth;
        app.UpdateSettings.SettingsPanelWidth = _settingsPanelWidth;
        app.SaveUpdateSettings();
    }
}
