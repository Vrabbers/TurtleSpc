
using System.Text.Json;
using TurtleSpc;

var opt = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
};
// https://github.com/SingleStepTests/ProcessorTests/tree/bb11756436da8fd16cce86aef63dc6725f48836f/spc700/v1
foreach (var file in Directory.EnumerateFiles("./tests/", "*.json"))
{
    Console.WriteLine(file);
    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
    using var outS = new FileStream(file + ".out.txt", FileMode.Create, FileAccess.Write);
    using var outW = new StreamWriter(outS);
    var tests = JsonSerializer.Deserialize<SpcTest[]>(fs, opt);
    foreach (var test in tests!)
    {
        var mem = new byte[0x1_0000];

        foreach (var item in test!.Initial.Ram)
        {
            mem[item[0]] = (byte)item[1];
        }
        var spc = new Spc
        {
            A = test.Initial.A,
            X = test.Initial.X,
            Y = test.Initial.Y,
            SP = test.Initial.SP,
            PC = test.Initial.PC,
            Status = (StatusWord)test.Initial.Psw,
            Memory = mem,
            Dsp = null
        };
        var count = 0;
        var failed = false;
        try
        {
            while (count < test.Cycles.Length)
            {
                count += spc.StepInstruction();
            }
        }
        catch (Exception e)
        {
            outW.WriteLine($"test failed on exception {e}");
            failed = true;
        }

        if (count != test.Cycles.Length)
        {
            outW.WriteLine($"count: expected {test.Cycles.Length} got {count}");
            failed = true;
        }
        if (test.Final.A != spc.A)
        {
            outW.WriteLine($"    A: expected {test.Final.A:X2} got {spc.A:X2}");
            failed = true;
        }
        if (test.Final.X != spc.X)
        {
            outW.WriteLine($"    X: expected {test.Final.X:X2} got {spc.X:X2}");
            failed = true;
        }
        if (test.Final.Y != spc.Y)
        {
            outW.WriteLine($"    Y: expected {test.Final.Y:X2} got {spc.Y:X2}");
            failed = true;
        }
        if (test.Final.SP != spc.SP)
        {
            outW.WriteLine($"   SP: expected {test.Final.SP:X2} got {spc.SP:X2}");
            failed = true;
        }
        if (test.Final.PC != spc.PC)
        {
            outW.WriteLine($"   PC: expected {test.Final.PC:X4} got {spc.PC:X4}");
            failed = true;
        }
        if (test.Final.Psw != (byte)spc.Status)
        {
            outW.WriteLine($"  PSW: expected ({(StatusWord)test.Final.Psw}) got ({(StatusWord)spc.Status})");
            failed = true;
        }
        foreach (var s in test.Final.Ram.Where(x => mem[x[0]] != x[1]))
        {
            outW.WriteLine($" {s[0]:X4}: expected {s[1]:X2} got {mem[s[0]]:X2}");
            failed = true;
        }
        if (failed)
        {
            //Debugger.Break();
            outW.WriteLine($"Test {test.Name} FAILED");
        }
    }
}

class TestSpecifications
{
    public ushort PC { get; set; }
    public byte A { get; set; }
    public byte X { get; set; }
    public byte Y { get; set; }
    public byte SP { get; set; }
    public byte Psw { get; set; }
    public required int[][] Ram { get; set; }
}
class SpcTest
{
    public required string Name { get; set; }
    public required TestSpecifications Initial { get; set; }
    public required TestSpecifications Final { get; set; }
    public required JsonElement[] Cycles { get; set; }
}