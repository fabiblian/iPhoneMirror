using System.Windows;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App;

// Dragging an image or a text file onto a preview puts it on the clipboard;
// with control running the clipboard sync moves it to the iPhone, ready to
// paste there.
public partial class MainWindow
{
    private void InitializePreviewDrop()
    {
        AllowDrop = true;
        PreviewDragOver += OnMainPreviewDragOver;
        PreviewDrop += OnMainPreviewDrop;
        _secondaryMirrors.FilesDropped += (udid, files) =>
            ReportPreviewDrop(Services.PreviewDrop.PutOnClipboard(files), udid);
    }

    // The native preview child does not take part in WPF hit testing, so the
    // drop is accepted wherever the pointer is over the preview's bounds.
    private bool IsOverMainPreview(DragEventArgs e)
    {
        if (!MainPreviewHost.IsVisible || MainPreviewHost.ActualWidth <= 0) return false;
        var point = e.GetPosition(MainPreviewHost);
        return point.X >= 0 && point.Y >= 0 &&
            point.X <= MainPreviewHost.ActualWidth && point.Y <= MainPreviewHost.ActualHeight;
    }

    private void OnMainPreviewDragOver(object sender, DragEventArgs e)
    {
        if (!IsOverMainPreview(e)) return;
        e.Effects = Services.PreviewDrop.CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnMainPreviewDrop(object sender, DragEventArgs e)
    {
        if (!IsOverMainPreview(e)) return;
        e.Handled = true;
        ReportPreviewDrop(Services.PreviewDrop.PutOnClipboard(e.Data), _viewModel.SelectedDevice?.Udid);
    }

    private void ReportPreviewDrop(PreviewDropResult result, string? udid)
    {
        _viewModel.AddDiagnosticLog(AppLog.Event("preview_drop", ("device", AppLog.Device(udid)),
            ("result", result)));
        var key = result switch
        {
            PreviewDropResult.Image or PreviewDropResult.Text when !_viewModel.IsDeviceControlEnabled(udid) =>
                "PreviewDropNeedsControl",
            PreviewDropResult.Image => "PreviewDropImage",
            PreviewDropResult.Text => "PreviewDropText",
            PreviewDropResult.TooLarge => "PreviewDropTooLarge",
            PreviewDropResult.Failed => "PreviewDropFailed",
            _ => "PreviewDropUnsupported",
        };
        _viewModel.AddUiLog(LocalizationService.Get(key));
    }
}
