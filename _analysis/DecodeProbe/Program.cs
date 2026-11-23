using System.Text.RegularExpressions;

var asset = File.ReadAllText(@"D:\Game\ParkingLoop\Car Loop Parking Game\Assets\MonoBehaviour\Level_0001.asset");
var encoded = Regex.Match(asset, @"encodedLevel:\s*(\S+)").Groups[1].Value;
Console.WriteLine(encoded.Length);
Console.WriteLine(LevelDataEncoder.DecodeText(encoded));
