namespace TurtleSpc;

internal enum EnvState
{
    Release,
    Attack,
    Decay,
    Sustain,
}
internal class Dsp(byte[] aram)
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
    private readonly int[] _samplePositions = new int[VoiceCount];
    private readonly int[] _envVolumes = new int[VoiceCount];
    private readonly EnvState[] _envStates = new EnvState[VoiceCount];
    private byte _lastKeyOn;
    private readonly int[] _lastSample = new int[VoiceCount];

    private ushort _noiseSample = 1;

    private readonly short[,] _brrDecodeBuffers = new short[12, VoiceCount];
    private readonly int[] _brrBlockAddress = new int[VoiceCount];
    private readonly int[] _brrBlockPosition = new int[VoiceCount];
    private readonly int[] _brrBufferPosition = new int[VoiceCount];

    internal ulong _counter;

    private static bool GetBitProperty(byte value, int voice) => ((1 << voice) & value) != 0;

    private static int BytePropertyAddress(int index, int voice) => (voice << 4) | index;

    private sbyte VoiceVolLeft(int voice) => (sbyte)_regs[BytePropertyAddress(0x0, voice)];
    private sbyte VoiceVolRight(int voice) => (sbyte)_regs[BytePropertyAddress(0x1, voice)];

    private int VoicePitch(int voice) => _regs[BytePropertyAddress(0x2, voice)] | (_regs[BytePropertyAddress(0x3, voice)] << 8);

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
        throw new NotImplementedException();
    }

    private sbyte EchoFeedback => (sbyte)_regs[0x0d];

    private bool VoicePitchModulationOn(int voice) => GetBitProperty(_regs[0x2d], voice);
    private bool VoiceNoiseOn(int voice) => GetBitProperty(_regs[0x3d], voice);

    private bool VoiceEchoOn(int voice) => GetBitProperty(_regs[0x4d], voice);

    private int DirectoryAddress => _regs[0x5d] << 8;

    private int EchoStartAddress => _regs[0x6d] << 8;

    private int EchoDelay => _regs[0x7d];

    private sbyte EchoFilterCoefficients(int index) => (sbyte) _regs[BytePropertyAddress(0x0f, index)];


    private static (int Shift, int Filter, bool Loop, bool End) BrrHeaderDecode(byte header) =>
    (
        header >> 4,
        (header >> 2) & 0x03,
        ((header >> 1) & 0x01) != 0,
        (header & 0x01) != 0
    );
    
    private int SampleNormalAddress(int voice)
    {
        var basePtr = DirectoryAddress + VoiceSourceNumber(voice) * 4;
        return _aram[basePtr] | (_aram[basePtr + 1] << 8);
    }
    
    private int SampleLoopAddress(int voice)
    {
        var basePtr = DirectoryAddress + VoiceSourceNumber(voice) * 4 + 2;
        return _aram[basePtr] | (_aram[basePtr + 1] << 8);    
    }

    private bool ShouldForThisSample(int rate)
    {
        if (rate == 0)
            return false;
        return (_counter + TimerOffsets[rate]) % TimerDividers[rate] == 0;
    }
    
    public (short L, short R) OneSample()
    {
        Span<short> samples = stackalloc short[VoiceCount];
        var toggleKeyOn = (byte)(~_lastKeyOn & KeyOn);
        var doPollKeyOn = _counter % 2 == 0;
        for (var voice = 0; voice < VoiceCount; voice++)
        {
            if (doPollKeyOn)
            {
                if (GetBitProperty(toggleKeyOn, voice))
                {
                    _brrBufferPosition[voice] = -5;
                    _brrBlockPosition[voice] = 0;
                    _brrBlockAddress[voice] = SampleNormalAddress(voice);
                    _envStates[voice] = EnvState.Attack;
                    _envVolumes[voice] = 0;
                    // DECODE a brr group into buffer
                }

                if (GetBitProperty(KeyOff, voice))
                {
                    _envStates[voice] = EnvState.Release;
                }

                _lastKeyOn = KeyOn;
            }

            if (_brrBufferPosition[voice] < 0) // "preparing" sample
            {
                _brrBufferPosition[voice]++;
                samples[voice] = 0;
                continue; // doesn't go forward...
            }


            if (_envStates[voice] == EnvState.Release)
            {
                // RELEASE...
            }
            else if (VoiceAdsrEnable(voice))
            {
                
            }
            else // VxGAIN type envelope
            {
                
            }
        }
        var (l, r) = (0, 0);
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