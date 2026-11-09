using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.IO.Compression;
using Microsoft.Web.WebView2.Wpf;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class Model3DPreviewTests
{
    [Fact]
    public void ModelFormats_RenderTriangles_AndReplaceOrRecoverTheCurrentModel()
    {
        using var temp = new TempTestDirectory();
        var files = new[]
        {
            WriteGlb(temp.GetPath("triangle.glb")),
            WriteGltfWithSiblingBuffer(temp.GetPath("sibling.gltf")),
            WriteAsciiFbx(temp.GetPath("triangle.fbx")),
            WriteObj(temp.GetPath("triangle.obj")),
            WriteAsciiStl(temp.GetPath("triangle.stl")),
            WriteAsciiPly(temp.GetPath("triangle.ply")),
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "3D", "alphabet.fbx"),
        };
        var broken = temp.GetPath("broken.glb");
        File.WriteAllText(broken, "not a glTF model");
        var appearance = new AppAppearance(new FileAppThemeSettings(temp.GetPath("appearance.json")));

        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new MainWindow(new MainWindowViewModel(), appearance: appearance) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                var host = (Grid)window.FindName("SpecializedViewport");
                Assert.True(File.Exists(files[^1]), "MIT-licensed binary FBX fixture is missing from test output.");

                foreach (var file in files)
                {
                    window.ViewModel.OpenFile(file);
                    Assert.Equal(PreviewStateKind.SpecializedLoaded, window.ViewModel.State);
                    Assert.Equal(SpecializedPreviewKind.Model3D, window.ViewModel.CurrentResult.SpecializedKind);
                    Assert.True(Complete(window.WaitForSpecializedPreviewAsync()), $"{Path.GetFileName(file)}: {Status(host)}");
                    Assert.True(window.VerifyRenderedState());

                    var renderer = Assert.Single(host.Children.Cast<FrameworkElement>().OfType<Grid>());
                    var web = Assert.Single(renderer.Children.OfType<WebView2>());
                    using var diagnostics = JsonDocument.Parse(Complete(web.ExecuteScriptAsync("window.moaview3d.diagnostics()")));
                    Assert.True(diagnostics.RootElement.GetProperty("meshCount").GetInt32() > 0, file);
                    Assert.True(diagnostics.RootElement.GetProperty("triangleCount").GetInt32() > 0, file);

                    if (file == files[0])
                    {
                        Assert.Equal(3, diagnostics.RootElement.GetProperty("camera").GetArrayLength());
                        Assert.Equal("true", Complete(web.ExecuteScriptAsync("document.getElementById('wire').click(); window.moaview3d.diagnostics().wireframe")));
                        Assert.Equal("false", Complete(web.ExecuteScriptAsync("window.moaview3d.setWireframe(false); window.moaview3d.diagnostics().wireframe")));
                        var dark = web.DefaultBackgroundColor;
                        appearance.Select(AppTheme.Light);
                        Complete(WaitForLightTheme(web));
                        Assert.NotEqual(dark, web.DefaultBackgroundColor);
                    }
                }

                window.ViewModel.OpenFile(broken);
                Assert.False(Complete(window.WaitForSpecializedPreviewAsync()));
                Assert.NotEmpty(Status(host));

                window.ViewModel.OpenFile(files[0]);
                Assert.True(Complete(window.WaitForSpecializedPreviewAsync()), Status(host));
                Assert.True(window.VerifyRenderedState());
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Gltf_RefusesRemoteBuffers_AndDoesNotReadOutsideTheModelFolder()
    {
        using var temp = new TempTestDirectory();
        using var outside = new TempTestDirectory();
        var outsideBuffer = Path.Combine(outside.DirectoryPath, "outside.bin");
        File.WriteAllBytes(outsideBuffer, TriangleBuffer());

        var traversal = Path.Combine(temp.DirectoryPath, "traversal.gltf");
        File.WriteAllText(traversal, GltfJson("../" + Path.GetFileName(outside.DirectoryPath) + "/outside.bin"));
        var remote = Path.Combine(temp.DirectoryPath, "remote.gltf");
        File.WriteAllText(remote, GltfJson("https://example.invalid/triangle.bin"));

        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new MainWindow(new MainWindowViewModel()) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                var host = (Grid)window.FindName("SpecializedViewport");
                foreach (var path in new[] { traversal, remote })
                {
                    window.ViewModel.OpenFile(path);
                    Assert.False(Complete(window.WaitForSpecializedPreviewAsync()));
                    Assert.NotEmpty(Status(host));
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Gltf_LoadsLocalTextureAndExposesAnimationPlaybackControls()
    {
        using var temp = new TempTestDirectory();
        var path = WriteAnimatedGltf(temp.GetPath("animated.gltf"));
        var appearance = new AppAppearance(new FileAppThemeSettings(temp.GetPath("appearance.json")));

        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new MainWindow(new MainWindowViewModel(), appearance: appearance) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.ViewModel.OpenFile(path);
                Assert.Equal(SpecializedPreviewKind.Model3D, window.ViewModel.CurrentResult.SpecializedKind);
                Assert.True(Complete(window.WaitForSpecializedPreviewAsync()), Status((Grid)window.FindName("SpecializedViewport")));
                var web = ModelWebView(window);

                using var diagnostics = JsonDocument.Parse(Complete(web.ExecuteScriptAsync("window.moaview3d.diagnostics()")));
                Assert.Equal(1, diagnostics.RootElement.GetProperty("animationCount").GetInt32());
                Assert.False(diagnostics.RootElement.GetProperty("animationPlaying").GetBoolean());
                Assert.Equal("1", Complete(web.ExecuteScriptAsync("document.getElementById('animation').options.length")));
                Assert.Equal("false", Complete(web.ExecuteScriptAsync("document.getElementById('play').disabled")));
                Assert.Equal("false", Complete(web.ExecuteScriptAsync("document.getElementById('stop').disabled")));

                var textureFetched = Complete(web.ExecuteScriptAsync("performance.getEntriesByType('resource').some(entry => entry.name.endsWith('/texture.png'))"));
                Assert.Equal("true", textureFetched);
                var rendererStatus = Complete(web.ExecuteScriptAsync("document.getElementById('status').textContent"));
                Assert.Contains("왼쪽 끌기", rendererStatus);
                Assert.DoesNotContain("자주색 대체 무늬", rendererStatus);

                Assert.Equal("true", Complete(web.ExecuteScriptAsync("document.getElementById('play').click(); window.moaview3d.diagnostics().animationPlaying")));
                Assert.Equal("false", Complete(web.ExecuteScriptAsync("document.getElementById('stop').click(); window.moaview3d.diagnostics().animationPlaying")));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ZipEntry_SelfContainedGlbOpensInModelViewer()
    {
        using var temp = new TempTestDirectory();
        var glbPath = WriteGlb(temp.GetPath("triangle.glb"));
        var zipPath = temp.GetPath("models.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(glbPath, "models/triangle.glb", CompressionLevel.Optimal);

        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new MainWindow(new MainWindowViewModel()) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.ViewModel.OpenFile(zipPath);
                Assert.Equal(PreviewStateKind.ArchiveLoaded, window.ViewModel.State);
                var modelEntry = Assert.Single(window.ViewModel.ArchiveEntries.Where(entry => entry.Name == "models/triangle.glb"));
                window.ViewModel.SelectedArchiveEntry = modelEntry;
                Assert.Equal(SpecializedPreviewKind.Model3D, window.ViewModel.CurrentResult.SpecializedKind);
                Assert.True(Complete(window.WaitForSpecializedPreviewAsync()), Status((Grid)window.FindName("SpecializedViewport")));
                using var diagnostics = JsonDocument.Parse(Complete(ModelWebView(window).ExecuteScriptAsync("window.moaview3d.diagnostics()")));
                Assert.Equal(1, diagnostics.RootElement.GetProperty("meshCount").GetInt32());
                Assert.Equal(1, diagnostics.RootElement.GetProperty("triangleCount").GetInt32());
            }
            finally { window.Close(); }
        });
    }

    private static string WriteGlb(string path)
    {
        var binary = TriangleBuffer();
        var json = JsonSerializer.SerializeToUtf8Bytes(ModelJson(null));
        var jsonLength = (json.Length + 3) & ~3;
        var binaryLength = (binary.Length + 3) & ~3;
        var result = new byte[12 + 8 + jsonLength + 8 + binaryLength];
        using var writer = new BinaryWriter(new MemoryStream(result));
        writer.Write(0x46546C67);
        writer.Write(2);
        writer.Write(result.Length);
        writer.Write(jsonLength);
        writer.Write(0x4E4F534A);
        writer.Write(json);
        for (var i = json.Length; i < jsonLength; i++) writer.Write((byte)0x20);
        writer.Write(binaryLength);
        writer.Write(0x004E4942);
        writer.Write(binary);
        for (var i = binary.Length; i < binaryLength; i++) writer.Write((byte)0);
        File.WriteAllBytes(path, result);
        return path;
    }

    private static string WriteGltfWithSiblingBuffer(string path)
    {
        File.WriteAllBytes(Path.ChangeExtension(path, ".bin"), TriangleBuffer());
        File.WriteAllText(path, GltfJson(Path.GetFileNameWithoutExtension(path) + ".bin"));
        return path;
    }

    private static string WriteAnimatedGltf(string path)
    {
        SampleImageFactory.WriteValidImage(Path.Combine(Path.GetDirectoryName(path)!, "texture.png"), ImageFormatKind.Png, 4, 4,
            System.Windows.Media.Color.FromRgb(245, 20, 20));

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            foreach (var value in new[] { -1f, -1f, 0f, 1f, -1f, 0f, 0f, 1f, 0f }) writer.Write(value);
            foreach (var value in new ushort[] { 0, 1, 2 }) writer.Write(value);
            writer.Write((ushort)0); // align subsequent float bufferViews to four-byte boundaries
            foreach (var value in new[] { 0f, 0f, 1f, 0f, 0.5f, 1f }) writer.Write(value);
            writer.Write(0f); writer.Write(1f);
            foreach (var value in new[] { 0f, 0f, 0f, 1f, 0f, 0f }) writer.Write(value);
        }
        File.WriteAllBytes(Path.ChangeExtension(path, ".bin"), stream.ToArray());

        var document = new
        {
            asset = new { version = "2.0", generator = "MoaView tests" },
            buffers = new[] { new { uri = Path.GetFileNameWithoutExtension(path) + ".bin", byteLength = 100 } },
            bufferViews = new object[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = 36, target = 34962 },
                new { buffer = 0, byteOffset = 36, byteLength = 6, target = 34963 },
                new { buffer = 0, byteOffset = 44, byteLength = 24, target = 34962 },
                new { buffer = 0, byteOffset = 68, byteLength = 8 },
                new { buffer = 0, byteOffset = 76, byteLength = 24 },
            },
            accessors = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3", min = new[] { -1, -1, 0 }, max = new[] { 1, 1, 0 } },
                new { bufferView = 1, componentType = 5123, count = 3, type = "SCALAR" },
                new { bufferView = 2, componentType = 5126, count = 3, type = "VEC2" },
                new { bufferView = 3, componentType = 5126, count = 2, type = "SCALAR", min = new[] { 0 }, max = new[] { 1 } },
                new { bufferView = 4, componentType = 5126, count = 2, type = "VEC3" },
            },
            images = new[] { new { uri = "texture.png" } },
            textures = new[] { new { source = 0 } },
            materials = new[] { new { pbrMetallicRoughness = new { baseColorTexture = new { index = 0 }, metallicFactor = 0, roughnessFactor = 1 }, doubleSided = true } },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, TEXCOORD_0 = 2 }, indices = 1, material = 0 } } } },
            nodes = new[] { new { mesh = 0 } },
            animations = new[]
            {
                new { name = "Move", samplers = new[] { new { input = 3, output = 4, interpolation = "LINEAR" } },
                    channels = new[] { new { sampler = 0, target = new { node = 0, path = "translation" } } } },
            },
            scenes = new[] { new { nodes = new[] { 0 } } },
            scene = 0,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document));
        return path;
    }

    private static object ModelJson(string? uri) => new
    {
        asset = new { version = "2.0", generator = "MoaView tests" },
        buffers = uri is null ? new object[] { new { byteLength = 42 } } : new object[] { new { uri, byteLength = 42 } },
        bufferViews = new[]
        {
            new { buffer = 0, byteOffset = 0, byteLength = 36, target = 34962 },
            new { buffer = 0, byteOffset = 36, byteLength = 6, target = 34963 },
        },
        accessors = new object[]
        {
            new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3", min = new[] { -1, -1, 0 }, max = new[] { 1, 1, 0 } },
            new { bufferView = 1, componentType = 5123, count = 3, type = "SCALAR" },
        },
        meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 }, indices = 1 } } } },
        nodes = new[] { new { mesh = 0 } },
        scenes = new[] { new { nodes = new[] { 0 } } },
        scene = 0,
    };

    private static string GltfJson(string buffer) => JsonSerializer.Serialize(ModelJson(buffer));

    private static byte[] TriangleBuffer()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var value in new[] { -1f, -1f, 0f, 1f, -1f, 0f, 0f, 1f, 0f }) writer.Write(value);
        foreach (var value in new ushort[] { 0, 1, 2 }) writer.Write(value);
        return stream.ToArray();
    }

    private static string WriteAsciiFbx(string path)
    {
        var ascii = """
            ; FBX 7.4.0 project file
            FBXHeaderExtension:  {
                FBXHeaderVersion: 1003
                FBXVersion: 7400
            }
            GlobalSettings:  {
                Version: 1000
                Properties70:  {
                    P: "UpAxis", "int", "Integer", "",1
                    P: "UpAxisSign", "int", "Integer", "",1
                    P: "FrontAxis", "int", "Integer", "",2
                    P: "FrontAxisSign", "int", "Integer", "",1
                    P: "CoordAxis", "int", "Integer", "",0
                    P: "CoordAxisSign", "int", "Integer", "",1
                    P: "UnitScaleFactor", "double", "Number", "",1
                }
            }
            Documents:  {
                Count: 1
                Document: 1, "Scene", "Scene" {
                    RootNode: 0
                }
            }
            References:  {
            }
            Definitions:  {
                Version: 100
                Count: 2
                ObjectType: "Model" {
                    Count: 1
                }
                ObjectType: "Geometry" {
                    Count: 1
                }
            }
            Objects:  {
                Geometry: 1001, "Geometry::Triangle", "Mesh" {
                    GeometryVersion: 124
                    Vertices: *9 {
                        a: -1,-1,0,1,-1,0,0,1,0
                    }
                    PolygonVertexIndex: *3 {
                        a: 0,1,-3
                    }
                }
                Model: 1002, "Model::Triangle", "Mesh" {
                    Version: 232
                    Properties70:  {
                        P: "Lcl Translation", "Lcl Translation", "", "A",0,0,0
                        P: "Lcl Rotation", "Lcl Rotation", "", "A",0,0,0
                        P: "Lcl Scaling", "Lcl Scaling", "", "A",1,1,1
                    }
                    Shading: T
                    Culling: "CullingOff"
                }
            }
            Connections:  {
                C: "OO",1001,1002
                C: "OO",1002,0
            }
            Takes:  {
                Current: ""
            }
            """;
        // FBX's ASCII grammar uses tabs as hierarchy delimiters; spaces are not equivalent in Three.js's loader.
        File.WriteAllText(path, ascii.Replace("    ", "\t", StringComparison.Ordinal));
        return path;
    }

    private static string WriteObj(string path)
    {
        File.WriteAllText(path, "v -1 -1 0\nv 1 -1 0\nv 0 1 0\nf 1 2 3\n");
        return path;
    }

    private static string WriteAsciiStl(string path)
    {
        File.WriteAllText(path, """
            solid triangle
              facet normal 0 0 1
                outer loop
                  vertex -1 -1 0
                  vertex 1 -1 0
                  vertex 0 1 0
                endloop
              endfacet
            endsolid triangle
            """);
        return path;
    }

    private static string WriteAsciiPly(string path)
    {
        File.WriteAllText(path, """
            ply
            format ascii 1.0
            element vertex 3
            property float x
            property float y
            property float z
            element face 1
            property list uchar int vertex_indices
            end_header
            -1 -1 0
            1 -1 0
            0 1 0
            3 0 1 2
            """);
        return path;
    }

    private static string Status(Grid host) => string.Join(" ", Descendants(host).OfType<TextBlock>().Select(text => text.Text));

    private static WebView2 ModelWebView(MainWindow window) =>
        Assert.Single(Assert.Single(((Grid)window.FindName("SpecializedViewport")).Children.Cast<Grid>()).Children.OfType<WebView2>());

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static T Complete<T>(Task<T> task)
    {
        Complete((Task)task);
        return task.GetAwaiter().GetResult();
    }

    private static void Complete(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            timeout.Tick += (_, _) => frame.Continue = false;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            timeout.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();
        }
        Assert.True(task.IsCompleted, "3D 미리보기가 제한 시간 안에 완료되지 않았습니다.");
        task.GetAwaiter().GetResult();
    }

    private static async Task WaitForLightTheme(WebView2 web)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (await web.ExecuteScriptAsync("document.documentElement.style.colorScheme") != "\"light\"")
            await Task.Delay(20, timeout.Token);
    }
}
