using Mech;
using PartModel;
using Xunit;

namespace Part3dAsCode.Tests;

/// The boards a model shows (PartModel.Board, exported by kicad-cli) and the
/// extra assembly views (PartModel.AssemblyView): their invariants, their
/// JSON, the placement axes and the kicad-cli command lines.
public class BoardViewTests
{
    static Model With(Board[]? boards = null, AssemblyView[]? views = null, Body[]? bodies = null) => new()
    {
        Name = "t",
        Envelope = new(100, 100, 100),
        Bodies = bodies ??
        [
            new("lid", "parts/case.py"),
            new("tray", "parts/case.py"),
            new("board-keepout", "parts/refs.py", Kind: BodyKind.Reference),
        ],
        Boards = boards ?? [],
        Views = views ?? [],
    };

    static Board Good => new("ctl", "../pcb/ctl.kicad_pcb", (150, 100), "+X", "+Y", (0, 0, 20), ["board-keepout"]);

    [Fact]
    public void AGoodBoardAndViews_areValid()
    {
        var m = With([Good],
        [
            new("open", Hide: ["lid"]),
            new("cutaway", Cut: ("lid", "+X", 0)),
            new("exploded", Move: ("lid", (0, 0, 80))),
            new("bare", Hide: ["ctl"]),
        ]);
        Assert.Empty(m.Validate());
        Assert.Contains("../pcb/ctl.kicad_pcb", m.AllInputs);
    }

    [Fact]
    public void Validate_catchesBadBoards()
    {
        var m = With(
        [
            Good with { X = "+Z", Y = "-Z" },
            Good with { Name = "lid", Pcb = "board.step", X = "up" },
            Good with { Name = "tray-parts", Replaces = ["tray", "ghost"] },
            Good,
        ]);
        var bad = string.Join("\n", m.Validate());
        Assert.Contains("board 'ctl' defined 2 times", bad);
        Assert.Contains("board 'ctl': X '+Z' and Y '-Z' are not perpendicular", bad);
        Assert.Contains("board 'lid': its mesh lid.stl clashes with body 'lid'", bad);
        Assert.Contains("board 'lid': board.step is not a .kicad_pcb", bad);
        Assert.Contains("board 'lid': X 'up' and Y '+Y' must each be one of", bad);
        Assert.Contains("board 'tray-parts': replaces 'tray', which is printed", bad);
        Assert.Contains("board 'tray-parts': replaces no body 'ghost'", bad);
    }

    [Fact]
    public void Validate_catchesBadViews()
    {
        var m = With([Good],
        [
            new("empty"),
            new("ghosts", Hide: ["nope"], Cut: ("nope", "+W", 0), Move: ("nope", (0, 0, 0))),
            new("both", Hide: ["lid"], Cut: ("lid", "-Y", 5)),
            new("shifter", Hide: ["lid"], Move: ("lid", (0, 0, 80))),
            new("bad name!", Hide: ["lid"]),
        ]);
        var bad = string.Join("\n", m.Validate());
        Assert.Contains("view 'empty': hides, cuts and moves nothing", bad);
        Assert.Contains("view 'ghosts': hides no body or board 'nope'", bad);
        Assert.Contains("view 'ghosts': cuts no body 'nope'", bad);
        Assert.Contains("view 'ghosts': keep '+W' is not one of", bad);
        Assert.Contains("view 'ghosts': moves no body 'nope'", bad);
        Assert.Contains("view 'ghosts': moves 'nope' by nothing", bad);
        Assert.Contains("view 'both': cuts 'lid', which it also hides", bad);
        Assert.Contains("view 'shifter': moves 'lid', which it also hides", bad);
        Assert.Contains("view 'bad name!': names are letters", bad);
    }

    [Fact]
    public void Validate_catchesBoardMeshNameClashes()
    {
        var m = With([Good, Good with { Name = "ctl-parts" }]);
        var bad = string.Join("\n", m.Validate());
        Assert.Contains("board 'ctl': its mesh ctl-parts.stl clashes with board 'ctl-parts'", bad);
    }

    [Fact]
    public void Validate_catchesABadColor()
    {
        var m = With(bodies: [new("lid", "parts/case.py", Color: "#12345g"), new("tray", "parts/case.py", Color: "#3a6ea5")]);
        var bad = m.Validate();
        Assert.Single(bad);
        Assert.Contains("body 'lid': color '#12345g' is not #rrggbb", bad[0]);
    }

    [Fact]
    public void Json_carriesTheBoardsAndTheCuts()
    {
        var j = With([Good], [new("cutaway", Cut: ("lid", "-Z", 12.5)), new("open", Hide: ["lid"])]).ToJson();
        var b = j["boards"]![0]!;
        Assert.Equal("ctl", (string)b["name"]!);
        Assert.Equal(150, (double)b["origin"]![0]!);
        Assert.Equal("+Y", (string)b["y"]!);
        Assert.Equal(20, (double)b["at"]![2]!);
        var cut = j["views"]![0]!["cut"]!;
        Assert.Equal("lid", (string)cut["body"]!);
        Assert.Equal("-Z", (string)cut["keep"]!);
        Assert.Equal(12.5, (double)cut["at"]!);
        Assert.Null(j["views"]![1]!["cut"]);
    }

    [Theory]
    [InlineData("+X", "+Y", 0, 0, 1)]     // flat, parts up
    [InlineData("-X", "+Z", 0, 1, 0)]     // a front panel: F face looks along +Y
    [InlineData("+X", "+Z", 0, -1, 0)]    // a rear panel: F face looks along -Y
    [InlineData("+X", "-Y", 0, 0, -1)]    // flat, upside down
    public void Normal_isXCrossY(string x, string y, int nx, int ny, int nz) =>
        Assert.Equal((nx, ny, nz), (Good with { X = x, Y = y }).Normal);

    [Fact]
    public void ExportArgs_boardAndPartsPasses()
    {
        var board = KiCad.ExportArgs("a.kicad_pcb", "b.step", "board", (150, 100.5));
        Assert.Equal(["pcb", "export", "step", "-f", "--user-origin", "150x100.5mm",
            "--no-components", "--include-silkscreen", "--include-soldermask", "-o", "b.step", "a.kicad_pcb"], board);
        var parts = KiCad.ExportArgs("a.kicad_pcb", "p.step", "parts", (0, 0));
        Assert.Contains("--no-board-body", parts);
        Assert.Contains("--subst-models", parts);
        Assert.DoesNotContain("--no-components", parts);
    }

    [Fact]
    public void Find_honoursKicadCli()
    {
        Assert.Equal("C:/tools/kicad-cli.exe", KiCad.Find("C:/tools/kicad-cli.exe"));
    }

    [Fact]
    public void Export_reportsAMissingCli()
    {
        var mech = Path.Combine(Path.GetTempPath(), "part3dascode-ki-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(mech);
        try
        {
            File.WriteAllText(Path.Combine(mech, "b.kicad_pcb"), "");
            var (bad, missing) = KiCad.Export("no-such-kicad-cli", new Layout(mech), "ctl", "b.kicad_pcb", (0, 0));
            Assert.Contains(bad, p => p.Contains("kicad-cli not found"));
            Assert.Equal(0, missing);
        }
        finally { Directory.Delete(mech, recursive: true); }
    }

    [Fact]
    public void Layout_namesTheBoardAndViewFiles()
    {
        var l = new Layout("m");
        Assert.Equal(Path.Combine("m", "build", "boards", "ctl-parts.step"), l.BoardStep("ctl", "parts"));
        Assert.Equal(l.AsmStl("ctl"), l.BoardStl("ctl", "board"));
        Assert.Equal(l.AsmStl("ctl-parts"), l.BoardStl("ctl", "parts"));
        Assert.Equal(Path.Combine("m", "build", "asm", "views", "cutaway", "lid.stl"), l.ViewStl("cutaway", "lid"));
    }
}
