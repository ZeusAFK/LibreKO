using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace LibreKO.Tests;

public class ShaderFileTests
{
    private const string ProjectFile = "project.godot";
    private const string ExportPresets = "export_presets.cfg";

    private static string ClientDir([CallerFilePath] string source = "")
    {
        string root = Path.Combine(Path.GetDirectoryName(source)!, "..", "..");
        string nested = Path.Combine(root, "client");
        return File.Exists(Path.Combine(nested, ProjectFile)) ? nested : root;
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ClientDir(), relative)).Replace("\r\n", "\n");

    [Fact]
    public void TheFxShadersTellTheFxCameraByTheFxLayerBit() =>
        Assert.Contains($"const uint FX_LAYER = {FxShading.LayerBit}u;", Read("shaders/fx_common.gdshaderinc"));

    [Fact]
    public void TheFxShadersReadTheGlobalsTheClientSets()
    {
        string common = Read("shaders/fx_common.gdshaderinc");
        Assert.Contains($"global uniform sampler2D {FxShading.SceneDepthParam};", common);
        Assert.Contains($"global uniform mat4 {FxShading.SceneViewProjectionParam};", common);
        Assert.Contains($"global uniform bool {FxShading.SceneDepthLiveParam};", common);
        Assert.Contains($"global uniform float {ItemShine.ClassicParam};", Read("shaders/outfit_shine.gdshader"));
    }

    [Theory]
    [InlineData(FxShading.SceneDepthParam)]
    [InlineData(FxShading.SceneViewProjectionParam)]
    [InlineData(FxShading.SceneDepthLiveParam)]
    [InlineData(ItemShine.ClassicParam)]
    public void EveryGlobalShaderUniformIsDeclaredInTheProject(string name) =>
        Assert.Contains($"\n{name}={{\n", Read("project.godot"));

    [Fact]
    public void TheOutfitShadersHaveOneTextureForEverySlot()
    {
        string slots = Read("shaders/outfit_slots.gdshaderinc");
        for (int i = 0; i < OutfitMaterial.MaxSlots; i++)
            Assert.Contains($"uniform sampler2D slot_texture_{i} ", slots);
        Assert.DoesNotContain($"slot_texture_{OutfitMaterial.MaxSlots} ", slots);
        Assert.Contains($"uniform vec4 slot_color[{OutfitMaterial.MaxSlots}];", slots);
        Assert.Contains($"uniform vec4 slot_surface[{OutfitMaterial.MaxSlots}];", Read("shaders/outfit_body.gdshader"));
        Assert.Contains($"uniform vec4 slot_shine_a[{OutfitMaterial.MaxSlots}];", Read("shaders/outfit_shine.gdshader"));
    }

    [Fact]
    public void TheSceneDepthComputeShaderShipsWithTheGame()
    {
        Assert.True(File.Exists(Path.Combine(ClientDir(), "shaders", "fx_scene_depth.comp")));
        if (File.Exists(Path.Combine(ClientDir(), ExportPresets)))
            Assert.Contains("include_filter=\"settings.default.cfg,version.txt,shaders/*.comp\"", Read(ExportPresets));
    }
}
