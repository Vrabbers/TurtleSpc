using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TurtleSpc;

public sealed class Dsp(byte[] aram)
{
    [InlineArray(128)]
    private struct Regs
    {
        private byte _el;
    }

    [InlineArray(VoiceCount)]
    private struct VoiceProps<T>
    {
        private T _el;
    }

    [InlineArray(12)]
    private struct BrrDecodeBuf
    {
        private short _el;
    }

    private enum EnvState
    {
        Release,
        Attack,
        Decay,
        Sustain,
    }

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
        0x008, 0x008, 0x009, 0x009, 0x009, 0x00a, 0x00a, 0x00a,
        0x00b, 0x00b, 0x00b, 0x00c, 0x00c, 0x00d, 0x00d, 0x00e,
        0x00e, 0x00f, 0x00f, 0x00f, 0x010, 0x010, 0x011, 0x011,
        0x012, 0x013, 0x013, 0x014, 0x014, 0x015, 0x015, 0x016,
        0x017, 0x017, 0x018, 0x018, 0x019, 0x01a, 0x01b, 0x01b,
        0x01c, 0x01d, 0x01d, 0x01e, 0x01f, 0x020, 0x020, 0x021,
        0x022, 0x023, 0x024, 0x024, 0x025, 0x026, 0x027, 0x028,
        0x029, 0x02a, 0x02b, 0x02c, 0x02d, 0x02e, 0x02f, 0x030,
        0x031, 0x032, 0x033, 0x034, 0x035, 0x036, 0x037, 0x038,
        0x03a, 0x03b, 0x03c, 0x03d, 0x03e, 0x040, 0x041, 0x042,
        0x043, 0x045, 0x046, 0x047, 0x049, 0x04a, 0x04c, 0x04d,
        0x04e, 0x050, 0x051, 0x053, 0x054, 0x056, 0x057, 0x059,
        0x05a, 0x05c, 0x05e, 0x05f, 0x061, 0x063, 0x064, 0x066,
        0x068, 0x06a, 0x06b, 0x06d, 0x06f, 0x071, 0x073, 0x075,
        0x076, 0x078, 0x07a, 0x07c, 0x07e, 0x080, 0x082, 0x084,
        0x086, 0x089, 0x08b, 0x08d, 0x08f, 0x091, 0x093, 0x096,
        0x098, 0x09a, 0x09c, 0x09f, 0x0a1, 0x0a3, 0x0a6, 0x0a8,
        0x0ab, 0x0ad, 0x0af, 0x0b2, 0x0b4, 0x0b7, 0x0ba, 0x0bc,
        0x0bf, 0x0c1, 0x0c4, 0x0c7, 0x0c9, 0x0cc, 0x0cf, 0x0d2,
        0x0d4, 0x0d7, 0x0da, 0x0dd, 0x0e0, 0x0e3, 0x0e6, 0x0e9,
        0x0ec, 0x0ef, 0x0f2, 0x0f5, 0x0f8, 0x0fb, 0x0fe, 0x101,
        0x104, 0x107, 0x10b, 0x10e, 0x111, 0x114, 0x118, 0x11b,
        0x11e, 0x122, 0x125, 0x129, 0x12c, 0x130, 0x133, 0x137,
        0x13a, 0x13e, 0x141, 0x145, 0x148, 0x14c, 0x150, 0x153,
        0x157, 0x15b, 0x15f, 0x162, 0x166, 0x16a, 0x16e, 0x172,
        0x176, 0x17a, 0x17d, 0x181, 0x185, 0x189, 0x18d, 0x191,
        0x195, 0x19a, 0x19e, 0x1a2, 0x1a6, 0x1aa, 0x1ae, 0x1b2,
        0x1b7, 0x1bb, 0x1bf, 0x1c3, 0x1c8, 0x1cc, 0x1d0, 0x1d5,
        0x1d9, 0x1dd, 0x1e2, 0x1e6, 0x1eb, 0x1ef, 0x1f3, 0x1f8,
        0x1fc, 0x201, 0x205, 0x20a, 0x20f, 0x213, 0x218, 0x21c,
        0x221, 0x226, 0x22a, 0x22f, 0x233, 0x238, 0x23d, 0x241,
        0x246, 0x24b, 0x250, 0x254, 0x259, 0x25e, 0x263, 0x267,
        0x26c, 0x271, 0x276, 0x27b, 0x280, 0x284, 0x289, 0x28e,
        0x293, 0x298, 0x29d, 0x2a2, 0x2a6, 0x2ab, 0x2b0, 0x2b5,
        0x2ba, 0x2bf, 0x2c4, 0x2c9, 0x2ce, 0x2d3, 0x2d8, 0x2dc,
        0x2e1, 0x2e6, 0x2eb, 0x2f0, 0x2f5, 0x2fa, 0x2ff, 0x304,
        0x309, 0x30e, 0x313, 0x318, 0x31d, 0x322, 0x326, 0x32b,
        0x330, 0x335, 0x33a, 0x33f, 0x344, 0x349, 0x34e, 0x353,
        0x357, 0x35c, 0x361, 0x366, 0x36b, 0x370, 0x374, 0x379,
        0x37e, 0x383, 0x388, 0x38c, 0x391, 0x396, 0x39b, 0x39f,
        0x3a4, 0x3a9, 0x3ad, 0x3b2, 0x3b7, 0x3bb, 0x3c0, 0x3c5,
        0x3c9, 0x3ce, 0x3d2, 0x3d7, 0x3dc, 0x3e0, 0x3e5, 0x3e9,
        0x3ed, 0x3f2, 0x3f6, 0x3fb, 0x3ff, 0x403, 0x408, 0x40c,
        0x410, 0x415, 0x419, 0x41d, 0x421, 0x425, 0x42a, 0x42e,
        0x432, 0x436, 0x43a, 0x43e, 0x442, 0x446, 0x44a, 0x44e,
        0x452, 0x455, 0x459, 0x45d, 0x461, 0x465, 0x468, 0x46c,
        0x470, 0x473, 0x477, 0x47a, 0x47e, 0x481, 0x485, 0x488,
        0x48c, 0x48f, 0x492, 0x496, 0x499, 0x49c, 0x49f, 0x4a2,
        0x4a6, 0x4a9, 0x4ac, 0x4af, 0x4b2, 0x4b5, 0x4b7, 0x4ba,
        0x4bd, 0x4c0, 0x4c3, 0x4c5, 0x4c8, 0x4cb, 0x4cd, 0x4d0,
        0x4d2, 0x4d5, 0x4d7, 0x4d9, 0x4dc, 0x4de, 0x4e0, 0x4e3,
        0x4e5, 0x4e7, 0x4e9, 0x4eb, 0x4ed, 0x4ef, 0x4f1, 0x4f3,
        0x4f5, 0x4f6, 0x4f8, 0x4fa, 0x4fb, 0x4fd, 0x4ff, 0x500,
        0x502, 0x503, 0x504, 0x506, 0x507, 0x508, 0x50a, 0x50b,
        0x50c, 0x50d, 0x50e, 0x50f, 0x510, 0x511, 0x511, 0x512,
        0x513, 0x514, 0x514, 0x515, 0x516, 0x516, 0x517, 0x517,
        0x517, 0x518, 0x518, 0x518, 0x518, 0x518, 0x519, 0x519
    ];

    private const int VoiceCount = 8;
    private const int VoiceEnvXAddress = 0x08;
    private const int VoiceEndXAddress = 0x7c;
    private const int VoiceOutXAddress = 0x09;
    private const int EndXAddress = 0x7c;
    private const int KeyOnAddress = 0x4c;

    private Regs _regs;
    private readonly byte[] _aram = aram;
    private VoiceProps<short> _envVolumes;
    private VoiceProps<EnvState> _envStates;
    private bool _keyOnWritten;

    private ushort _noiseSample = 1;

    private VoiceProps<BrrDecodeBuf> _brrDecodeBuffers;
    private VoiceProps<ushort> _brrBlockAddress;
    private VoiceProps<sbyte> _brrBlockIndex;
    private VoiceProps<ushort> _brrInterpolatePosition;
    private VoiceProps<sbyte> _brrBufferIndex;

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

    private void WriteVoiceEnvX(byte value, int voice) => _regs[BytePropertyAddress(VoiceEnvXAddress, voice)] = value;

    private void WriteVoiceOutX(sbyte value, int voice) => _regs[BytePropertyAddress(VoiceOutXAddress, voice)] = (byte)value;

    private sbyte MainVolLeft => (sbyte)_regs[0x0c];
    private sbyte MainVolRight => (sbyte)_regs[0x1c];
    private sbyte EchoVolLeft => (sbyte)_regs[0x2c];
    private sbyte EchoVolRight => (sbyte)_regs[0x3c];
    private byte KeyOn => _regs[KeyOnAddress];
    private byte KeyOff => _regs[0x5c];
    private bool Reset => TestBitFlag(_regs[0x6c], 7);
    private bool Mute => TestBitFlag(_regs[0x6c], 6);
    private bool EchoDisable => TestBitFlag(_regs[0x6c], 5);
    private int NoiseFrequency => _regs[0x6c] & 0x1f;

    private void WriteVoiceEndX(bool end, int voice)
    {
        var mask = 1 << voice;
        _regs[VoiceEndXAddress] = (byte)(end ? (_regs[EndXAddress] | mask) : (_regs[EndXAddress] & ~mask));
    }

    private sbyte EchoFeedback => (sbyte)_regs[0x0d];
    private bool VoicePitchModulationOn(int voice) => TestBitFlag(_regs[0x2d], voice);
    private bool VoiceNoiseOn(int voice) => TestBitFlag(_regs[0x3d], voice);
    private bool VoiceEchoOn(int voice) => TestBitFlag(_regs[0x4d], voice);
    private int DirectoryAddress => _regs[0x5d] << 8;
    private int EchoStartAddress => _regs[0x6d] << 8;
    private int EchoDelay => _regs[0x7d] & 0xf;

    private sbyte EchoFilterCoefficients(int index) => (sbyte)_regs[BytePropertyAddress(0x0f, index)];

    private ushort SampleStartAddress(int voice)
    {
        var basePtr = (ushort)(DirectoryAddress + VoiceSourceNumber(voice) * 4);
        return (ushort)(_aram[basePtr] | (_aram[(ushort)(basePtr + 1)] << 8));
    }

    private ushort SampleLoopAddress(int voice)
    {
        var basePtr = (ushort)(DirectoryAddress + VoiceSourceNumber(voice) * 4 + 2);
        return (ushort)(_aram[basePtr] | (_aram[(ushort)(basePtr + 1)] << 8));
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
    private static int SignExtend15Bit(int val) => ((short)(val << 1)) >> 1;

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
        vals[2] = (short)((sbyte)_aram[(ushort)(baseDataAddress + 1)] >> 4);
        vals[1] = SignExtendLeast4Bits(_aram[baseDataAddress]);
        vals[0] = (short)((sbyte)_aram[baseDataAddress] >> 4);

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
                1 => sample + 15 * _brrDecodeBuffers[voice][prev1] / 16,
                2 => sample + 61 * _brrDecodeBuffers[voice][prev1] / 32 - 15 * _brrDecodeBuffers[voice][prev2] / 16,
                3 => sample + 115 * _brrDecodeBuffers[voice][prev1] / 64 - 13 * _brrDecodeBuffers[voice][prev2] / 16,
                _ => throw new UnreachableException()
            };
            _brrDecodeBuffers[voice][bufferIndex] = (short)SignExtend15Bit(short.CreateSaturating(sample));
            bufferIndex++;
        }

        _brrBufferIndex[voice] = (sbyte)(bufferIndex == 12 ? 0 : bufferIndex);
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
            if (_keyOnWritten)
            {
                _keyOnWritten = false;
                if (KeyOn != 0)
                    PollKeyOn();
            }

            for (var voice = 0; voice < VoiceCount; voice++)
            {
                if (TestBitFlag(KeyOff, voice))
                    _envStates[voice] = EnvState.Release;
            }
        }

        for (var voice = 0; voice < VoiceCount; voice++)
        {
            if (_brrInterpolatePosition[voice] > 0xc000) // "preparing" sample
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

        if (Mute)
            return (0, 0);

        return (short.CreateSaturating(dacL), short.CreateSaturating(dacR));
    }

    private void PollKeyOn()
    {
        for (var voice = 0; voice < VoiceCount; voice++)
        {
            if (!TestBitFlag(KeyOn, voice))
                continue;

            _brrBlockIndex[voice] = 0;
            _brrBufferIndex[voice] = 0;
            _brrBlockAddress[voice] = SampleStartAddress(voice);
            _envStates[voice] = EnvState.Attack;
            _envVolumes[voice] = 0;
            _brrInterpolatePosition[voice] = unchecked((ushort)-5);
            DecodeBrrBlock(voice);
            DecodeBrrBlock(voice);
            DecodeBrrBlock(voice);

            WriteVoiceEndX(false, voice);
        }
    }

    private (short enterFirL, short enterFirR) ReadEchoBuffer()
    {
        var enterFirL = (short)(_aram[(ushort)(EchoStartAddress + _echoIndex * 4)] |
                                (_aram[(ushort)(EchoStartAddress + _echoIndex * 4 + 1)] << 8));
        var enterFirR = (short)(_aram[(ushort)(EchoStartAddress + _echoIndex * 4 + 2)] |
                                (_aram[(ushort)(EchoStartAddress + _echoIndex * 4 + 3)] << 8));
        return (enterFirL, enterFirR);
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
        _brrInterpolatePosition[voice] = (ushort)newInterpolatePosition;
    }

    private short GenerateSample(int voice)
    {
        var pos = _brrInterpolatePosition[voice] >> 12;
        var dist = (_brrInterpolatePosition[voice] >> 4) & 0xff;
        var a = (GaussianCoefficients[255 - dist] * _brrDecodeBuffers[voice][pos]) >> 11;
        var b = (GaussianCoefficients[511 - dist] * _brrDecodeBuffers[voice][(pos + 1) % 12]) >> 11;
        var c = (GaussianCoefficients[256 + dist] * _brrDecodeBuffers[voice][(pos + 2) % 12]) >> 11;
        var d = (GaussianCoefficients[dist] * _brrDecodeBuffers[voice][(pos + 3) % 12]) >> 11;

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

    public byte Read(byte address) => _regs[address];

    public void Write(byte address, byte value)
    {
        switch (address)
        {
            case VoiceEndXAddress:
                _regs[VoiceEndXAddress] = 0;
                break;
            case KeyOnAddress:
                _keyOnWritten = true;
                goto default;
            default:
                _regs[address] = value;
                break;
        }
    }
}