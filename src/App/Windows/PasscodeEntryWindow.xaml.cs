using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App.Windows;

/// <summary>
/// Takes the iPhone passcode on the PC. The digits leave this window only as
/// a char array for the caller to type and clear; nothing is stored.
/// </summary>
public partial class PasscodeEntryWindow : IPhoneMirror.UI.Controls.RoundedWindow
{
    private readonly Action<char[]> _confirmed;

    private PasscodeEntryWindow(nint owner, string deviceName, Action<char[]> confirmed)
    {
        _confirmed = confirmed;
        InitializeComponent();
        DeviceLabel.Text = deviceName;
        // Own the preview the user is looking at, so the dialog opens on top
        // of it even when that preview window is pinned on top.
        if (owner != 0) new WindowInteropHelper(this).Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DataObject.AddPastingHandler(PasscodeBox, OnPasscodePasting);
        Loaded += (_, _) => { Activate(); PasscodeBox.Focus(); };
        Closed += (_, _) => PasscodeBox.Clear();
    }

    internal static PasscodeEntryWindow Show(nint owner, string deviceName, Action<char[]> confirmed)
    {
        var window = new PasscodeEntryWindow(owner, deviceName, confirmed);
        window.Show();
        return window;
    }

    private void OnPasscodePreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(PasscodeKeys.IsAccepted);

    private void OnPasscodePasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !text.All(PasscodeKeys.IsAccepted))
            e.CancelCommand();
    }

    private void OnPasscodeChanged(object sender, RoutedEventArgs e)
    {
        using var secure = PasscodeBox.SecurePassword;
        ConfirmButton.IsEnabled = secure.Length >= PasscodeKeys.MinimumLength;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var digits = ReadDigits();
        PasscodeBox.Clear();
        if (!PasscodeKeys.IsValid(digits))
        {
            Array.Clear(digits);
            return;
        }
        Close();
        _confirmed(digits);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    // Copy from the SecureString instead of PasswordBox.Password, which would
    // leave an immutable string with the passcode on the managed heap.
    private char[] ReadDigits()
    {
        using var secure = PasscodeBox.SecurePassword;
        var digits = new char[secure.Length];
        var buffer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try { Marshal.Copy(buffer, digits, 0, digits.Length); }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(buffer); }
        return digits;
    }
}
