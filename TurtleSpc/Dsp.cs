namespace TurtleSpc;

internal class Dsp
{
    public byte[] Registers { get; } = new byte[128];

    public byte Read(byte addr) { return 0; }

    public void Write(byte addr, byte val) {}
}