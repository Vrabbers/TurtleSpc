namespace TurtleSpc;

using System.Diagnostics;

[Flags]
internal enum StatusWord : byte
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

internal sealed class Spc
{
    public required byte A { get; set; }
    public required byte X { get; set; }
    public required byte Y { get; set; }
    public required byte SP { get; set; }
    public required StatusWord Status { get; set; }

    public required ushort PC { get; set; }

    public required byte[] Memory { get; init; }

    public required Dsp Dsp { get; init; }

    private static StatusWord SetBit(StatusWord word, StatusWord flags, bool val) => val ? word | flags : word & ~flags;

    public static Spc FromSpcFileStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        stream.Seek(0x25, SeekOrigin.Begin);
        var pc = reader.ReadUInt16();
        var a = reader.ReadByte();
        var x = reader.ReadByte();
        var y = reader.ReadByte();
        var psw = (StatusWord) reader.ReadByte();
        var s = reader.ReadByte();
        stream.Seek(0x100, SeekOrigin.Begin);
        var ram = reader.ReadBytes(0x1_0000);
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
    
    private bool Carry
    {
        get => Status.HasFlag(StatusWord.Carry);
        set => Status = SetBit(Status, StatusWord.Carry, value);
    }

    private bool Zero
    {
        get => Status.HasFlag(StatusWord.Zero);
        set => Status = SetBit(Status, StatusWord.Zero, value);
    }

    private bool Negative
    {
        get => Status.HasFlag(StatusWord.Negative);
        set => Status = SetBit(Status, StatusWord.Negative, value);
    }

    private bool Overflow
    {
        get => Status.HasFlag(StatusWord.Overflow);
        set => Status = SetBit(Status, StatusWord.Overflow, value);
    }

    private bool HalfCarry
    {
        get => Status.HasFlag(StatusWord.HalfCarry);
        set => Status = SetBit(Status, StatusWord.HalfCarry, value);
    }

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

    internal ulong _cpuTicksElapsed;

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

    private int DirectPageAddress(byte offset) => (Status.HasFlag(StatusWord.DirectPage) ? 0x100 : 0x000) + offset;

    private void Write(int address, byte value)
    {
        switch (address)
        {
            case DspDataAddress:
                if ((Memory[DspAddrAddress] & 0x80) == 0)
                    Dsp.Write((byte)(Memory[0xf2] & 0x7f), value); // DSP not write protected.
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
                Memory[(ushort)address] = value;
                return;
        }
    }

    internal byte Read(int address)
    {
        switch (address)
        {
            case DspDataAddress:
                return Dsp.Read((byte)(Memory[DspAddrAddress] & 0x7f)); // DSP data read
            case T0OutAddress:
            case T1OutAddress:
            case T2OutAddress:
                var x = Memory[(ushort)address]; // Timer reads reset the timer.
                Memory[(ushort)address] = 0;
                return x;
            default:
                return Memory[(ushort)address]; // Regular memory read.
        }
    }

    public (short L, short R) OneSample()
    {
        while (true)
        {
            var cycles = StepInstruction();
            if (CheckTimers(cycles))
            {
                return Dsp.OneSample();
            }
        }
    }

    private bool CheckTimers(int cycles)
    {
        var oldElapsed = _cpuTicksElapsed;
        _cpuTicksElapsed += (ulong)cycles;
        if (_cpuTicksElapsed % Division64kHz >= oldElapsed % Division64kHz)
        {
            return false;
        }
        //Console.WriteLine("64kHz timer tick.");

        if ((Control & 0b100) != 0)
        {
            if (_timer2Counter++ == Timer2Div)
            {
                Timer2Out = (byte)((Timer2Out + 1) & 0x0f);
                _timer2Counter = 0;
            }
        }

        if (_cpuTicksElapsed % Division8kHz < oldElapsed % Division8kHz)
        {
            //Console.WriteLine("8kHz timer tick.");

            if ((Control & 0b001) != 0)
            {
                if (_timer0Counter++ == Timer0Div)
                {
                    Timer0Out = (byte)((Timer0Out + 1) & 0x0f);
                    _timer0Counter = 0;
                }
            }

            if ((Control & 0b010) != 0)
            {
                if (_timer1Counter++ == Timer1Div)
                {
                    Timer1Out = (byte)((Timer1Out + 1) & 0x0f);
                    _timer1Counter = 0;
                }
            }
        }

        return _cpuTicksElapsed % Division32kHz <
               oldElapsed % Division32kHz; // returns whether DSP should produce sample.
    }

    private int StepInstruction()
    {
        var instr = Read(PC);
        //Console.WriteLine($"{PC:X4}  A:{A:X2} X:{X:X2} Y:{Y:X2} SP:{SP:X2} PSW:{(byte)Status:X2}");
        PC++;
        int addr;
        sbyte rel;
        byte val;
        short valw;
        switch (instr)
        {
            // Misc. instructions
            case 0x00: // NOP
                return 2;

            // Increment and decrement
            case 0x8b: // DEC dp
                addr = DirectPageAddress(Read(PC++));
                Write(addr, SetNZ((byte)(Read(addr) - 1)));
                return 4;
            case 0x9b: // DEC dp + X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                Write(addr, SetNZ((byte)(Read(addr) - 1)));
                return 5;
            case 0x8c: // DEC !imm16
                addr = ReadImmWord();
                Write(addr, SetNZ((byte)(Read(addr) - 1)));
                return 5;
            case 0x9c: // DEC A
                SetNZ(--A);
                return 2;
            case 0xab: // INC dp
                addr = DirectPageAddress(Read(PC++));
                Write(addr, SetNZ((byte)(Read(addr) + 1)));
                return 4;
            case 0xbb: // INC dp + X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                Write(addr, SetNZ((byte)(Read(addr) + 1)));
                return 5;
            case 0xac: // INC !imm16
                addr = ReadImmWord();
                Write(addr, SetNZ((byte)(Read(addr) + 1)));
                return 5;
            case 0xbc: // INC A
                SetNZ(++A);
                return 2;
            case 0xdc: // DEC Y
                SetNZ(--Y);
                return 2;
            case 0xfc: // INC Y
                SetNZ(++Y);
                return 2;
            case 0x1d: // DEC X
                SetNZ(--X);
                return 2;
            case 0x3d: // INC X
                SetNZ(++X);
                return 2;

            // Register to register MOVs
            case 0x5d: // MOV X, A
                X = SetNZ(A);
                return 2;
            case 0x7d: // MOV A, X
                A = SetNZ(X);
                return 2;
            case 0x9d: // MOV X, SP
                X = SetNZ(SP);
                return 2;
            case 0xbd: // MOV SP, X
                SP = X;
                return 2;
            case 0xdd: // MOV A, Y
                A = SetNZ(Y);
                return 2;
            case 0xfd: // MOV Y, A
                Y = SetNZ(A);
                return 2;

            // Immediate value loads
            case 0xe8: // MOV A, #imm8
                A = SetNZ(Read(PC++));
                return 2;
            case 0x8d: // MOV Y
                Y = SetNZ(Read(PC++));
                return 2;
            case 0xcd: // MOV X
                X = SetNZ(Read(PC++));
                return 2;

            // Stack instructions
            case 0x0d: // PUSH PSW
                Write(0x100 + SP, (byte)Status);
                SP--;
                return 4;
            case 0x2d: // PUSH A
                Write(0x100 + SP, A);
                SP--;
                return 4;
            case 0x4d: // PUSH X
                Write(0x100 + SP, X);
                SP--;
                return 4;
            case 0x6d: // PUSH Y
                Write(0x100 + SP, Y);
                SP--;
                return 4;
            case 0x8e: // POP PSW
                SP++;
                Status = (StatusWord)Read(0x100 + SP);
                return 4;
            case 0xae: // POP A
                SP++;
                A = Read(0x100 + SP);
                return 4;
            case 0xce: // POP X
                SP++;
                X = Read(0x100 + SP);
                return 4;
            case 0xee: // POP Y
                SP++;
                Y = Read(0x100 + SP);
                return 4;

            // Shifts and rotates
            case 0x0b: // ASL dp
                addr = DirectPageAddress(Read(PC++));
                Write(addr, ArithmeticShiftLeft(Read(addr)));
                return 4;
            case 0x1b: // ASL dp+X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                Write(addr, ArithmeticShiftLeft(Read(addr)));
                return 5;
            case 0x0c: // ASL !imm16
                addr = ReadImmWord();
                Write(addr, ArithmeticShiftLeft(Read(addr)));
                return 5;
            case 0x1c: // ASL A
                A = ArithmeticShiftLeft(A);
                return 2;
            case 0x2b: // ROL dp
                addr = DirectPageAddress(Read(PC++));
                Write(addr, RotateLeft(Read(addr)));
                return 4;
            case 0x3b: // ROL dp+X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                Write(addr, RotateLeft(Read(addr)));
                return 4;
            case 0x2c: // ROL !imm16
                addr = ReadImmWord();
                Write(addr, RotateLeft(Read(addr)));
                return 5;
            case 0x3c: // ROL A
                A = RotateLeft(A);
                return 2;
            case 0x4b: // LSR dp
                addr = DirectPageAddress(Read(PC++));
                Write(addr, LogicalShiftRight(Read(addr)));
                return 4;
            case 0x5b: // LSR dp+X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                Write(addr, LogicalShiftRight(Read(addr)));
                return 5;
            case 0x4c: // LSR !imm16
                addr = ReadImmWord();
                Write(addr, LogicalShiftRight(Read(addr)));
                return 5;
            case 0x5c: // LSR A
                A = LogicalShiftRight(A);
                return 2;
            case 0x6b: // ROR dp
                addr = DirectPageAddress(Read(PC++));
                Write(addr, RotateRight(Read(addr)));
                return 4;
            case 0x7b: // ROR dp+X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                Write(addr, RotateRight(Read(addr)));
                return 5;
            case 0x6c: // ROR !imm16
                addr = ReadImmWord();
                Write(addr, RotateRight(Read(addr)));
                return 5;
            case 0x7c: // ROR A
                A = RotateRight(A);
                return 2;
            case 0x9f: // XCN A
                A = SetNZ((byte)((A >> 4) | (A << 4)));
                return 5;

            // Branches
            case 0x10: // BPL
                rel = (sbyte)Read(PC++);
                if (Negative)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0x30: // BMI
                rel = (sbyte)Read(PC++);
                if (!Negative)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0x50: // BVC
                rel = (sbyte)Read(PC++);
                if (Overflow)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0x70: // BVS
                rel = (sbyte)Read(PC++);
                if (!Overflow)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0x90: // BCC
                rel = (sbyte)Read(PC++);
                if (Carry)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0xb0: // BCS
                rel = (sbyte)Read(PC++);
                if (!Carry)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0xd0: // BNE
                rel = (sbyte)Read(PC++);
                if (Zero)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0xf0: // BEQ
                rel = (sbyte)Read(PC++);
                if (!Zero)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0x2f: // BRA
                rel = (sbyte)Read(PC++);
                PC = (ushort)(PC + rel);
                return 4;

            // Mixed branch instructions
            case 0x2e: // CBNE dp, rel
                addr = DirectPageAddress(Read(PC++));
                rel = (sbyte)Read(PC++);
                if (A == Read(addr))
                    return 5;
                PC = (ushort)(PC + rel);
                return 7;
            case 0xde: // CBNE dp + X, rel
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                rel = (sbyte)Read(PC++);
                if (A == Read(addr))
                    return 6;
                PC = (ushort)(PC + rel);
                return 8;
            case 0x6e: // DBNZ dp, rel
                addr = DirectPageAddress(Read(PC++));
                rel = (sbyte)Read(PC++);
                val = (byte)(Read(addr) - 1);
                Write(addr, val);
                if (val == 0)
                    return 5;
                PC = (ushort)(PC + rel);
                return 7;
            case 0xfe: // DBNZ Y, rel
                rel = (sbyte)Read(PC++);
                Y--;
                if (Y == 0)
                    return 5;
                PC = (ushort)(PC + rel);
                return 7;

            // Jumps
            case 0x1f: // JMP [!imm16 + X]
                addr = ReadImmWord() + X;
                PC = (ushort)ReadWord(addr);
                return 6;
            case 0x5f: // JMP !imm16
                PC = (ushort)ReadImmWord();
                return 3;
            case 0x3f: // CALL !imm16
                Call(ReadImmWord());
                return 8;
            case 0x4f: // PCALL upper
                Call(0xff00 + Read(PC++));
                return 6;

            // Bitwise instructions
            case 0x0a: // OR1 C, m.b
                Carry |= Read1Bit((ushort)ReadImmWord());
                return 5;
            case 0x2a: // OR1, C, /m.b
                Carry |= !Read1Bit((ushort)ReadImmWord());
                return 5;
            case 0x4a: // AND1 C, m.b
                Carry &= Read1Bit((ushort)ReadImmWord());
                return 4;
            case 0x6a: // AND1, C, /m.b
                Carry &= !Read1Bit((ushort)ReadImmWord());
                return 4;
            case 0x8a: // EOR1
                Carry ^= Read1Bit((ushort)ReadImmWord());
                return 5;
            case 0xaa: // MOV1 C, m.b
                Carry = Read1Bit((ushort)ReadImmWord());
                return 4;
            case 0xca: // MOV1 m.b, C
                Write1Bit((ushort)ReadImmWord(), Carry);
                return 6;
            case 0xea: // NOT1 m.b
                addr = ReadImmWord();
                Write1Bit((ushort)addr, !Read1Bit((ushort)addr));
                return 5;
            case 0x0e: // TSET1 !imm16
                addr = ReadImmWord();
                val = Read(addr);
                SetNZ((byte)(A & val));
                Write(addr, (byte)(val | A));
                return 6;
            case 0x4e: // TCLR1 !imm16
                addr = ReadImmWord();
                val = Read(addr);
                SetNZ((byte)(A & val));
                Write(addr, (byte)(val & ~A));
                return 6;

            // Returns
            case 0x6f: // RET
                SP++;
                addr = ReadWord(0x100 + SP);
                SP++;
                PC = (ushort)addr;
                return 5;
            case 0x7f: // RETI
                Status |= StatusWord.Interrupt;
                SP++;
                addr = ReadWord(0x100 + SP);
                SP++;
                PC = (ushort)addr;
                return 5;

            // Word instructions
            case 0x1a: //DECW
                addr = DirectPageAddress(Read(PC++));
                valw = (short)(ReadWord(addr) - 1);
                WriteWord(addr, SetNZ(valw));
                return 6;
            case 0x3a: //INCW
                addr = DirectPageAddress(Read(PC++));
                valw = (short)(ReadWord(addr) + 1);
                WriteWord(addr, SetNZ(valw));
                return 6;
            case 0x5a: //CMPW YA, dp
                //TODO: how does this actually work!?
                throw new NotImplementedException();
            case 0x7a: // ADDW YA, dp
                Carry = false;
                valw = ReadWord(DirectPageAddress(Read(PC++)));
                A = AdcOperation(A, (byte)valw);
                Y = AdcOperation(Y, (byte)(valw >> 8));
                SetNZ((Y << 8 | A));
                return 5;
            case 0x9a: // SUBW YA, dp
                Carry = true;
                valw = ReadWord(DirectPageAddress(Read(PC++)));
                A = SbcOperation(A, (byte)valw);
                Y = SbcOperation(Y, (byte)(valw >> 8));
                SetNZ((Y << 8 | A));
                return 5;
            case 0xcf: // MUL YA
                valw = (short)(Y * A); //TODO: is this signed or unsigned?
                A = (byte)valw;
                Y = SetNZ((byte)(valw >> 8));
                return 9;
            case 0x9e: // DIV YA, X
                valw = (short)((Y << 8) | A);
                int nA, nY;
                if (X != 0)
                    (nA, nY) = int.DivRem(valw, X);
                else
                    (nA, nY) = (0x1ff, 0);
                A = SetNZ((byte)nA);
                Y = (byte)nY;
                Overflow = (nA & 0x100) != 0;
                // TODO: How is H set?!?
                return 12;
            case 0xba: // MOVW YA, dp
                valw = SetNZ(ReadWord(DirectPageAddress(Read(PC++))));
                A = (byte)valw;
                Y = (byte)(valw << 8);
                return 5;
            case 0xda: // MOVW dp, YA
                valw = (short)((Y << 8) | (A));
                WriteWord(DirectPageAddress(Read(PC++)), valw);
                return 4;

            //PSW operations
            case 0x20: //CLRP
                Status &= ~StatusWord.DirectPage;
                return 2;
            case 0x40: //SETP
                Status |= StatusWord.DirectPage;
                return 2;
            case 0x60: //CLRC
                Carry = false;
                return 2;
            case 0x80: //SETC
                Carry = true;
                return 2;
            case 0xa0: // EI
                Status |= StatusWord.Interrupt;
                return 2;
            case 0xc0: // DI
                Status &= ~StatusWord.Interrupt;
                return 2;
            case 0xe0: // CLRV
                Status &= ~(StatusWord.HalfCarry | StatusWord.Overflow);
                return 2;
            case 0xed: //NOTC
                Status ^= StatusWord.Carry;
                return 3;

            case 0xc8: // CMP X, #imm8
                CmpOperation(X, Read(PC++));
                return 2;
            case 0xad: // CMP Y, #imm8
                CmpOperation(Y, Read(PC++));
                return 2;
            case 0x1e: // CMP X, !imm16
                addr = ReadImmWord();
                CmpOperation(X, Read(addr));
                return 4;
            case 0x3e: // CMP X, dp
                addr = DirectPageAddress(Read(PC++));
                CmpOperation(X, Read(addr));
                return 3;
            case 0x5e: // CMP Y, !imm16
                addr = ReadImmWord();
                CmpOperation(Y, Read(addr));
                return 4;
            case 0x7e: // CMP Y, dp
                addr = DirectPageAddress(Read(PC++));
                CmpOperation(Y, Read(addr));
                return 3;

            // Register to memory MOVs
            case 0xc4: // dp, A
                addr = DirectPageAddress(Read(PC++));
                Write(addr, A);
                return 4;
            case 0xc5: // !imm16, A
                addr = ReadImmWord();
                Write(addr, A);
                return 5;
            case 0xc6: // (X), A
                Write(DirectPageAddress(X), A);
                return 4;
            case 0xc7: // [dp + X], A
                addr = ReadWord(DirectPageAddress((byte)(Read(PC++) + X)));
                Write(addr, A);
                return 7;
            case 0xc9: // !imm16, A
                addr = ReadImmWord();
                Write(addr, A);
                return 5;
            case 0xcb: // dp, Y
                Write(DirectPageAddress(Read(PC++)), Y);
                return 4;
            case 0xcc: // !imm16, Y
                Write(ReadImmWord(), Y);
                return 5;
            case 0xd4: // dp + X, A
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                Write(addr, A);
                return 5;
            case 0xd5: // !imm16 + X, A
                addr = ReadImmWord() + X;
                Write(addr, A);
                return 6;
            case 0xd6: // !imm16 + Y, A
                addr = ReadImmWord() + Y;
                Write(addr, A);
                return 6;
            case 0xd7: // [dp] + Y, A
                addr = ReadWord(DirectPageAddress(Read(PC++))) + Y;
                Write(addr, A);
                return 7;
            case 0xd8: // dp, X
                Write(DirectPageAddress(Read(PC++)), X);
                return 4;
            case 0xd9: // dp + Y, X
                addr = DirectPageAddress((byte)(Read(PC++) + Y));
                Write(addr, X);
                return 5;
            case 0xdb: // dp + X, Y
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                Write(addr, Y);
                return 5;
            case 0xaf: // (X)+, A
                Write(DirectPageAddress(X++), A);
                return 4;

            // Memory to register MOVs
            case 0xe4: //A, dp
                A = SetNZ(Read(DirectPageAddress(Read(PC++))));
                return 3;
            case 0xe5: //A, !imm16
                A = SetNZ(Read(ReadImmWord()));
                return 4;
            case 0xe6: // A, (X)
                A = SetNZ(Read(DirectPageAddress(X)));
                return 3;
            case 0xe7: // A, [dp+X]
                addr = ReadWord(DirectPageAddress((byte)(Read(PC++) + X)));
                A = SetNZ(Read(addr));
                return 6;
            case 0xe9: // X, !imm16
                X = SetNZ(Read(ReadImmWord()));
                return 4;
            case 0xeb: // Y, dp
                Y = SetNZ(Read(DirectPageAddress(Read(PC++))));
                return 3;
            case 0xec: // Y, !imm16
                Y = SetNZ(Read(ReadImmWord()));
                return 4;
            case 0xbf: // A, (X)+
                A = SetNZ(Read(DirectPageAddress(X++)));
                return 4;
            case 0xf4: // A, dp+X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                A = SetNZ(Read(addr));
                return 4;
            case 0xf5: // A, !imm16 + X
                addr = ReadImmWord() + X;
                A = SetNZ(Read(addr));
                return 5;
            case 0xf6: // A, !imm16 + Y
                addr = ReadImmWord() + Y;
                A = SetNZ(Read(addr));
                return 5;
            case 0xf7: // A, [dp] + Y
                addr = ReadWord(DirectPageAddress(Read(PC++)));
                A = SetNZ(Read(addr + Y));
                return 6;
            case 0xf8: // X, dp
                X = SetNZ(Read(DirectPageAddress(Read(PC++))));
                return 3;
            case 0xf9: // X, dp + Y
                X = SetNZ(Read(DirectPageAddress((byte)(Read(PC++) + Y))));
                return 4;
            case 0xfb: // Y, dp + X
                Y = SetNZ(Read(DirectPageAddress((byte)(Read(PC++) + X))));
                return 4;
            case 0xfa: // dp dest, dp src
                var src = DirectPageAddress(Read(PC++));
                var dest = DirectPageAddress(Read(PC++));
                Write(dest, Read(src));
                return 5;
            case 0x8f: // dp, #imm8
                val = Read(PC++);
                addr = DirectPageAddress(Read(PC++));
                Write(addr, val);
                return 5;

            // Set and clear bit
            case var _ when (instr & 0b0000_1111) == 0b0000_0010: //SET1 dp.b and CLR1 dp.b
            {
                var dpOffset = Read(PC++);
                addr = DirectPageAddress(dpOffset);
                var bit = (instr & 0b1110_0000) >> 5;
                var value = Read(addr);

                if ((instr & 0b0001_0000) == 0) //SET1
                    value |= (byte)(1 << bit);
                else //CLR1
                    value &= (byte)~(1 << bit);
                Write(addr, value);
                return 4;
            }

            //BBC and BBS
            case var _ when (instr & 0x0f) == 0x03:
            {
                var dpOffset = Read(PC++); // TODO: Check this order! and whether the correction works
                rel = (sbyte)Read(PC++);
                var bit = (instr & 0b1110_0000) >> 5;
                var mask = (byte)(1 << bit);
                addr = DirectPageAddress(dpOffset);
                var value = Read(addr);
                bool br;
                if ((instr & 0b0001_0000) == 0) //BBS
                    br = (value & mask) != 0;
                else // BBC
                    br = (value & mask) == 0;
                if (!br)
                    return 5;
                PC = (ushort)(PC + rel);
                return 7;
            }

            // TCALL x
            case var _ when (instr & 0x0f) == 0x01:
            {
                addr = 0xffc0 + 2 * (instr >> 4);
                Call(ReadWord(addr));
                return 8;
            }

            // OR, AND, EOR, CMP, ADC, SBC
            case var _ when (instr & 0x0f) >= 4 && (instr & 0x0f) <= 9 && instr <= 0xb9:
            {
                var op = instr >> 5;
                var addressingMode = instr & 0x1f;

                var lhs = A; // lots of cases use this so we set it here
                byte rhs;

                byte op1;
                byte op2 = 0;

                // Sets lhs, rhs, and reads required operands
                switch (addressingMode)
                {
                    case 0x04: // A, dp
                        op1 = Read(PC++);
                        rhs = Read(DirectPageAddress(op1));
                        break;
                    case 0x05: // A, !imm16
                        addr = ReadImmWord();
                        rhs = Read(addr);
                        break;
                    case 0x06: // A, (X)
                        rhs = Read(DirectPageAddress(X));
                        break;
                    case 0x07: // A, [dp + X]
                        op1 = Read(PC++);
                        var indirectAddressX = DirectPageAddress((byte)(op1 + X));
                        addr = ReadWord(indirectAddressX);
                        rhs = Read(addr);
                        break;
                    case 0x08: // A, #imm8
                        rhs = Read(PC++);
                        break;
                    case 0x14: // A, dp + X
                        op1 = Read(PC++);
                        addr = DirectPageAddress((byte)(op1 + X));
                        rhs = Read(addr);
                        break;
                    case 0x15: // A, !imm16 + X
                        addr = ReadImmWord() + X;
                        rhs = Read(addr);
                        break;
                    case 0x16: // A, !imm16 + Y
                        addr = ReadImmWord() + Y;
                        rhs = Read(addr);
                        break;
                    case 0x17: // A, [dp] + Y
                        op1 = Read(PC++);
                        var indirectAddress = DirectPageAddress(op1);
                        addr = ReadWord(indirectAddress);
                        rhs = Read(addr + Y); // TODO: does this addition have carry?
                        break;

                    case 0x09: // dpdest, dpsrc
                        op1 = Read(PC++);
                        op2 = Read(PC++);
                        lhs = Read(DirectPageAddress(op2));
                        rhs = Read(DirectPageAddress(op1));
                        break;

                    case 0x18: // dp, #imm8
                        op1 = Read(PC++);
                        rhs = op1;
                        op2 = Read(PC++);
                        lhs = Read(DirectPageAddress(op2));
                        break;

                    case 0x19: // (X), (Y)
                        lhs = Read(DirectPageAddress(X));
                        rhs = Read(DirectPageAddress(Y));
                        break;

                    default:
                        throw new UnreachableException();
                }

                var result = op switch
                {
                    0 => SetNZ((byte)(lhs | rhs)),
                    1 => SetNZ((byte)(lhs & rhs)),
                    2 => SetNZ((byte)(lhs ^ rhs)),
                    3 => CmpOperation(lhs, rhs),
                    4 => AdcOperation(lhs, rhs),
                    5 => SbcOperation(lhs, rhs),
                    _ => throw new UnreachableException()
                };

                if (op != 3) // CMP does not write result!
                {
                    // Write result
                    switch (addressingMode)
                    {
                        case 0x04: // A, dp
                        case 0x05: // A, !imm16
                        case 0x06: // A, (X)
                        case 0x07: // A, [dp + X]
                        case 0x08: // A, #imm8
                        case 0x14: // A, dp + X
                        case 0x15: // A, !imm16 + X
                        case 0x16: // A, !imm16 + Y
                        case 0x17: // A, [dp] + Y
                            A = result;
                            break;

                        case 0x09: // dpdest, dpsrc
                        case 0x18: // dp, #imm8
                            Write(DirectPageAddress(op2), result);
                            break;

                        case 0x19: // (X), (Y)
                            Write(DirectPageAddress(X), result);
                            break;

                        default:
                            throw new UnreachableException();
                    }
                }

                return addressingMode switch
                {
                    0x04 => 3, // A, dp
                    0x05 => 4, // A, !imm16
                    0x06 => 3, // A, (X)
                    0x07 => 6, // A, [dp + X]
                    0x08 => 2, // A, #imm8
                    0x09 => 6, // dpdest, dpsrc
                    0x14 => 4, // A, dp + X
                    0x15 => 5, // A, !imm16 + X
                    0x16 => 5, // A, !imm16 + Y
                    0x17 => 6, // A, [dp] + Y
                    0x18 => 5, // dp, #imm8
                    0x19 => 5, // (X), (Y)
                    _ => throw new UnreachableException()
                };
            }
            default:
                throw new NotImplementedException();
        }
    }

    private bool Read1Bit(ushort bitAddr)
    {
        var addr = bitAddr & 0x1fff;
        var bit = bitAddr >> 13;
        var mask = 1 << bit;
        return (Read(addr) & mask) != 0;
    }

    private void Write1Bit(ushort bitAddr, bool val)
    {
        var addr = bitAddr & 0x1fff;
        var bit = bitAddr >> 13;
        var mask = 1 << bit;
        var byteVal = Read(addr);
        if (val)
            byteVal |= (byte)mask;
        else
            byteVal &= (byte)~mask;
        Write(addr, byteVal);
    }

    private byte SbcOperation(byte lhs, byte rhs)
    {
        var result = SetNZ((byte)(lhs - rhs - (Carry ? 0 : 1)));
        Overflow = ((lhs & 0x80) == 0 && (rhs & 0x80) != 0 && (result & 0x80) != 0) ||
                   ((lhs & 0x80) != 0 && (rhs & 0x80) == 0 && (result & 0x80) == 0);
        HalfCarry = (((lhs) ^ (rhs)) & 0x10) ==
                    (result & 0x10); // If the 1st bit of the upper nibble of the result isnt just lhs xor rhs, then a half-carry has happened.
        // TODO: check if this behavior is correct for SBC! Carry flag with SBC is confusing!
        Carry = !(result > lhs);
        return result;
    }

    private byte AdcOperation(byte lhs, byte rhs)
    {
        var result = SetNZ((byte)(lhs + rhs + (Carry ? 1 : 0)));
        Overflow = ((lhs & 0x80) == 0 && (rhs & 0x80) == 0 && (result & 0x80) != 0) ||
                   ((lhs & 0x80) != 0 && (rhs & 0x80) != 0 && (result & 0x80) == 0);
        HalfCarry = ((lhs ^ (rhs)) & 0x10) !=
                    (result & 0x10); // If the 1st bit of the upper nibble of the result isnt just lhs xor rhs, then a half-carry has happened.
        Carry = result < lhs;
        return result;
    }

    private byte CmpOperation(byte lhs, byte rhs)
    {
        var result = SetNZ((byte)(lhs - rhs)); // no carry
        Carry = !(result > lhs);
        return result;
    }

    private void Call(int v)
    {
        SP--;
        WriteWord(0x100 + SP, PC);
        SP--;
        PC = (ushort)v;
    }

    private byte ArithmeticShiftLeft(byte value)
    {
        Carry = value >= 0x80;
        return SetNZ((byte)(value << 1));
    }

    private byte LogicalShiftRight(byte value)
    {
        Carry = (value & 1) == 1;
        return SetNZ((byte)(value >> 1));
    }

    private byte RotateLeft(byte value)
    {
        var newVal = SetNZ((byte)((value << 1) | (Carry ? 1 : 0)));
        Carry = value >= 0x80;
        return newVal;
    }

    private byte RotateRight(byte value)
    {
        var newVal = SetNZ((byte)((value >> 1) | (Carry ? 0x80 : 0)));
        Carry = (value & 1) == 1;
        return newVal;
    }

    // Sets N and Z flags appropriately, passing the value through.
    private byte SetNZ(byte val)
    {
        Zero = val == 0;
        Negative = val >= 0x80;
        return val;
    }

    private short SetNZ(int val)
    {
        Zero = val == 0;
        Negative = val < 0;
        return (short)val;
    }

    private int ReadImmWord()
    {
        var lower = Read(PC++);
        var upper = Read(PC++);
        return (upper << 8) | lower;
    }

    private short ReadWord(int address)
    {
        var lower = Read(address);
        var upper = Read(address + 1);
        return (short)((upper << 8) | lower);
    }

    private void WriteWord(int address, int word)
    {
        Write(address, (byte)word);
        Write(address + 1, (byte)(word >> 8));
    }
}