// See https://aka.ms/new-console-template for more information

// using System.Runtime.CompilerServices;
// using static SDL3.SDL;

// SDL_SetAppMetadata("asdf", "1.0", "br.why.asdf");

// if (!SDL_Init(SDL_InitFlags.SDL_INIT_AUDIO | SDL_InitFlags.SDL_INIT_VIDEO))
// {
//     throw new ApplicationException("Could not initialize SDL");
// }

// if (!SDL_CreateWindowAndRenderer("asdf, 640, 480", 640, 480, 0, out var windowPtr, out var rendererPtr))
// {
//     throw null;
// }

// var spec = new SDL_AudioSpec
// {
//     channels = 1,
//     format = SDL_AudioFormat.SDL_AUDIO_F32,
//     freq = 8000
// };

// var stream = SDL_OpenAudioDeviceStream(unchecked((uint)-1), ref spec, null, nint.Zero);

// if (stream == 0) throw null;

// SDL_ResumeAudioStreamDevice(stream);

// Span<float> buf = stackalloc float[512];
// int total = 0;
// while (true)
// {
//     while (SDL_PollEvent(out var @event))
//     {
//         if (@event.type == (uint)SDL_EventType.SDL_EVENT_QUIT)
//         {
//             SDL_Quit();
//             return;
//         }
//     }
//     const int minimumAudio = (8000 * sizeof (float)) / 2;
//     if (SDL_GetAudioStreamAvailable(stream) < minimumAudio)
//     {
//         unsafe
//         {
//             for (var i = 0; i < buf.Length; i++)
//             {
//                 var time = total / 8000f;
//                 buf[i] = float.SinPi(1000 * time);
//                 total++;
//             }

//             SDL_PutAudioStreamData(stream, (IntPtr)Unsafe.AsPointer(ref buf[0]), buf.Length * sizeof(float));
//         }
//     }

//     SDL_RenderClear(rendererPtr);
//     SDL_RenderPresent(rendererPtr);
// }
ushort pc;
byte a, x, y, psw, s;
byte[] ram;

using (var file = File.Open(@"C:\Users\vrabb\stuff\projects\TurtleSpc\TurtleSpc\bin\Debug\net9.0\test.spc", FileMode.Open, FileAccess.Read))
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
    Mem = ram
};

spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
spc.OneSample();
