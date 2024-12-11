// See https://aka.ms/new-console-template for more information

using System.Runtime.CompilerServices;
using System.Text;
using SDL3;
using TurtleSpc;
using static SDL3.SDL;

ushort pc;
byte a, x, y, psw, s;
byte[] ram;
var p = Console.ReadLine()!.Trim('"');
Dsp dsp;
using (var file = File.Open(p, FileMode.Open, FileAccess.Read))
using (var reader = new BinaryReader(file))
{
    file.Seek(0x25, SeekOrigin.Begin);
    pc = reader.ReadUInt16();
    a = reader.ReadByte();
    x = reader.ReadByte();
    y = reader.ReadByte();
    psw = reader.ReadByte();
    s = reader.ReadByte();
    file.Seek(0x100, SeekOrigin.Begin);
    ram = reader.ReadBytes(0x1_0000);
    dsp = new Dsp(ram);
    for (var i = 0; i < 128; i++)
    {
        dsp.Write((byte)i, reader.ReadByte());
    }
}

/*for (int i = 0; i < 0x1_0000; i++)
{
    if (i % 16 == 0)
        Console.Write($"\n{i:X4}: ");
    Console.Write($"{ram[i]:X2} ");
}
Console.WriteLine();*/


var spc = new Spc
{
    A = a,
    X = x,
    Y = y,
    Status = (StatusWord) psw,
    SP = s,
    PC = pc,
    Memory = ram,
    Dsp = dsp
};

SDL_SetAppMetadata("asdf", "1.0", "br.why.asdf");

if (!SDL_Init(SDL_InitFlags.SDL_INIT_AUDIO | SDL_InitFlags.SDL_INIT_VIDEO))
{
    throw new ApplicationException("Could not initialize SDL");
}

if (!SDL_CreateWindowAndRenderer("asdf, 640, 480", 640, 480, 0, out var windowPtr, out var rendererPtr))
{
    throw null;
}

var spec = new SDL_AudioSpec
{
    channels = 2,
    format = SDL_AudioFormat.SDL_AUDIO_S16,
    freq = 32000
};

var stream = SDL_OpenAudioDeviceStream(unchecked((uint)-1), ref spec, null, nint.Zero);

if (stream == 0) throw null;

SDL_ResumeAudioStreamDevice(stream);

Span<short> buf = stackalloc short[1024];
int total = 0;
while (true)
{
    while (SDL_PollEvent(out var @event))
    {
        if (@event.type == (uint)SDL_EventType.SDL_EVENT_QUIT)
        {
            SDL_Quit();
            return;
        }
    }
    const int minimumAudio = 16000;
    var avail = SDL_GetAudioStreamAvailable(stream);
    if (avail < minimumAudio)
    {
        unsafe
        {
            for (var i = 0; i < buf.Length / 2; i++)
            {
                (buf[2 * i], buf[2 * i + 1]) = spc.OneSample();
                total++;
            }

            SDL_PutAudioStreamData(stream, (IntPtr)Unsafe.AsPointer(ref buf[0]), buf.Length * sizeof(short));
        }
    }

    SDL_SetRenderDrawColor(rendererPtr, 0, 0, 0, 255);

    SDL_RenderClear(rendererPtr);

    SDL_SetRenderDrawColor(rendererPtr, 255, 255, 255, 255);

    SDL_RenderDebugText(rendererPtr, 0, 0, $"Sample counter: {dsp._counter} PC: {spc.PC:X4}");

    for (int i = 0; i < 8; i++)
    {
        var strb = new StringBuilder();
        strb.Append($"{i << 4:X2}: ");
        for (int j = 0; j < 16; j++)
        {
            strb.AppendFormat("{0:X2} ", dsp.Read((byte)((i << 4) | j)));
        }
        SDL_RenderDebugText(rendererPtr, 0, 12 + i * 12, strb.ToString());
    }
    SDL_RenderPresent(rendererPtr);
}


