using System.Text;
using Bunit;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;
using StoneshardSaveEditor.UI.Screens;

namespace StoneshardSaveEditor.Tests;

/// <summary>Failures from the final review: edits that fail half-way, unreadable saves, and disk errors.</summary>
public class FailureTests
{
    private static string SlotFile(EditorServiceTests.Harness h, string slot) =>
        Path.Combine(h.Root, "character_2", slot, "data.sav");

    private static void Rewrite(EditorServiceTests.Harness h, string slot, Action<GmObject> change)
    {
        var file = SlotFile(h, slot);
        var root = (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(file)).Json);
        change(root);
        File.WriteAllBytes(file, SaveCodec.Encode(GmJson.Write(root), Salt.For(SaveFileKind.SlotFile, "character_2", slot)));
    }

    [Fact]
    public void A_failed_edit_leaves_the_save_unchanged()
    {
        // Ring of Fire opens Pyromancy (Volume I) but is missing from this save: Unlock tree fails after the first skills.
        var h = EditorServiceTests.Start(gameDir: Fixtures.GameDir);
        Rewrite(h, "exitsave_1", root =>
        {
            var list = ((GmArray)((GmObject)root["skillsDataMap"]!)["skillsAllDataList"]!).Items;
            var i = list.FindIndex(n => n is GmString { Value: "o_skill_flame_ring_ico" });
            list.RemoveRange(i, 5);
        });
        h.Editor.Open("character_2", "exitsave_1");

        Assert.Throws<EditRejectedException>(() => h.Editor.UnlockTree("pyromancy"));

        var pyromancy = h.Editor.Current.Trees.Single(t => t.Id == "pyromancy");
        Assert.All(pyromancy.Skills, s => Assert.True(s.Locked, s.Object));
        Assert.Equal(0, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void Unreadable_saves_are_reported_as_messages()
    {
        var h = EditorServiceTests.Start();
        File.Delete(SlotFile(h, "save_1"));
        File.WriteAllBytes(SlotFile(h, "save_2"),
            SaveCodec.Encode(Encoding.UTF8.GetBytes("{ \"characterDataMap\": [1,"), Salt.For(SaveFileKind.SlotFile, "character_2", "save_2")));

        Assert.Contains("can't be opened", Assert.Throws<EditRejectedException>(() => h.Editor.Open("character_2", "save_1")).Message);
        Assert.Contains("can't be opened", Assert.Throws<EditRejectedException>(() => h.Editor.Open("character_2", "save_2")).Message);
    }

    [Fact]
    public void A_locked_save_file_is_reported_and_cleaned_up()
    {
        var h = EditorServiceTests.Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetCharacter(CharacterField.Strength, 18);
        var file = SlotFile(h, "exitsave_1");
        var before = File.ReadAllBytes(file);

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) // another program holds it open
        {
            var error = Assert.Throws<EditRejectedException>(() => h.Editor.Save());
            Assert.StartsWith("Couldn't write the save", error.Message);
        }

        Assert.False(File.Exists(file + ".tmp"));
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.Equal(1, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void A_broken_backup_is_reported()
    {
        var h = EditorServiceTests.Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetCharacter(CharacterField.Strength, 18);
        Assert.Equal(SaveResult.Saved, h.Editor.Save());
        var backup = h.Editor.Backups().Single();
        File.WriteAllText(backup.Path, "not a zip");

        Assert.StartsWith("Couldn't restore that backup", Assert.Throws<EditRejectedException>(() => h.Editor.Restore(backup.Path)).Message);
    }

    [Fact]
    public void Editor_shows_a_failed_save_as_a_message()
    {
        var h = EditorServiceTests.Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetCharacter(CharacterField.Strength, 18);
        using var ctx = ShellTests.Context(h);
        var cut = ctx.Render<Editor>();

        using (new FileStream(SlotFile(h, "exitsave_1"), FileMode.Open, FileAccess.Read, FileShare.Read))
            cut.Find("button.save").Click();

        Assert.Contains("Couldn't write the save", cut.Find(".banner-error").TextContent);
    }
}
