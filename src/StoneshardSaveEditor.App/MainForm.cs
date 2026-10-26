using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.UI.Services;

namespace StoneshardSaveEditor.App;

/// <summary>The editor window: the Blazor UI in a WebView2, plus the services it uses.</summary>
internal sealed class MainForm : Form
{
    private static readonly string DataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoneShardSaveEditor");

    public MainForm()
    {
        Text = "Stoneshard Save Editor";
        Icon = new Icon(typeof(MainForm).Assembly.GetManifestResourceStream("app.ico")!);
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(1100, 720);
        BackColor = Color.FromArgb(0x1b, 0x18, 0x15);
        StartPosition = FormStartPosition.CenterScreen;

        var cacheRoot = Path.Combine(DataRoot, "cache");
        var settingsPath = Path.Combine(DataRoot, "settings.json");
        var game = new GameDataProvider(() => Settings.Load(settingsPath).GameDir, cacheRoot);
        var options = new EditorOptions(
            SaveLocator.DefaultRoot, BackupService.DefaultRoot, cacheRoot, settingsPath, new GameProcessProbe(), PickFolder);

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton(game);
        services.AddSingleton(new EditorService(options, game));
        services.AddSingleton<IAssetUrls>(new VirtualHostAssets(game));

        var view = new BlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = Path.Combine("wwwroot", "index.html"),
            Services = services.BuildServiceProvider(),
        };
        view.RootComponents.Add<UI.App>("#app");
        view.BlazorWebViewInitializing += (_, e) => e.UserDataFolder = Path.Combine(DataRoot, "webview2");
        view.BlazorWebViewInitialized += (_, e) =>
        {
            // Icons and save screenshots are plain files; WebView2 serves them under these host names.
            Directory.CreateDirectory(cacheRoot);
            e.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostAssets.IconsHost, cacheRoot, CoreWebView2HostResourceAccessKind.Allow);
            if (Directory.Exists(SaveLocator.DefaultRoot))
                e.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHostAssets.SavesHost, SaveLocator.DefaultRoot, CoreWebView2HostResourceAccessKind.Allow);
        };
        Controls.Add(view);
    }

    private string? PickFolder() => (string?)Invoke(() =>
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the Stoneshard folder (the one with StoneShard.exe)",
            UseDescriptionForTitle = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    });
}
