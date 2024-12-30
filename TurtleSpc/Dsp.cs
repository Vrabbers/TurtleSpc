using System.Diagnostics;

namespace TurtleSpc;
public enum EnvState
{
    Release,
    Attack,
    Decay,
    Sustain,
}

public sealed class Dsp(byte[] aram)
{
    private static readonly ulong[] TimerDividers =
    [
        0, 2048, 1536, 
        1280, 1024, 768, 
        640, 512, 384, 
        320, 256, 192, 
        160, 128, 96, 
        80, 64, 48, 
        40, 32, 24, 
        20, 16, 12,
        10, 8, 6, 
        5, 4, 3, 
        2, 1
    ];

    private static readonly ulong[] TimerOffsets =
    [
         0, 0, 1040,
        536, 0, 1040,
        536, 0, 1040,
        536, 0, 1040,
        536, 0, 1040,
        536, 0, 1040,
        536, 0, 1040,
        536, 0, 1040,
        536, 0, 1040,
        536, 0, 1040,
        0, 0
    ];

    private static readonly int[] GaussianCoefficients =
    [
        0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000,
        0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000,
        0x001, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001,
        0x001, 0x001, 0x001, 0x002, 0x002, 0x002, 0x002, 0x002,
        0x002, 0x002, 0x003, 0x003, 0x003, 0x003, 0x003, 0x004,
        0x004, 0x004, 0x004, 0x004, 0x005, 0x005, 0x005, 0x005,
        0x006, 0x006, 0x006, 0x006, 0x007, 0x007, 0x007, 0x008,
        0x008, 0x008, 0x009, 0x009, 0x009, 0x00A, 0x00A, 0x00A,
        0x00B, 0x00B, 0x00B, 0x00C, 0x00C, 0x00D, 0x00D, 0x00E,
        0x00E, 0x00F, 0x00F, 0x00F, 0x010, 0x010, 0x011, 0x011,
        0x012, 0x013, 0x013, 0x014, 0x014, 0x015, 0x015, 0x016,
        0x017, 0x017, 0x018, 0x018, 0x019, 0x01A, 0x01B, 0x01B,
        0x01C, 0x01D, 0x01D, 0x01E, 0x01F, 0x020, 0x020, 0x021,
        0x022, 0x023, 0x024, 0x024, 0x025, 0x026, 0x027, 0x028,
        0x029, 0x02A, 0x02B, 0x02C, 0x02D, 0x02E, 0x02F, 0x030,
        0x031, 0x032, 0x033, 0x034, 0x035, 0x036, 0x037, 0x038,
        0x03A, 0x03B, 0x03C, 0x03D, 0x03E, 0x040, 0x041, 0x042,
        0x043, 0x045, 0x046, 0x047, 0x049, 0x04A, 0x04C, 0x04D,
        0x04E, 0x050, 0x051, 0x053, 0x054, 0x056, 0x057, 0x059,
        0x05A, 0x05C, 0x05E, 0x05F, 0x061, 0x063, 0x064, 0x066,
        0x068, 0x06A, 0x06B, 0x06D, 0x06F, 0x071, 0x073, 0x075,
        0x076, 0x078, 0x07A, 0x07C, 0x07E, 0x080, 0x082, 0x084,
        0x086, 0x089, 0x08B, 0x08D, 0x08F, 0x091, 0x093, 0x096,
        0x098, 0x09A, 0x09C, 0x09F, 0x0A1, 0x0A3, 0x0A6, 0x0A8,
        0x0AB, 0x0AD, 0x0AF, 0x0B2, 0x0B4, 0x0B7, 0x0BA, 0x0BC,
        0x0BF, 0x0C1, 0x0C4, 0x0C7, 0x0C9, 0x0CC, 0x0CF, 0x0D2,
        0x0D4, 0x0D7, 0x0DA, 0x0DD, 0x0E0, 0x0E3, 0x0E6, 0x0E9,
        0x0EC, 0x0EF, 0x0F2, 0x0F5, 0x0F8, 0x0FB, 0x0FE, 0x101,
        0x104, 0x107, 0x10B, 0x10E, 0x111, 0x114, 0x118, 0x11B,
        0x11E, 0x122, 0x125, 0x129, 0x12C, 0x130, 0x133, 0x137,
        0x13A, 0x13E, 0x141, 0x145, 0x148, 0x14C, 0x150, 0x153,
        0x157, 0x15B, 0x15F, 0x162, 0x166, 0x16A, 0x16E, 0x172,
        0x176, 0x17A, 0x17D, 0x181, 0x185, 0x189, 0x18D, 0x191,
        0x195, 0x19A, 0x19E, 0x1A2, 0x1A6, 0x1AA, 0x1AE, 0x1B2,
        0x1B7, 0x1BB, 0x1BF, 0x1C3, 0x1C8, 0x1CC, 0x1D0, 0x1D5,
        0x1D9, 0x1DD, 0x1E2, 0x1E6, 0x1EB, 0x1EF, 0x1F3, 0x1F8,
        0x1FC, 0x201, 0x205, 0x20A, 0x20F, 0x213, 0x218, 0x21C,
        0x221, 0x226, 0x22A, 0x22F, 0x233, 0x238, 0x23D, 0x241,
        0x246, 0x24B, 0x250, 0x254, 0x259, 0x25E, 0x263, 0x267,
        0x26C, 0x271, 0x276, 0x27B, 0x280, 0x284, 0x289, 0x28E,
        0x293, 0x298, 0x29D, 0x2A2, 0x2A6, 0x2AB, 0x2B0, 0x2B5,
        0x2BA, 0x2BF, 0x2C4, 0x2C9, 0x2CE, 0x2D3, 0x2D8, 0x2DC,
        0x2E1, 0x2E6, 0x2EB, 0x2F0, 0x2F5, 0x2FA, 0x2FF, 0x304,
        0x309, 0x30E, 0x313, 0x318, 0x31D, 0x322, 0x326, 0x32B,
        0x330, 0x335, 0x33A, 0x33F, 0x344, 0x349, 0x34E, 0x353,
        0x357, 0x35C, 0x361, 0x366, 0x36B, 0x370, 0x374, 0x379,
        0x37E, 0x383, 0x388, 0x38C, 0x391, 0x396, 0x39B, 0x39F,
        0x3A4, 0x3A9, 0x3AD, 0x3B2, 0x3B7, 0x3BB, 0x3C0, 0x3C5,
        0x3C9, 0x3CE, 0x3D2, 0x3D7, 0x3DC, 0x3E0, 0x3E5, 0x3E9,
        0x3ED, 0x3F2, 0x3F6, 0x3FB, 0x3FF, 0x403, 0x408, 0x40C,
        0x410, 0x415, 0x419, 0x41D, 0x421, 0x425, 0x42A, 0x42E,
        0x432, 0x436, 0x43A, 0x43E, 0x442, 0x446, 0x44A, 0x44E,
        0x452, 0x455, 0x459, 0x45D, 0x461, 0x465, 0x468, 0x46C,
        0x470, 0x473, 0x477, 0x47A, 0x47E, 0x481, 0x485, 0x488,
        0x48C, 0x48F, 0x492, 0x496, 0x499, 0x49C, 0x49F, 0x4A2,
        0x4A6, 0x4A9, 0x4AC, 0x4AF, 0x4B2, 0x4B5, 0x4B7, 0x4BA,
        0x4BD, 0x4C0, 0x4C3, 0x4C5, 0x4C8, 0x4CB, 0x4CD, 0x4D0,
        0x4D2, 0x4D5, 0x4D7, 0x4D9, 0x4DC, 0x4DE, 0x4E0, 0x4E3,
        0x4E5, 0x4E7, 0x4E9, 0x4EB, 0x4ED, 0x4EF, 0x4F1, 0x4F3,
        0x4F5, 0x4F6, 0x4F8, 0x4FA, 0x4FB, 0x4FD, 0x4FF, 0x500,
        0x502, 0x503, 0x504, 0x506, 0x507, 0x508, 0x50A, 0x50B,
        0x50C, 0x50D, 0x50E, 0x50F, 0x510, 0x511, 0x511, 0x512,
        0x513, 0x514, 0x514, 0x515, 0x516, 0x516, 0x517, 0x517,
        0x517, 0x518, 0x518, 0x518, 0x518, 0x518, 0x519, 0x519
    ];
    
    private const int VoiceCount = 8;
    private const int VoiceEnvXAddress = 0x08;
    private const int VoiceEndXAddress = 0x7c;
    private const int VoiceOutXAddress = 0x09;
    private const int EndXAddress = 0x7c;

    private readonly byte[] _regs = new byte[128];
    private byte[] _aram = aram;
    private readonly short[] _envVolumes = new short[VoiceCount];
    private readonly EnvState[] _envStates = new EnvState[VoiceCount];
    private byte _lastKeyOn;

    private ushort _noiseSample = 1;

    private readonly short[,] _brrDecodeBuffers = new short[VoiceCount, 12];
    private readonly ushort[] _brrBlockAddress = new ushort[VoiceCount];
    private readonly sbyte[] _brrBlockIndex = new sbyte[VoiceCount];
    private readonly int[] _brrInterpolatePosition = new int[VoiceCount];
    private readonly byte[] _brrBufferIndex = new byte[VoiceCount];

    private ulong _counter;

    private int _echoIndex;
    private int _echoIndexMax;

    private static bool TestBitFlag(byte value, int voice) => ((1 << voice) & value) != 0;

    private static int BytePropertyAddress(int index, int voice) => (voice << 4) | index;

    private sbyte VoiceVolLeft(int voice) => (sbyte)_regs[BytePropertyAddress(0x0, voice)];
    private sbyte VoiceVolRight(int voice) => (sbyte)_regs[BytePropertyAddress(0x1, voice)];

    private int VoicePitch(int voice) => (_regs[BytePropertyAddress(0x2, voice)] | (_regs[BytePropertyAddress(0x3, voice)] << 8)) & 0x3fff;

    private byte VoiceSourceNumber(int voice) => _regs[BytePropertyAddress(0x4, voice)];

    private bool VoiceAdsrEnable(int voice) => TestBitFlag(_regs[BytePropertyAddress(0x5, voice)], 7);
    private int VoiceAdsrDecayRate(int voice) => (_regs[BytePropertyAddress(0x5, voice)] >> 4) & 0x07;
    private int VoiceAdsrAttackRate(int voice) => _regs[BytePropertyAddress(0x5, voice)] & 0x0f;
    private int VoiceAdsrSustainLevel(int voice) => (_regs[BytePropertyAddress(0x6, voice)] >> 5) & 0x07;
    private int VoiceAdsrSustainRate(int voice) => _regs[BytePropertyAddress(0x6, voice)] & 0x1f;

    private byte VoiceGain(int voice) => _regs[BytePropertyAddress(0x7, voice)];

    private void WriteVoiceEnvX(byte value, int voice)
    {
        _regs[BytePropertyAddress(VoiceEnvXAddress, voice)] = value;
    }

    private void WriteVoiceOutX(sbyte value, int voice)
    {
        _regs[BytePropertyAddress(VoiceOutXAddress, voice)] = (byte)value;
    }

    private sbyte MainVolLeft => (sbyte)_regs[0x0c];
    private sbyte MainVolRight => (sbyte)_regs[0x1c];
    private sbyte EchoVolLeft => (sbyte)_regs[0x2c];
    private sbyte EchoVolRight => (sbyte)_regs[0x3c];
    private byte KeyOn => _regs[0x4c];

    private byte KeyOff => _regs[0x5c];
    private bool Reset => TestBitFlag(_regs[0x6c], 7);
    private bool Mute => TestBitFlag(_regs[0x6c], 6);
    private bool EchoDisable => TestBitFlag(_regs[0x6c], 5);
    private int NoiseFrequency => _regs[0x6c] & 0x1f;
    
    private void WriteVoiceEndX(bool end, int voice)
    {
        var mask = 1 << voice;
        _regs[VoiceEndXAddress] = (byte)(end ? (_regs[EndXAddress] | mask) : (_regs[EndXAddress] & ~mask ));
    }

    private sbyte EchoFeedback => (sbyte)_regs[0x0d];

    private bool VoicePitchModulationOn(int voice) => TestBitFlag(_regs[0x2d], voice);
    private bool VoiceNoiseOn(int voice) => TestBitFlag(_regs[0x3d], voice);

    private bool VoiceEchoOn(int voice) => TestBitFlag(_regs[0x4d], voice);

    private int DirectoryAddress => _regs[0x5d] << 8;

    private int EchoStartAddress => _regs[0x6d] << 8;

    private int EchoDelay => _regs[0x7d] & 0xf;

    private sbyte EchoFilterCoefficients(int index) => (sbyte) _regs[BytePropertyAddress(0x0f, index)];

    private ushort SampleStartAddress(int voice)
    {
        var basePtr = DirectoryAddress + VoiceSourceNumber(voice) * 4;
        return (ushort)(_aram[basePtr] | (_aram[basePtr + 1] << 8));
    }
    
    private ushort SampleLoopAddress(int voice)
    {
        var basePtr = DirectoryAddress + VoiceSourceNumber(voice) * 4 + 2;
        return (ushort)(_aram[basePtr] | (_aram[basePtr + 1] << 8));
    }

    private bool ShouldDoAtRate(int rate)
    {
        if (rate == 0)
            return false;
        return (_counter + TimerOffsets[rate]) % TimerDividers[rate] == 0;
    }
    
    private static (int Shift, int Filter, bool Loop, bool End) BrrHeaderDecode(byte header) =>
    (
        header >> 4,
        (header >> 2) & 0x03,
        (header & 0x02) != 0,
        (header & 0x01) != 0
    );
    private static int SignExtend15Bit(int val) => (((short)(val << 1)) >> 1);

    private void DecodeBrrBlock(int voice)
    {
        static short SignExtendLeast4Bits(byte v) => (short)((sbyte)(v << 4) >> 4);
        
        var baseBlockAddress = _brrBlockAddress[voice];
        var (shift, filter, loop, end) = BrrHeaderDecode(_aram[baseBlockAddress]);
        if (end)
        {
            WriteVoiceEndX(true, voice);
            if (!loop)
            {
                _envStates[voice] = EnvState.Release;
                _envVolumes[voice] = 0;
            }
        }
        var blockIndex = _brrBlockIndex[voice];

        var baseDataAddress = (ushort)(baseBlockAddress + 1 + blockIndex / 2);

        Span<short> vals = stackalloc short[4];
        vals[3] = SignExtendLeast4Bits(_aram[(ushort)(baseDataAddress + 1)]);
        vals[2] = (short)(((sbyte)_aram[(ushort)(baseDataAddress + 1)]) >> 4);
        vals[1] = SignExtendLeast4Bits(_aram[baseDataAddress]);
        vals[0] = (short)(((sbyte)_aram[baseDataAddress]) >> 4);

        var bufferIndex = _brrBufferIndex[voice];
        for (var i = 0; i < 4; i++)
        {
            var sample = (vals[i] << shift) >> 1;

            var prev1 = bufferIndex - 1;
            if (prev1 < 0)
                prev1 += 12;
            var prev2 = bufferIndex - 2;
            if (prev2 < 0)
                prev2 += 12;

            sample = filter switch
            {
                0 => sample,
                1 => sample + 15 * _brrDecodeBuffers[voice, prev1] / 16,
                2 => sample + 61 * _brrDecodeBuffers[voice, prev1] / 32 - 15 * _brrDecodeBuffers[voice, prev2] / 16,
                3 => sample + 115 * _brrDecodeBuffers[voice, prev1] / 64 - 13 * _brrDecodeBuffers[voice, prev2] / 16,
                _ => throw new UnreachableException()
            };
            _brrDecodeBuffers[voice, bufferIndex] = (short)SignExtend15Bit(short.CreateSaturating(sample));
            bufferIndex++;
        }

        _brrBufferIndex[voice] = (byte)(bufferIndex == 12 ? 0 : bufferIndex);
        blockIndex += 4;
        if (blockIndex == 16)
        {
            _brrBlockIndex[voice] = 0;
            if (end && loop)
                _brrBlockAddress[voice] = SampleLoopAddress(voice);
            else
                _brrBlockAddress[voice] += 9;
        }
        else
        {
            _brrBlockIndex[voice] = blockIndex;
        }
    }
    
    public (short L, short R) OneSample()
    {
        Span<short> samplesL = stackalloc short[VoiceCount];
        Span<short> samplesR = stackalloc short[VoiceCount];

        if (ShouldDoAtRate(NoiseFrequency))
            _noiseSample = (ushort)(((_noiseSample << 1) | ((_noiseSample >> 13) ^ (_noiseSample >> 14)) & 1) & 0x7fff);
        
        if (_counter % 2 == 0)
        {
            var toggleKeyOn = (byte)(~_lastKeyOn & KeyOn);
            _lastKeyOn = KeyOn;
            
            for (var voice = 0; voice < VoiceCount; voice++) 
                PollKeyOnAndKeyOff(toggleKeyOn, voice);
        }
        
        for (var voice = 0; voice < VoiceCount; voice++)
        {
            if (_brrInterpolatePosition[voice] < 0) // "preparing" sample
            {
                _brrInterpolatePosition[voice]++;
                samplesL[voice] = 0;
                samplesR[voice] = 0;
                WriteVoiceOutX(0, voice);
            }
            else 
            {
                UpdateEnvelope(voice);
                
                var sample = GenerateSample(voice);
                
                WriteVoiceOutX((sbyte)(sample >> 8), voice);
                
                samplesL[voice] = (short)(((sample * VoiceVolLeft(voice)) >> 7) << 1);
                samplesR[voice] = (short)(((sample * VoiceVolRight(voice)) >> 7) << 1);
                
                AdvanceVoicePitch(voice);
            }
            WriteVoiceEnvX((byte)(_envVolumes[voice] >> 4), voice);
        }
        
        short dacL = 0;
        short dacR = 0;

        for (var i = 0; i < VoiceCount; i++)
        {
            dacL = short.CreateSaturating(dacL + samplesL[i]);
            dacR = short.CreateSaturating(dacR + samplesR[i]);
        }
        
        dacL = (short)((dacL * MainVolLeft) >> 7);
        dacR = (short)((dacR * MainVolRight) >> 7);

        if (!EchoDisable)
        {
            var (enterFirL, enterFirR) = ReadEchoBuffer();
            EnterFir((short)(enterFirL >> 1), (short)(enterFirR >> 1));
            var (firL, firR) = CalculateFirSample();
            
            dacL = short.CreateSaturating(dacL + ((firL * EchoVolLeft) >> 7));
            dacR = short.CreateSaturating(dacR + ((firR * EchoVolRight) >> 7));
            
            short echoL = 0;
            short echoR = 0;
            
            for (var i = 0; i < VoiceCount; i++)
            {
                if (!VoiceEchoOn(i)) 
                    continue;
                echoL = short.CreateSaturating(echoL + samplesL[i]);
                echoR = short.CreateSaturating(echoR + samplesR[i]);
            }
            
            echoL = short.CreateSaturating(echoL + ((firL * EchoFeedback) >> 7));
            echoR = short.CreateSaturating(echoR + ((firR * EchoFeedback) >> 7));
            
            echoL &= ~1;
            echoR &= ~1;
            
            WriteToEchoBuffer(echoL, echoR);
            
            if (_echoIndex == 0)
                _echoIndexMax = EchoDelay << 9;
            _echoIndex++;
            if (_echoIndex >= _echoIndexMax)
                _echoIndex = 0;
        }

        _counter++;
        if (!Mute) 
            return (short.CreateSaturating(dacL), short.CreateSaturating(dacR));
        return (0, 0);
    }

    private (short enterFirL, short enterFirR) ReadEchoBuffer()
    {
        var enterFirL = (short)(_aram[(ushort)(EchoStartAddress + _echoIndex * 4)] |
                                (_aram[(ushort)(EchoStartAddress + _echoIndex * 4 + 1)] << 8));
        var enterFirR = (short)(_aram[(ushort)(EchoStartAddress + _echoIndex * 4 + 2)] |
                                (_aram[(ushort)(EchoStartAddress + _echoIndex * 4 + 3)] << 8));
        return (enterFirL, enterFirR);
    }

    private void PollKeyOnAndKeyOff(byte toggleKeyOn, int voice)
    {
        if (TestBitFlag(toggleKeyOn, voice))
        {
            _brrBlockIndex[voice] = 0;
            _brrBufferIndex[voice] = 0;
            _brrBlockAddress[voice] = SampleStartAddress(voice);
            _envStates[voice] = EnvState.Attack;
            _envVolumes[voice] = 0;
            _brrInterpolatePosition[voice] = -5;
            DecodeBrrBlock(voice);
            DecodeBrrBlock(voice);
            DecodeBrrBlock(voice);

            WriteVoiceEndX(false, voice);
        }

        if (TestBitFlag(KeyOff, voice))
        {
            _envStates[voice] = EnvState.Release;
        }
    }

    private void WriteToEchoBuffer(short echoL, short echoR)
    {
        _aram[(ushort)(EchoStartAddress + _echoIndex * 4)] = (byte)echoL;
        _aram[(ushort)(EchoStartAddress + _echoIndex * 4 + 1)] = (byte)(echoL >> 8);
        _aram[(ushort)(EchoStartAddress + _echoIndex * 4 + 2)] = (byte)echoR;
        _aram[(ushort)(EchoStartAddress + _echoIndex * 4 + 3)] = (byte)(echoR >> 8);
    }

    private void AdvanceVoicePitch(int voice)
    {
        var oldInterpolatePosition = _brrInterpolatePosition[voice];
        var newInterpolatePosition = oldInterpolatePosition + VoicePitch(voice);
        if ((newInterpolatePosition & 0xc000) != (oldInterpolatePosition & 0xc000))
            DecodeBrrBlock(voice);
        if (newInterpolatePosition >= 0xc000)
            newInterpolatePosition -= 0xc000;
        _brrInterpolatePosition[voice] = newInterpolatePosition;
    }

    private short GenerateSample(int voice)
    {
        var pos = _brrInterpolatePosition[voice] >> 12;
        var dist = (_brrInterpolatePosition[voice] >> 4) & 0xff;
        var a = (GaussianCoefficients[255 - dist] * _brrDecodeBuffers[voice, pos]) >> 11;
        var b = (GaussianCoefficients[511 - dist] * _brrDecodeBuffers[voice, (pos + 1) % 12]) >> 11;
        var c = (GaussianCoefficients[256 + dist] * _brrDecodeBuffers[voice, (pos + 2) % 12]) >> 11;
        var d = (GaussianCoefficients[dist] * _brrDecodeBuffers[voice, (pos + 3) % 12]) >> 11;
        
        int sample;
        if (VoiceNoiseOn(voice))
            sample = SignExtend15Bit(_noiseSample);
        else 
            sample = int.Clamp(SignExtend15Bit((a + b + c) & 0x7fff) + d, -0x4000, 0x3fff);
        
        sample *= _envVolumes[voice];
        sample >>= 11;
        
        return (short)sample;
    }

    private readonly (short FirL, short FirR)[] _firBuffer = new (short, short)[8];
    private int _firBufferPos;

    private void EnterFir(short enterFirL, short enterFirR)
    {
        _firBuffer[_firBufferPos] = (enterFirL, enterFirR);
        _firBufferPos++;
        _firBufferPos &= 7;
    }

    private (short FirL, short FirR) CalculateFirSample()
    {
        short firL = 0;
        short firR = 0;
        for (var i = 0; i < 7; i++)
        {
            var (bufferL, bufferR) = _firBuffer[(_firBufferPos - (7 - i)) & 7];
            firL += (short)((bufferL * EchoFilterCoefficients(i)) >> 6);
            firR += (short)((bufferR * EchoFilterCoefficients(i)) >> 6);
        }
        firL = short.CreateSaturating(firL + ((_firBuffer[_firBufferPos].FirL * EchoFilterCoefficients(7)) >> 6));
        firR = short.CreateSaturating(firR + ((_firBuffer[_firBufferPos].FirR * EchoFilterCoefficients(7)) >> 6));
        firL &= ~1;
        firR &= ~1;
        return (firL, firR);
    }

    private void UpdateEnvelope(int voice)
    {
        if (_envStates[voice] == EnvState.Release)
        {
            _envVolumes[voice] -= 8;
            if (_envVolumes[voice] < 0)
                _envVolumes[voice] = 0;
        }
        else if (VoiceAdsrEnable(voice))
        {
            switch (_envStates[voice])
            {
                case EnvState.Attack:
                    var rate = VoiceAdsrAttackRate(voice);
        
                    if (rate == 0xf)
                        _envVolumes[voice] += 0x400;
                    else if (ShouldDoAtRate((rate << 1) + 1))
                        _envVolumes[voice] += 32;
        
                    if (_envVolumes[voice] > 0x7ff)
                    {
                        _envVolumes[voice] = 0x7ff;
                        _envStates[voice] = EnvState.Decay;
                    }
                    break;
                case EnvState.Decay:
                    if (ShouldDoAtRate(0x10 + (VoiceAdsrDecayRate(voice) << 1)))
                        _envVolumes[voice] = ExponentialDecay(_envVolumes[voice]);
                    break;
                case EnvState.Sustain:
                    if (ShouldDoAtRate(VoiceAdsrSustainRate(voice)))
                        _envVolumes[voice] = ExponentialDecay(_envVolumes[voice]);
                    break;
            }
        }
        else // VxGAIN type envelope
        {
            var gain = VoiceGain(voice);
            if ((gain & 0x80) != 0)
            {
                if (!ShouldDoAtRate(gain & 0x1f))
                    return;
                _envVolumes[voice] = (gain & 0b0110_0000) switch
                {
                    0b0000_0000 => short.Max(0, (short)(_envVolumes[voice] - 32)),
                    0b0010_0000 => ExponentialDecay(_envVolumes[voice]),
                    0b0100_0000 => short.Min(0x7ff, (short)(_envVolumes[voice] + 32)),
                    0b0110_0000 => (short)(_envVolumes[voice] + ((_envVolumes[voice] < 0x600) ? 32 : 8)),
                    _ => throw new UnreachableException()
                };
            }
            else
            {
                _envVolumes[voice] = (short)(gain << 4);
            }
        }

        if (_envVolumes[voice] is > 0x7ff or < 0)
        {
            _envVolumes[voice] = short.Clamp(_envVolumes[voice], 0, 0x7ff);
            if (_envStates[voice] == EnvState.Attack)
                _envStates[voice] = EnvState.Decay;
        }
        
        if (_envStates[voice] == EnvState.Decay && _envVolumes[voice] >> 8 == VoiceAdsrSustainLevel(voice))
            _envStates[voice] = EnvState.Sustain;
        
    }

    private static short ExponentialDecay(short envVol)
    {
        envVol -= (short)(((envVol - 1) >> 8) + 1);

        if (envVol < 0)
            envVol = 0;

        return envVol;
    }

    public byte Read(byte address)
    {
        return _regs[address];
    }

    public void Write(byte address, byte value)
    {
        if (address == VoiceEndXAddress)
            _regs[VoiceEndXAddress] = 0;
        _regs[address] = value;
    }
}