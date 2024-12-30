using System.Diagnostics;

namespace TurtleSpc;

public sealed partial class Spc
{
    private byte OrOperation(byte lhs, byte rhs)
    {
        var val = (byte)(lhs | rhs);
        SetNZ(val);
        return val;
    }

    private byte AndOperation(byte lhs, byte rhs)
    {
        var val = (byte)(lhs & rhs);
        SetNZ(val);
        return val;
    }

    private byte XorOperation(byte lhs, byte rhs)
    {
        var val = (byte)(lhs ^ rhs);
        SetNZ(val);
        return val;
    }

    private void DivOperation()
    {
        var a = (uint)((Y << 8) | A);
        var b = (uint)(X << 9);
        for (var i = 0; i < 9; i++)
        {
            if ((a & 0x1_0000) != 0)
                a = ((a << 1) | 1) & 0x1ffff;
            else
                a <<= 1;

            if (a >= b)
                a ^= 1;

            if ((a & 1) != 0)
                a = (a - b) & 0x1ffff;
        }

        _halfCarry = (Y & 0x0f) >= (X & 0x0f);
        _overflow = (a & 0x100) != 0;
        A = (byte)a;
        SetNZ((byte)a);
        Y = (byte)(a >> 9);
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
        var resultBig = lhs - rhs - (_carry ? 0 : 1);
        var result = (byte)resultBig;
        SetNZ(result);
        _overflow = ((lhs & 0x80) == 0 && (rhs & 0x80) != 0 && (result & 0x80) != 0) ||
                   ((lhs & 0x80) != 0 && (rhs & 0x80) == 0 && (result & 0x80) == 0);
        _halfCarry = (((lhs) ^ (rhs)) & 0x10) ==
                    (result & 0x10); // If the 1st bit of the upper nibble of the result isnt just lhs xor rhs, then a half-carry has happened.
        _carry = !(resultBig < 0);
        return result;
    }

    private byte AdcOperation(byte lhs, byte rhs)
    {
        var resultBig = lhs + rhs + (_carry ? 1 : 0);
        var result = (byte)resultBig;
        SetNZ(result);
        _overflow = ((lhs & 0x80) == 0 && (rhs & 0x80) == 0 && (result & 0x80) != 0) ||
                   ((lhs & 0x80) != 0 && (rhs & 0x80) != 0 && (result & 0x80) == 0);
        _halfCarry = ((lhs ^ rhs) & 0x10) !=
                    (result & 0x10); // If the 1st bit of the upper nibble of the result isnt just lhs xor rhs, then a half-carry has happened.
        _carry = resultBig > 0xff;
        return result;
    }

    private byte CmpOperation(byte lhs, byte rhs)
    {
        var result = (byte)(lhs - rhs); // no carry
        SetNZ(result);
        _carry = !(result > lhs);
        return result;
    }

    private void Call(int v)
    {
        Write(0x100 + SP, (byte)(PC >> 8));
        SP--;
        Write(0x100 + SP, (byte)(PC));
        SP--;
        PC = (ushort)v;
    }

    private byte ArithmeticShiftLeft(byte value)
    {
        _carry = value >= 0x80;
        var newVal = (byte)(value << 1);
        SetNZ(newVal);
        return newVal;
    }

    private byte LogicalShiftRight(byte value)
    {
        _carry = (value & 1) == 1;
        var newVal = (byte)(value >> 1);
        SetNZ(newVal);
        return newVal;
    }

    private byte RotateLeft(byte value)
    {
        var newVal = (byte)((value << 1) | (_carry ? 1 : 0));
        SetNZ(newVal);
        _carry = value >= 0x80;
        return newVal;
    }

    private byte RotateRight(byte value)
    {
        var newVal = (byte)((value >> 1) | (_carry ? 0x80 : 0));
        SetNZ(newVal);
        _carry = (value & 1) == 1;
        return newVal;
    }

    // Sets N and Z flags appropriately, passing the value through.
    private void SetNZ(byte val)
    {
        _zero = val == 0;
        _negative = val >= 0x80;
    }

    private void SetNZ(short val)
    {
        _zero = val == 0;
        _negative = val < 0;
    }

    private int ReadImmWord()
    {
        var lower = Read(PC++);
        var upper = Read(PC++);
        return (upper << 8) | lower;
    }

    private short ReadWordNoCarry(int address)
    {
        var lower = Read(address);
        var upper = Read((address & 0xff00) | (byte)(address + 1));
        return (short)((upper << 8) | lower);
    }

    private short ReadWordCarry(int address)
    {
        var lower = Read(address);
        var upper = Read(address + 1);
        return (short)((upper << 8) | lower);
    }
    
    private void WriteWordNoCarry(int address, int word)
    {
        Write(address, (byte)word);
        Write((address & 0xff00) | (byte)(address + 1), (byte)(word >> 8));
    }
    
    
     public int StepInstruction()
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
                val = (byte)(Read(addr) - 1);
                SetNZ(val);
                Write(addr, val);
                return 4;
            case 0x9b: // DEC dp + X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                val = (byte)(Read(addr) - 1);
                SetNZ(val);
                Write(addr, val);
                return 5;
            case 0x8c: // DEC !imm16
                addr = ReadImmWord();
                val = (byte)(Read(addr) - 1);
                SetNZ(val);
                Write(addr, val);
                return 5;
            case 0x9c: // DEC A
                SetNZ(--A);
                return 2;
            case 0xab: // INC dp
                addr = DirectPageAddress(Read(PC++));
                val = (byte)(Read(addr) + 1);
                SetNZ(val);
                Write(addr, val);
                return 4;
            case 0xbb: // INC dp + X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                val = (byte)(Read(addr) + 1);
                SetNZ(val);
                Write(addr, val);
                return 5;
            case 0xac: // INC !imm16
                addr = ReadImmWord();
                val = (byte)(Read(addr) + 1);
                SetNZ(val);
                Write(addr, val);
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
                SetNZ(A);
                X = A;
                return 2;
            case 0x7d: // MOV A, X
                SetNZ(X);
                A = X;
                return 2;
            case 0x9d: // MOV X, SP
                SetNZ(SP);
                X = SP;
                return 2;
            case 0xbd: // MOV SP, X
                SP = X;
                return 2;
            case 0xdd: // MOV A, Y
                SetNZ(Y);
                A = Y;
                return 2;
            case 0xfd: // MOV Y, A
                SetNZ(A);
                Y = A;
                return 2;

            // Immediate value loads
            case 0xe8: // MOV A, #imm8
                val = Read(PC++);
                SetNZ(val);
                A = val;
                return 2;
            case 0x8d: // MOV Y
                val = Read(PC++);
                SetNZ(val);
                Y = val;
                return 2;
            case 0xcd: // MOV X
                val = Read(PC++);
                SetNZ(val);
                X = val;
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
                return 5;
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
                val = (byte)((A >> 4) | (A << 4));
                SetNZ(val);
                A = val;
                return 5;

            // Branches
            case 0x10: // BPL
                rel = (sbyte)Read(PC++);
                if (_negative)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0x30: // BMI
                rel = (sbyte)Read(PC++);
                if (!_negative)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0x50: // BVC
                rel = (sbyte)Read(PC++);
                if (_overflow)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0x70: // BVS
                rel = (sbyte)Read(PC++);
                if (!_overflow)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0x90: // BCC
                rel = (sbyte)Read(PC++);
                if (_carry)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0xb0: // BCS
                rel = (sbyte)Read(PC++);
                if (!_carry)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0xd0: // BNE
                rel = (sbyte)Read(PC++);
                if (_zero)
                    return 2;
                PC = (ushort)(PC + rel);
                return 4;
            case 0xf0: // BEQ
                rel = (sbyte)Read(PC++);
                if (!_zero)
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
                    return 4;
                PC = (ushort)(PC + rel);
                return 6;

            // Jumps
            case 0x1f: // JMP [!imm16 + X]
                addr = ReadImmWord() + X;
                PC = (ushort)ReadWordCarry(addr);
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
                _carry |= Read1Bit((ushort)ReadImmWord());
                return 5;
            case 0x2a: // OR1, C, /m.b
                _carry |= !Read1Bit((ushort)ReadImmWord());
                return 5;
            case 0x4a: // AND1 C, m.b
                _carry &= Read1Bit((ushort)ReadImmWord());
                return 4;
            case 0x6a: // AND1, C, /m.b
                _carry &= !Read1Bit((ushort)ReadImmWord());
                return 4;
            case 0x8a: // EOR1
                _carry ^= Read1Bit((ushort)ReadImmWord());
                return 5;
            case 0xaa: // MOV1 C, m.b
                _carry = Read1Bit((ushort)ReadImmWord());
                return 4;
            case 0xca: // MOV1 m.b, C
                Write1Bit((ushort)ReadImmWord(), _carry);
                return 6;
            case 0xea: // NOT1 m.b
                addr = ReadImmWord();
                Write1Bit((ushort)addr, !Read1Bit((ushort)addr));
                return 5;
            case 0x0e: // TSET1 !imm16
                addr = ReadImmWord();
                val = Read(addr);
                SetNZ((byte)(A - val));
                Write(addr, (byte)(val | A));
                return 6;
            case 0x4e: // TCLR1 !imm16
                addr = ReadImmWord();
                val = Read(addr);
                SetNZ((byte)(A - val));
                Write(addr, (byte)(val & ~A));
                return 6;

            // Returns
            case 0x6f: // RET
                SP++;
                addr = Read(0x100 + SP);
                SP++;
                addr |= Read(0x100 + SP) << 8;
                PC = (ushort)addr;
                return 5;
            case 0x7f: // RETI
                SP++;
                Status = (StatusWord)Read(0x100 + SP);
                SP++;
                addr = Read(0x100 + SP);
                SP++;
                addr |= Read(0x100 + SP) << 8;
                PC = (ushort)addr;
                return 6;

            // Word instructions
            case 0x1a: //DECW
                addr = DirectPageAddress(Read(PC++));
                valw = (short)(ReadWordNoCarry(addr) - 1);
                SetNZ(valw);
                WriteWordNoCarry(addr, valw);
                return 6;
            case 0x3a: //INCW
                addr = DirectPageAddress(Read(PC++));
                valw = (short)(ReadWordNoCarry(addr) + 1);
                SetNZ(valw);
                WriteWordNoCarry(addr, valw);
                return 6;
            case 0x5a: //CMPW YA, dp
                //TODO: how does this actually work!?
                throw new NotImplementedException();
            case 0x7a: // ADDW YA, dp
                _carry = false;
                valw = ReadWordNoCarry(DirectPageAddress(Read(PC++)));
                A = AdcOperation(A, (byte)valw);
                Y = AdcOperation(Y, (byte)(valw >> 8));
                SetNZ((short)((Y << 8) | A));
                return 5;
            case 0x9a: // SUBW YA, dp
                _carry = true;
                valw = ReadWordNoCarry(DirectPageAddress(Read(PC++)));
                A = SbcOperation(A, (byte)valw);
                Y = SbcOperation(Y, (byte)(valw >> 8));
                SetNZ((short)((Y << 8) | A));
                return 5;
            case 0xcf: // MUL YA
                valw = (short)(Y * A);
                A = (byte)valw;
                val = (byte)(valw >> 8);
                SetNZ(val);
                Y = val;
                return 9;
            case 0x9e: // DIV YA, X
                DivOperation();
                return 12;
            case 0xba: // MOVW YA, dp
                valw = ReadWordNoCarry(DirectPageAddress(Read(PC++)));
                SetNZ(valw);
                A = (byte)valw;
                Y = (byte)(valw >> 8);
                return 5;
            case 0xda: // MOVW dp, YA
                valw = (short)((Y << 8) | (A));
                WriteWordNoCarry(DirectPageAddress(Read(PC++)), valw);
                return 5;

            //PSW operations
            case 0x20: //CLRP
                _directPage = false;
                return 2;
            case 0x40: //SETP
                _directPage = true;
                return 2;
            case 0x60: //CLRC
                _carry = false;
                return 2;
            case 0x80: //SETC
                _carry = true;
                return 2;
            case 0xa0: // EI
                _interrupt = true;
                return 3;
            case 0xc0: // DI
                _interrupt = false;
                return 3;
            case 0xe0: // CLRV
                _halfCarry = false;
                _overflow = false;
                return 2;
            case 0xed: //NOTC
                _carry = !_carry;
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
                addr = ReadWordNoCarry(DirectPageAddress((byte)(Read(PC++) + X)));
                Write(addr, A);
                return 7;
            case 0xc9: // !imm16, X
                addr = ReadImmWord();
                Write(addr, X);
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
                addr = ReadWordNoCarry(DirectPageAddress(Read(PC++))) + Y;
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
                val = Read(DirectPageAddress(Read(PC++)));
                SetNZ(val);
                A = val;
                return 3;
            case 0xe5: //A, !imm16
                val = Read(ReadImmWord());
                SetNZ(val);
                A = val;
                return 4;
            case 0xe6: // A, (X)
                val = Read(DirectPageAddress(X));
                SetNZ(val);
                A = val;
                return 3;
            case 0xe7: // A, [dp+X]
                addr = ReadWordNoCarry(DirectPageAddress((byte)(Read(PC++) + X)));
                val = Read(addr);
                SetNZ(val);
                A = val;
                return 6;
            case 0xe9: // X, !imm16
                val = Read(ReadImmWord());
                SetNZ(val);
                X = val;
                return 4;
            case 0xeb: // Y, dp
                val = Read(DirectPageAddress(Read(PC++)));
                SetNZ(val);
                Y = val;
                return 3;
            case 0xec: // Y, !imm16
                val = Read(ReadImmWord());
                SetNZ(val);
                Y = val;
                return 4;
            case 0xbf: // A, (X)+
                val = Read(DirectPageAddress(X++));
                SetNZ(val);
                A = val;
                return 4;
            case 0xf4: // A, dp+X
                addr = DirectPageAddress((byte)(Read(PC++) + X));
                val = Read(addr);
                SetNZ(val);
                A = val;
                return 4;
            case 0xf5: // A, !imm16 + X
                addr = ReadImmWord() + X;
                val = Read(addr);
                SetNZ(val);
                A = val;
                return 5;
            case 0xf6: // A, !imm16 + Y
                addr = ReadImmWord() + Y;
                val = Read(addr);
                SetNZ(val);
                A = val;
                return 5;
            case 0xf7: // A, [dp] + Y
                addr = ReadWordNoCarry(DirectPageAddress(Read(PC++)));
                val = Read(addr + Y);
                SetNZ(val);
                A = val;
                return 6;
            case 0xf8: // X, dp
                val = Read(DirectPageAddress(Read(PC++)));
                SetNZ(val);
                X = val;
                return 3;
            case 0xf9: // X, dp + Y
                val = Read(DirectPageAddress((byte)(Read(PC++) + Y)));
                SetNZ(val);
                X = val;
                return 4;
            case 0xfb: // Y, dp + X
                val = Read(DirectPageAddress((byte)(Read(PC++) + X)));
                SetNZ(val);
                Y = val;
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
                var dpOffset = Read(PC++);
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
                addr = 0xffde - 2 * (instr >> 4);
                Call(ReadWordNoCarry(addr));
                return 8;
            }

            // OR, AND, EOR, CMP, ADC, SBC
            case var _ when (instr & 0x0f) >= 4 && (instr & 0x0f) <= 9 && instr <= 0xb9:
                return AluInstruction(instr);
            
            default:
                throw new NotImplementedException($"Instruction 0x{instr:X2} not supported");
        }
    }

    private int AluInstruction(byte instr)
    {
        var addr = 0;
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
                addr = ReadWordNoCarry(indirectAddressX);
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
                addr = ReadWordNoCarry(indirectAddress);
                rhs = Read(addr + Y);
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
            0 => OrOperation(lhs, rhs),
            1 => AndOperation(lhs, rhs),
            2 => XorOperation(lhs, rhs),
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
}