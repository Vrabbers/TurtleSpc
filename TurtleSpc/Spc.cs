namespace TurtleSpc;

[Flags]
public enum StatusWord : byte
{
    Carry = 1 << 0,
    Zero = 1 << 1,
    Interrupt = 1 << 2,
    HalfCarry = 1 << 3,
    Break = 1 << 4,
    DirectPage = 1 << 5,
    Overflow = 1 << 6,
    Negative = 1 << 7
}

public sealed partial class Spc
{
    public required byte A { get; set; }
    public required byte X { get; set; }
    public required byte Y { get; set; }
    public required byte SP { get; set; }
    public required ushort PC { get; set; }
    public required byte[] Memory { get; init; }
    public required Dsp? Dsp { get; init; }

    public required StatusWord Status
    {
        get
        {
            var value = (StatusWord)0;
            if (_carry)
                value |= StatusWord.Carry;
            if (_zero)
                value |= StatusWord.Zero;
            if (_interrupt)
                value |= StatusWord.Interrupt;
            if (_halfCarry)
                value |= StatusWord.HalfCarry;
            if (_break)
                value |= StatusWord.Break;
            if (_directPage)
                value |= StatusWord.DirectPage;
            if (_overflow)
                value |= StatusWord.Overflow;
            if (_negative)
                value |= StatusWord.Negative;
            return value;
        }
        set
        {
            _carry = (value & StatusWord.Carry) != 0;
            _zero = (value & StatusWord.Zero) != 0;
            _interrupt = (value & StatusWord.Interrupt) != 0;
            _halfCarry = (value & StatusWord.HalfCarry) != 0;
            _break = (value & StatusWord.Break) != 0;
            _directPage = (value & StatusWord.DirectPage) != 0;
            _overflow = (value & StatusWord.Overflow) != 0;
            _negative = (value & StatusWord.Negative) != 0;
        }
    }

    private bool _carry;
    private bool _zero;
    private bool _interrupt;
    private bool _halfCarry;
    private bool _break;
    private bool _directPage;
    private bool _overflow;
    private bool _negative;

    private const int Division64kHz = 1024 / 64;
    private const int Division32kHz = 1024 / 32;
    private const int Division8kHz = 1024 / 8;

    private const int ControlAddress = 0xf1;
    private const int DspAddrAddress = 0xf2;
    private const int DspDataAddress = 0xf3;
    private const int T0TargetAddress = 0xfa;
    private const int T1TargetAddress = 0xfb;
    private const int T2TargetAddress = 0xfc;
    private const int T0OutAddress = 0xfd;
    private const int T1OutAddress = 0xfe;
    private const int T2OutAddress = 0xff;

    private ulong _cpuTicksElapsed;

    private int _timer0Counter;
    private int _timer1Counter;
    private int _timer2Counter;

    private int Control => Memory[ControlAddress];
    private int Timer0Div => Memory[T0TargetAddress];
    private int Timer1Div => Memory[T1TargetAddress];
    private int Timer2Div => Memory[T2TargetAddress];
    private ref byte Timer0Out => ref Memory[T0OutAddress];
    private ref byte Timer1Out => ref Memory[T1OutAddress];
    private ref byte Timer2Out => ref Memory[T2OutAddress];

    private int DirectPageAddress(byte offset) => (_directPage ? 0x100 : 0x000) + offset;

    private void Write(int address, byte value)
    {
        var addr = (ushort)address;
#if DEBUG
        if (Dsp is null)
            Memory[(ushort)address] = value; // Regular memory read.
#endif
        switch (address)
        {
            case DspDataAddress:
                if ((Memory[DspAddrAddress] & 0x80) == 0)
                    Dsp?.Write((byte)(Memory[0xf2] & 0x7f), value); // DSP not write protected.
                return; // DSP address write protected.
            case ControlAddress:
                var timersShouldClear = ~Control & value;
                if ((timersShouldClear & 0b001) != 0)
                {
                    _timer0Counter = 0;
                    Timer0Out = 0;
                }
                if ((timersShouldClear & 0b010) != 0)
                {
                    _timer1Counter = 0;
                    Timer1Out = 0;
                }
                if ((timersShouldClear & 0b100) != 0)
                {
                    _timer2Counter = 0;
                    Timer2Out = 0;
                }
                goto default;
            case T0TargetAddress:
                _timer0Counter = 0;
                goto default;
            case T1TargetAddress:
                _timer1Counter = 0;
                goto default;
            case T2TargetAddress:
                _timer2Counter = 0;
                goto default;
            case 0xf4:
            case 0xf5:
            case 0xf6:
            case 0xf7:
                return;
            default:
                Memory[addr] = value;
                return;
        }
    }

    private byte Read(int address)
    {
        var addr = (ushort)address;
#if DEBUG
        if (Dsp is null)
            return Memory[addr]; // Regular memory read.
#endif
        switch (address)
        {
            case DspDataAddress:
                return Dsp?.Read((byte)(Memory[DspAddrAddress] & 0x7f)) ?? 0; // DSP data read
            case T0OutAddress:
            case T1OutAddress:
            case T2OutAddress:
                var x = Memory[addr]; // Timer reads reset the timer.
                Memory[addr] = 0;
                return x;
            default:
                return Memory[addr]; // Regular memory read.
        }
    }

    public (short L, short R) OneSample()
    {
        while (true)
        {
            var cycles = StepInstruction();
            if (CheckTimers(cycles))
            {
                return Dsp?.OneSample() ?? (0, 0);
            }
        }
    }

    private const int Timer0EnableMask = 0b001;
    private const int Timer1EnableMask = 0b010;
    private const int Timer2EnableMask = 0b100;

    private bool CheckTimers(int cycles)
    {
        var oldElapsed = _cpuTicksElapsed;
        _cpuTicksElapsed += (ulong)cycles;
        if (_cpuTicksElapsed % Division64kHz >= oldElapsed % Division64kHz)
        {
            return false;
        }
        //Console.WriteLine("64kHz timer tick.");

        if ((Control & Timer2EnableMask) != 0)
        {
            _timer2Counter++;
            if (_timer2Counter == Timer2Div)
            {
                Timer2Out = (byte)((Timer2Out + 1) & 0x0f);
                _timer2Counter = 0;
            }
        }

        if (_cpuTicksElapsed % Division8kHz < oldElapsed % Division8kHz)
        {
            //Console.WriteLine("8kHz timer tick.");

            if ((Control & Timer0EnableMask) != 0)
            {
                _timer0Counter++;
                if (_timer0Counter == Timer0Div)
                {
                    Timer0Out = (byte)((Timer0Out + 1) & 0x0f);
                    _timer0Counter = 0;
                }
            }

            if ((Control & Timer1EnableMask) != 0)
            {
                _timer1Counter++;
                if (_timer1Counter == Timer1Div)
                {
                    Timer1Out = (byte)((Timer1Out + 1) & 0x0f);
                    _timer1Counter = 0;
                }
            }
        }

        return _cpuTicksElapsed % Division32kHz < oldElapsed % Division32kHz;
        // returns whether DSP should produce sample.
    }

    public static Spc FromSpcFileStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        stream.Seek(0x25, SeekOrigin.Begin);
        var pc = reader.ReadUInt16();
        var a = reader.ReadByte();
        var x = reader.ReadByte();
        var y = reader.ReadByte();
        var psw = (StatusWord)reader.ReadByte();
        var s = reader.ReadByte();
        stream.Seek(0x100, SeekOrigin.Begin);
        var ram = reader.ReadBytes(0x1_0000);
        if (ram.Length != 0x1_0000)
            throw new InvalidDataException("Invalid file");
        var dsp = new Dsp(ram);
        for (var i = 0; i < 128; i++)
        {
            dsp.Write((byte)i, reader.ReadByte());
        }
        var spc = new Spc
        {
            A = a,
            X = x,
            Y = y,
            Status = psw,
            SP = s,
            PC = pc,
            Memory = ram,
            Dsp = dsp
        };
        return spc;
    }
}