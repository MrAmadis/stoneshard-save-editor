using Bunit;
using Microsoft.Extensions.DependencyInjection;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.UI;
using StoneshardSaveEditor.UI.Components;
using StoneshardSaveEditor.UI.Screens;
using StoneshardSaveEditor.UI.Services;

namespace StoneshardSaveEditor.Tests;

public sealed class FakeAssets : IAssetUrls
{
    public string Icon(string sprite) => $"icon://{sprite}";
    public string Preview(string character, string slot) => $"preview://{character}/{slot}";
}

public class ShellTests
{
    /// <summary>The save list waits for the game files, which take seconds to read the first time.</summary>
    private static readonly TimeSpan GameFilesLoad = TimeSpan.FromSeconds(60);

    public static BunitContext Context(EditorServiceTests.Harness h)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(h.Editor);
        ctx.Services.AddSingleton<IAssetUrls>(new FakeAssets());
        return ctx;
    }

    [Fact]
    public void SavePicker_shows_characters_and_slots()
    {
        var h = EditorServiceTests.Start();
        File.WriteAllText(Path.Combine(h.Root, "character_2", "save_1", "data.sav"), "junk");
        using var ctx = Context(h);

        var cut = ctx.Render<SavePicker>();
        cut.WaitForAssertion(() => Assert.Contains("Velmir", cut.Markup), GameFilesLoad);

        Assert.Contains("Revenger · Level 5", cut.Markup);
        Assert.Contains("Exit save", cut.Markup);
        Assert.Contains("Caravan Camp", cut.Markup);
        Assert.Contains("icon://s_Velmir", cut.Markup);
        Assert.Contains("preview://character_2/exitsave_1", cut.Markup);
        Assert.Contains("data.sav:", cut.Find("[data-slot='character_2/save_1'] .slot-warning").TextContent);
    }

    [Fact]
    public void SavePicker_says_it_is_reading_the_game_files_meanwhile()
    {
        // The first start reads ~280 MB of game files; the window must not look frozen (spec §9, final review).
        var root = Fixtures.CopyRealSavesToTemp();
        var work = Directory.CreateTempSubdirectory().FullName;
        using var gate = new ManualResetEventSlim();
        var options = new EditorOptions(root, Path.Combine(work, "backups"), Path.Combine(work, "cache"), Path.Combine(work, "settings.json"),
            new EditorServiceTests.FakeProbe(), null);
        var editor = new EditorService(options, new GameDataProvider(() =>
        {
            gate.Wait(TimeSpan.FromSeconds(30));
            return null;
        }, options.CacheRoot));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(editor);
        ctx.Services.AddSingleton<IAssetUrls>(new FakeAssets());

        var cut = ctx.Render<SavePicker>();
        Assert.Contains("Reading the game files", cut.Markup);

        gate.Set();
        cut.WaitForAssertion(() => Assert.Contains("Velmir", cut.Markup), GameFilesLoad);
        Assert.DoesNotContain("Reading the game files", cut.Markup);
    }

    [Fact]
    public void Picking_a_slot_opens_the_editor_and_back()
    {
        var h = EditorServiceTests.Start();
        using var ctx = Context(h);
        var cut = ctx.Render<App>();

        cut.WaitForElement("[data-slot='character_2/exitsave_1']", GameFilesLoad).Click();

        Assert.True(h.Editor.IsOpen);
        Assert.Contains("Save to game", cut.Markup);
        Assert.Contains("Caravan Camp", cut.Find(".editor-header").TextContent);

        cut.Find("button.back").Click();
        Assert.False(h.Editor.IsOpen);
        cut.WaitForAssertion(() => Assert.Contains("Exit save", cut.Markup), GameFilesLoad);
    }

    [Fact]
    public void Editor_shows_unsaved_change_count()
    {
        var h = EditorServiceTests.Start();
        h.Editor.Open("character_2", "exitsave_1");
        using var ctx = Context(h);
        var cut = ctx.Render<Editor>();
        Assert.DoesNotContain("unsaved", cut.Markup);

        h.Editor.SetCharacter(CharacterField.Strength, 18);

        cut.WaitForAssertion(() => Assert.Contains("1 unsaved change", cut.Markup));
        h.Editor.SetCharacter(CharacterField.Strength, 19);
        cut.WaitForAssertion(() => Assert.Contains("2 unsaved changes", cut.Markup));
    }

    [Fact]
    public void Editor_shows_game_running_banner_and_disables_save()
    {
        var h = EditorServiceTests.Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetCharacter(CharacterField.Strength, 18);
        using var ctx = Context(h);
        Assert.False(ctx.Render<Editor>().Find("button.save").HasAttribute("disabled"));

        h.Probe.Running = true;
        var cut = ctx.Render<Editor>();

        Assert.Contains("Stoneshard is running. Close the game before saving.", cut.Markup);
        Assert.True(cut.Find("button.save").HasAttribute("disabled"));
    }

    [Fact]
    public void Saving_reports_success()
    {
        var h = EditorServiceTests.Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetCharacter(CharacterField.Strength, 18);
        using var ctx = Context(h);
        var cut = ctx.Render<Editor>();

        cut.Find("button.save").Click();

        Assert.Contains("Saved. A backup of the previous version was kept.", cut.Markup);
        Assert.Equal(0, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void ChangedOnDisk_offers_reload()
    {
        var h = EditorServiceTests.Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetCharacter(CharacterField.Strength, 18);
        var file = Path.Combine(h.Root, "character_2", "exitsave_1", "data.sav");
        var json = SaveCodec.Decode(File.ReadAllBytes(file)).Json.Concat(" "u8.ToArray()).ToArray();
        File.WriteAllBytes(file, SaveCodec.Encode(json, Salt.For(SaveFileKind.SlotFile, "character_2", "exitsave_1")));
        using var ctx = Context(h);
        var cut = ctx.Render<Editor>();

        cut.Find("button.save").Click();
        Assert.Contains("The game changed this save after you opened it.", cut.Markup);

        cut.Find("button.reload").Click();
        Assert.Equal(0, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void NumberField_rejects_non_numeric_input()
    {
        using var ctx = new BunitContext();
        var committed = new List<double>();
        var cut = ctx.Render<NumberField>(p => p
            .Add(x => x.Value, 13)
            .Add(x => x.Whole, true)
            .Add(x => x.Min, 1)
            .Add(x => x.Max, 100)
            .Add(x => x.OnCommit, v => committed.Add(v)));

        cut.Find("input").Change("abc");
        Assert.Contains("Enter a number", cut.Find(".field-error").TextContent);
        cut.Find("input").Change("12.5");
        Assert.Contains("whole number", cut.Find(".field-error").TextContent);
        cut.Find("input").Change("101");
        Assert.Contains("between 1 and 100", cut.Find(".field-error").TextContent);
        Assert.Empty(committed);

        cut.Find("input").Change("14");
        Assert.Equal(new[] { 14.0 }, committed);
        Assert.Empty(cut.FindAll(".field-error"));
    }

    [Fact]
    public void Setup_problems_are_explained()
    {
        var missing = EditorServiceTests.Start(savesRoot: @"C:\does\not\exist");
        using (var ctx = Context(missing))
        {
            var picker = ctx.Render<SavePicker>();
            picker.WaitForAssertion(() => Assert.Contains(@"No Stoneshard saves found at C:\does\not\exist", picker.Markup));
        }

        var junk = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(junk, "data.win"), "junk");
        File.WriteAllText(Path.Combine(junk, "StoneShard.exe"), "junk");
        var noGame = EditorServiceTests.Start(gameDir: junk);
        using (var ctx = Context(noGame))
        {
            var cut = ctx.Render<SavePicker>();
            cut.WaitForAssertion(() => Assert.Contains("Game files not found", cut.Markup));
            Assert.Contains("Choose game folder…", cut.Find("button.choose-game").TextContent);
        }
    }
}
