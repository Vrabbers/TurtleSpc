namespace TurtleSpc;


internal class Dsp
{
    public struct Voice
    {
        public bool Echo;
        public bool OldKeyOn;
        public bool KeyOn;
        public bool OldKeyOff;
        public bool KeyOff;
        public bool EndOfSample;
        public bool PitchModulation;
        public bool NoiseOn;
        public sbyte VolLeft;
        public sbyte VolRight;
        public ushort Pitch;
        public byte SampleSource;
        public bool AdsrEnable;
        public int AttackRate;
        public int DecayRate;
        public int SustainLevel;
        public int SustainRate;
        public byte GainSettings;
        public byte EnvelopeValue;
        public short SampleValue;
        public int SamplePosition;
    }

    public sbyte MainVolumeLeft { get; set; }
    public sbyte MainVolumeRight { get; set; }
    
    public sbyte EchoVolumeLeft { get; set; }
    public sbyte EchoVolumeRight { get; set; }

    public bool Reset { get; set; } = true;
    public bool Mute { get; set; } = true;
    public bool DisableEchoWrite { get; set; } = true;
    public byte NoiseFrequency { get; set; } = 0;
    
    public sbyte EchoFeedback { get; set; }
    
    public byte SampleDirectoryPage { get; set; }
    
    public byte EchoStartAddress { get; set; }
    
    public byte EchoDelay { get; set; }

    public sbyte[] FirCoefficients { get; } = new sbyte[8];
    
    public Voice[] VoiceProps { get; } = new Voice[8];

    private byte[] _aram;

    public byte Read(byte address)
    {
        return 0;
    }

    public void Write(byte address, byte value)
    {
        return;
    }
}