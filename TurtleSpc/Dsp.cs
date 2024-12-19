using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TurtleSpc;

internal enum EnvState
{
    Release,
    Attack,
    Decay,
    Sustain,
}
internal sealed class Dsp(byte[] aram)
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

    private const int VoiceCount = 8;
    private const int VoiceRegMask = 0x0f;
    private const int VoiceEnvXAddress = 0x08;
    private const int VoiceOutXAddress = 0x09;
    private const int EndXAddress = 0x7c;

    private readonly byte[] _regs = new byte[128];
    private byte[] _aram = aram;
    private readonly short[] _envVolumes = new short[VoiceCount];
    private readonly EnvState[] _envStates = new EnvState[VoiceCount];
    private byte _lastKeyOn;
    private readonly short[] _lastSample = new short[VoiceCount];

    private ushort _noiseSample = 1;

    private readonly short[,] _brrDecodeBuffers = new short[VoiceCount, 12];
    private readonly ushort[] _brrBlockAddress = new ushort[VoiceCount];
    private readonly sbyte[] _brrBlockIndex = new sbyte[VoiceCount];
    private readonly int[] _brrInterpolatePosition = new int[VoiceCount];
    private readonly byte[] _brrBufferIndex = new byte[VoiceCount];

    internal ulong _counter;

    private static bool GetBitProperty(byte value, int voice) => ((1 << voice) & value) != 0;

    private static int BytePropertyAddress(int index, int voice) => (voice << 4) | index;

    private sbyte VoiceVolLeft(int voice) => (sbyte)_regs[BytePropertyAddress(0x0, voice)];
    private sbyte VoiceVolRight(int voice) => (sbyte)_regs[BytePropertyAddress(0x1, voice)];

    private int VoicePitch(int voice) => (_regs[BytePropertyAddress(0x2, voice)] | (_regs[BytePropertyAddress(0x3, voice)] << 8)) & 0x3fff;

    private byte VoiceSourceNumber(int voice) => _regs[BytePropertyAddress(0x4, voice)];

    private bool VoiceAdsrEnable(int voice) => GetBitProperty(_regs[BytePropertyAddress(0x5, voice)], 7);
    private int VoiceAdsrDecayRate(int voice) => (_regs[BytePropertyAddress(0x5, voice)] >> 4) & 0x07;
    private int VoiceAdsrAttackRate(int voice) => _regs[BytePropertyAddress(0x5, voice)] & 0x0f;
    private int VoiceAdsrSustainLevel(int voice) => (_regs[BytePropertyAddress(0x6, voice)] >> 5) & 0x07;
    private int VoiceAdsrSustainRate(int voice) => _regs[BytePropertyAddress(0x6, voice)] & 0x1f;

    private byte VoiceGain(int voice) => _regs[BytePropertyAddress(0x7, voice)];

    private void WriteVoiceEnvX(byte value, int voice)
    {
        _regs[BytePropertyAddress(0x8, voice)] = value;
    }

    private void WriteVoiceOutX(sbyte value, int voice)
    {
        _regs[BytePropertyAddress(0x9, voice)] = (byte)value;
    }


    private sbyte MainVolLeft => (sbyte)_regs[0x0c];
    private sbyte MainVolRight => (sbyte)_regs[0x1c];
    private sbyte EchoVolLeft => (sbyte)_regs[0x2c];
    private sbyte EchoVolRight => (sbyte)_regs[0x3c];
    private byte KeyOn => _regs[0x4c];

    private byte KeyOff => _regs[0x5c];
    private bool Reset => GetBitProperty(_regs[0x6c], 7);
    private bool Mute => GetBitProperty(_regs[0x6c], 6);
    private bool EchoDisable => GetBitProperty(_regs[0x6c], 5);
    private int NoiseFrequency => _regs[0x6c] & 0x1f;


    private void WriteVoiceEndX(bool end, int voice)
    {
        var mask = 1 << voice;
        _regs[VoiceEnvXAddress] = (byte)(end ? (_regs[EndXAddress] | mask) : (_regs[EndXAddress] & ~mask ));
    }

    private sbyte EchoFeedback => (sbyte)_regs[0x0d];

    private bool VoicePitchModulationOn(int voice) => GetBitProperty(_regs[0x2d], voice);
    private bool VoiceNoiseOn(int voice) => GetBitProperty(_regs[0x3d], voice);

    private bool VoiceEchoOn(int voice) => GetBitProperty(_regs[0x4d], voice);

    private int DirectoryAddress => _regs[0x5d] << 8;

    private int EchoStartAddress => _regs[0x6d] << 8;

    private int EchoDelay => _regs[0x7d];

    private sbyte EchoFilterCoefficients(int index) => (sbyte) _regs[BytePropertyAddress(0x0f, index)];

    private ushort SampleNormalAddress(int voice)
    {
        var basePtr = DirectoryAddress + VoiceSourceNumber(voice) * 4;
        return (ushort)(_aram[basePtr] | (_aram[basePtr + 1] << 8));
    }
    
    private ushort SampleLoopAddress(int voice)
    {
        var basePtr = DirectoryAddress + VoiceSourceNumber(voice) * 4 + 2;
        return (ushort)(_aram[basePtr] | (_aram[basePtr + 1] << 8));
    }

    private bool ShouldForThisSample(int rate)
    {
        if (rate == 0)
            return false;
        return (_counter + TimerOffsets[rate]) % TimerDividers[rate] == 0;
    }
    
    private static (int Shift, int Filter, bool Loop, bool End) BrrHeaderDecode(byte header) =>
    (
        header >> 4,
        (header >> 2) & 0x03,
        ((header) & 0x02) != 0,
        (header & 0x01) != 0
    );

    private void DecodeBrrBlock(int voice)
    {
        static short SignExtendLower4Bits(byte v)
        {
            if ((v & 0b0000_1000) != 0)
                return (short)((v & 0x0f) | (0xfff0)); 
            return (short)(v & 0x0f);
        }
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
        vals[3] = SignExtendLower4Bits(_aram[(ushort)(baseDataAddress + 1)]);
        vals[2] = (short)(((sbyte)_aram[(ushort)(baseDataAddress + 1)]) >> 4);
        vals[1] = SignExtendLower4Bits(_aram[baseDataAddress]);
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
            _brrDecodeBuffers[voice, bufferIndex] = (short)(sample & 0xfffe);
            bufferIndex++;
        }
        Debug.Assert(bufferIndex <= 12);
        Debug.Assert(blockIndex < 16);

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
        var toggleKeyOn = (byte)(~_lastKeyOn & KeyOn);
        var pollKeyOn = _counter % 2 == 0;
        for (var voice = 0; voice < VoiceCount; voice++)
        {
            if (pollKeyOn)
            {
                if (GetBitProperty(toggleKeyOn, voice))
                {
                    _brrBlockIndex[voice] = 0;
                    _brrBlockAddress[voice] = SampleNormalAddress(voice);
                    _envStates[voice] = EnvState.Attack;
                    _envVolumes[voice] = 0x7ff;
                    _brrInterpolatePosition[voice] = -5;
                    DecodeBrrBlock(voice);
                    DecodeBrrBlock(voice);
                    DecodeBrrBlock(voice);

                    WriteVoiceEndX(false, voice);
                }

                if (GetBitProperty(KeyOff, voice))
                {
                    _envStates[voice] = EnvState.Release;
                }

                _lastKeyOn = KeyOn;
            }

            if (_brrInterpolatePosition[voice] < 0) // "preparing" sample
            {
                _brrInterpolatePosition[voice]++;
                samplesL[voice] = 0;
                samplesR[voice] = 0;
                WriteVoiceOutX(0, voice);
            }
            else 
            {
                if (_envStates[voice] == EnvState.Release)
                {
                    _envVolumes[voice] = (short)int.Max(0, _envVolumes[voice] - 8);
                }
                else if (VoiceAdsrEnable(voice))
                {
                    
                }
                else // VxGAIN type envelope
                {
                    
                }

                var sample = (int)_brrDecodeBuffers[voice, _brrInterpolatePosition[voice] >> 12];
                sample = (sample * _envVolumes[voice]) / 0x800;
                WriteVoiceOutX((sbyte)(sample >> 8), voice);
                samplesL[voice] = (short)((sample * VoiceVolLeft(voice)) / 0x80);
                samplesR[voice] = (short)((sample * VoiceVolRight(voice)) / 0x80);
                
                var oldInterpolatePosition = _brrInterpolatePosition[voice];
                var newInterpolatePosition = oldInterpolatePosition + VoicePitch(voice);
                if ((newInterpolatePosition & 0xc000) != (oldInterpolatePosition & 0xc000))
                    DecodeBrrBlock(voice);
                if (newInterpolatePosition >= 0xc000)
                    newInterpolatePosition -= 0xc000;
                _brrInterpolatePosition[voice] = newInterpolatePosition;
            }
            WriteVoiceEnvX((byte)(_envVolumes[voice] >> 4), voice);
        }
        var l = 0;
        var r = 0;
        for (var i = 0; i < VoiceCount; i++)
        {
            l += samplesL[i];
            r += samplesR[i];
        }
        _counter++;
        return (short.CreateSaturating(l), short.CreateSaturating(r));
    }

    public byte Read(byte address)
    {
        return _regs[address];
    }

    public void Write(byte address, byte value)
    {
        _regs[address] = value;
    }
}