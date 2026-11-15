using HaloDeck.Core.Discovery;
using Xunit;

namespace HaloDeck.Tests;

public sealed class VdfParserTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ho_vdf_{Guid.NewGuid():N}");

    [Fact]
    public void Parse_LibraryFoldersVdf_HandlesEscapedPathsAndNestedBlocks()
    {
        const string sample = """
        // Steam Library Folders
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
                "label"		""
            }
            "1"
            {
                "path"		"D:\\Games\\SteamLibrary"
                "apps"
                {
                    "2358720"		"123456789"
                }
            }
        }
        """;

        var root = VdfParser.Parse(sample);
        var libs = root.Get("libraryfolders");
        Assert.NotNull(libs);
        Assert.Equal(@"C:\Program Files (x86)\Steam", libs!.Get("0")!.GetString("path"));
        Assert.Equal(@"D:\Games\SteamLibrary", libs.Get("1")!.GetString("path"));
    }

    [Fact]
    public void SteamScanner_FindsGamesAcrossLibrariesAndFiltersRedistributables()
    {
        var steamRoot = Path.Combine(_tempRoot, "Steam");
        var secondLib = Path.Combine(_tempRoot, "SecondLib");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps", "common", "Steamworks Shared"));
        Directory.CreateDirectory(Path.Combine(secondLib, "steamapps", "common", "BlackMythWukong"));

        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), $$"""
        "libraryfolders"
        {
            "0" { "path" "{{steamRoot.Replace("\\", "\\\\")}}" }
            "1" { "path" "{{secondLib.Replace("\\", "\\\\")}}" }
        }
        """);

        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "appmanifest_228980.acf"), """
        "AppState"
        {
            "appid" "228980"
            "name" "Steamworks Common Redistributables"
            "installdir" "Steamworks Shared"
        }
        """);

        File.WriteAllText(Path.Combine(secondLib, "steamapps", "appmanifest_2358720.acf"), """
        "AppState"
        {
            "appid" "2358720"
            "name" "Black Myth: Wukong"
            "installdir" "BlackMythWukong"
        }
        """);

        var scanner = new SteamLibraryScanner();
        var games = scanner.Scan(steamRoot);

        Assert.Single(games);
        Assert.Equal("steam:2358720", games[0].Id);
        Assert.Equal("Black Myth: Wukong", games[0].Title);
        Assert.Equal("steam://rungameid/2358720", games[0].LaunchUri);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
    }
}
