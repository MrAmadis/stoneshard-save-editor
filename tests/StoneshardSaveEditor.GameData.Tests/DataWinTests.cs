using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Tests;

public class DataWinTests
{
    [Fact]
    public void Sprite_table_has_expected_counts()
    {
        Assert.Equal(17248, GameFiles.Sprites.Count);
        Assert.Equal(1768, GameFiles.Sprites.Names.Count(n => n.StartsWith("s_inv_", StringComparison.Ordinal)));
    }

    [Fact]
    public void Object_table_maps_objects_to_sprites()
    {
        var objects = GameFiles.Objects;
        Assert.Equal("s_inv_bondage", objects.SpriteOf("o_inv_bandage"));
        Assert.Equal("s_inv_blessed_water", objects.SpriteOf("o_inv_aqua_vitae"));
        Assert.Equal("s_skills_inceneration", objects.SpriteOf("o_skill_incineration_ico"));
        Assert.Equal("s_passive_ecess_heat", objects.SpriteOf("o_pass_skill_excess_heat"));
        Assert.Equal(1005, objects.Names.Count(n => n.StartsWith("o_inv_", StringComparison.Ordinal)));
        Assert.True(objects.Exists("o_inv_salve"));
        Assert.False(objects.Exists("o_inv_not_a_thing"));
        Assert.Null(objects.SpriteOf("o_inv_not_a_thing"));
    }

    [Fact]
    public void Sprite_frames_point_into_texture_pages()
    {
        var salve = GameFiles.Sprites.Get("s_inv_salve")!;
        Assert.NotNull(salve.FirstFrame);
        Assert.Equal(27, salve.FirstFrame!.BoundW);
        Assert.Equal(27, salve.FirstFrame.BoundH);
        Assert.Equal(78, salve.FirstFrame.Texture);
        Assert.Null(GameFiles.Sprites.Get("s_does_not_exist"));
    }

    [Fact]
    public void GameInstall_accepts_configured_dir() =>
        Assert.Equal(Fixtures.GameDir, GameInstall.Find(Fixtures.GameDir));

    [Fact]
    public void GameInstall_rejects_folder_without_game_files_and_falls_back_to_steam()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        // An empty folder is never accepted; on this machine Steam discovery still finds the real install.
        Assert.Equal(Fixtures.GameDir, GameInstall.Find(empty), ignoreCase: true);
    }
}
