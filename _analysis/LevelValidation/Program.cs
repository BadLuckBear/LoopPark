using System.Diagnostics;
using System.Text.Json;
using LoopPark;

if (args.Length < 1 || args.Length > 2 || !int.TryParse(args[0], out var levelNumber))
{
    Console.Error.WriteLine("usage: LevelValidation <level>");
    return 2;
}

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var jsonPath = Path.Combine(root, "Assets", "LoopPark", "Resources", "Levels", $"level_{levelNumber:D4}.json");
var json = File.ReadAllText(jsonPath);
var options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
var level = JsonSerializer.Deserialize<LevelData>(json, options);
if (level is null)
{
    Console.WriteLine($"level={levelNumber} parse=false");
    return 1;
}

level.id = levelNumber;
var mode = args.Length == 2 ? args[1] : "none";
if (mode.Contains("colrev", StringComparison.Ordinal))
    foreach (var column in level.columns) column.cars.Reverse();
if (mode.Contains("lanerev", StringComparison.Ordinal))
    foreach (var lane in level.lanes) lane.cars.Reverse();
if (mode.Contains("colsrev", StringComparison.Ordinal)) level.columns.Reverse();
var validation = level.Validate();
var beam = false;
var replay = false;
var solutionLength = 0;
var beamWidthUsed = 0;
var beamElapsedMs = 0L;
var beamError = "";

foreach (var width in new[] { 1000, 5000, 20000 })
{
    var timer = Stopwatch.StartNew();
    try
    {
        beam = PuzzleSim.BeamSolve(level, width, out var solution);
        timer.Stop();
        beamElapsedMs = timer.ElapsedMilliseconds;
        beamWidthUsed = width;
        if (beam)
        {
            solutionLength = solution?.Count ?? 0;
            replay = solution is not null && PuzzleSim.Replay(level, solution);
            break;
        }
    }
    catch (Exception ex)
    {
        timer.Stop();
        beamError = ex.GetType().Name + ":" + ex.Message.Replace('\n', ' ');
        beamElapsedMs = timer.ElapsedMilliseconds;
        beamWidthUsed = width;
        break;
    }
}

var playoutSkill1 = 0;
var playoutSkill2 = 0;
const int trials = 20;
for (var seed = 0; seed < trials; seed++)
{
    if (PuzzleSim.Playout(level, new Random(seed * 7919 + levelNumber), 1)) playoutSkill1++;
    if (PuzzleSim.Playout(level, new Random(seed * 1543 + levelNumber), 2)) playoutSkill2++;
}

Console.WriteLine(string.Join(" ", new[]
{
    $"level={levelNumber}",
    $"validate={(validation is null ? "ok" : validation)}",
    $"cars={level.TotalCars()}",
    $"beam={beam}",
    $"replay={replay}",
    $"solutionSteps={solutionLength}",
    $"beamWidth={beamWidthUsed}",
    $"beamMs={beamElapsedMs}",
    $"skill1={playoutSkill1}/{trials}",
    $"skill2={playoutSkill2}/{trials}",
    $"error={beamError}"
}));

return validation is null && beam && replay ? 0 : 1;
