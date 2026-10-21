using System.IO;
using System.Windows;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Updater;
using IPhoneMirror.App.ViewModels;

namespace IPhoneMirror.App.Runtime.Tests;

internal static partial class Program
{
    private static int RunAudioPersistenceTests(string output)
    {
        var directory = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        var store = new UpdateSettingsStore(settingsPath);
        var app = new App { IsUiPreviewMode = true, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var originalStore = KeyboardField(app, "_settingsStore");
        var viewModels = new List<MainViewModel>();
        var phone = TrayTestDevice("audio-persistence-phone", "Audio persistence test");
        MainViewModel NewViewModel()
        {
            var vm = new MainViewModel();
            viewModels.Add(vm);
            KeyboardCall(vm, "SetSelectedDevice", phone, false);
            return vm;
        }
        try
        {
            SetKeyboardField(app, "_settingsStore", store);
            app.IsUiPreviewMode = false;
            var first = NewViewModel();
            first.PlayAudio = false;
            first.PlaybackVolume = 37;
            first.CaptureUsbAudio = false;
            var stored = new UpdateSettingsStore(settingsPath).Load();
            var preference = stored.DeviceVideoPreferences[phone.Udid];
            InteractionAssert(preference.PlayAudio == false && preference.Volume == 37 && preference.UsbVideoOnly == true,
                "Audio selection and volume must be written immediately, before shutdown or capture.");

            // Fresh store and view model: no previous runtime session is reused.
            app.RestoreUpdateSettings(stored);
            var restarted = NewViewModel();
            InteractionAssert(!restarted.PlayAudio && restarted.PlaybackVolume == 37 && !restarted.CaptureUsbAudio,
                "Restart must restore all audio controls before capture starts.");
            var state = (DeviceCaptureState)KeyboardCall(restarted, "GetOrCreateDeviceState", phone)!;
            InteractionAssert(!state.PlayAudio && state.Volume == 37 && state.UsbVideoOnly,
                "A new capture session must use the persisted audio choice.");
            var start = KeyboardCall(restarted, "CaptureSessionStartSettings", state)!;
            InteractionAssert(!(bool)KeyboardField(start, "PlayAudio") && (bool)KeyboardField(start, "UsbVideoOnly"),
                "Saved choices must reach the native capture startup settings.");
            var other = TrayTestDevice("audio-persistence-other", "Another iPhone");
            var otherState = (DeviceCaptureState)KeyboardCall(restarted, "GetOrCreateIndependentDeviceState", other)!;
            InteractionAssert(!otherState.PlayAudio && otherState.Volume == 100 && !otherState.UsbVideoOnly,
                "A different phone must keep its independent default audio settings.");
            var independent = NewViewModel();
            var independentState = (DeviceCaptureState)KeyboardCall(independent, "GetOrCreateIndependentDeviceState", phone)!;
            InteractionAssert(!independentState.PlayAudio && independentState.Volume == 37 && independentState.UsbVideoOnly,
                "Independent preview windows must restore the same device preferences.");

            restarted.PlayAudio = true;
            restarted.PlaybackVolume = 83;
            restarted.CaptureUsbAudio = true;
            app.RestoreUpdateSettings(new UpdateSettingsStore(settingsPath).Load());
            var enabled = NewViewModel();
            InteractionAssert(enabled.PlayAudio && enabled.PlaybackVolume == 83 && enabled.CaptureUsbAudio,
                "Enabling audio again must also survive restart.");
            File.WriteAllText(settingsPath, "{}");
            var fromSidecar = new UpdateSettingsStore(settingsPath).Load().DeviceVideoPreferences[phone.Udid];
            InteractionAssert(fromSidecar.PlayAudio == true && fromSidecar.Volume == 83 && fromSidecar.UsbVideoOnly == false,
                "The fork preferences must preserve audio when another build rewrites the main settings file.");

            var legacy = new DeviceCaptureState { Udid = "legacy", PlayAudio = false, Volume = 42 };
            new DeviceVideoPreferences(0, 0, 60, DecoderPreference.Auto).Apply(legacy);
            InteractionAssert(!legacy.PlayAudio && legacy.Volume == 42,
                "Older saved preferences without audio fields must preserve the existing defaults.");
            foreach (var invalid in new[] { -1d, 101, double.NaN, double.PositiveInfinity })
            {
                new DeviceVideoPreferences(0, 0, 60, DecoderPreference.Auto, Volume: invalid).Apply(legacy);
                InteractionAssert(legacy.Volume == 42, "Invalid saved volume must not reach playback.");
            }
            var blocker = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blocker, "blocked");
            var beforeFailure = app.UpdateSettings.DeviceVideoPreferences[phone.Udid];
            SetKeyboardField(app, "_settingsStore", new UpdateSettingsStore(Path.Combine(blocker, "settings.json")));
            enabled.PlayAudio = false;
            InteractionAssert(app.UpdateSettings.DeviceVideoPreferences[phone.Udid] == beforeFailure &&
                enabled.SettingsStatus == LocalizationService.Get("SetupSaveFailed"),
                "Save failure must preserve the previous stored choice and remain visible.");
            Console.WriteLine("Audio persistence: immediate disk save, restart restoration, capture startup, independent previews, device isolation, both toggle directions, fork fallback, legacy values and write failures passed.");
            return 0;
        }
        finally
        {
            app.IsUiPreviewMode = true;
            SetKeyboardField(app, "_settingsStore", originalStore);
            foreach (var vm in viewModels) WaitShortcutTask(vm.ShutdownAsync());
            app.Shutdown();
        }
    }
}
