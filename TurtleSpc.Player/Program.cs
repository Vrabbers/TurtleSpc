using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using TurtleSpc;
using static SDL3.SDL;

SDL_SetAppMetadata("TurtleSpc", "1.0", "io.github.vrabbers.turtlespc");

if (!SDL_Init(SDL_InitFlags.SDL_INIT_AUDIO | SDL_InitFlags.SDL_INIT_VIDEO))
{
    throw new ApplicationException("Could not initialize SDL");
}

SDL_SetHint(SDL_HINT_RENDER_VSYNC, "1");

if (!SDL_CreateWindowAndRenderer("TurtleSpc", 640, 480, 0, out var windowPtr, out var rendererPtr))
{
    throw new ApplicationException("Could not initialize SDL window");
}

var spec = new SDL_AudioSpec
{
    channels = 2,
    format = SDL_AudioFormat.SDL_AUDIO_S16,
    freq = 32000
};

var stream = SDL_OpenAudioDeviceStream(unchecked((uint)-1), ref spec, null, nint.Zero);

if (stream == nint.Zero)
{ 
    throw new ApplicationException("Could not initialize SDL window");
}

SDL_ResumeAudioStreamDevice(stream);

Span<short> buf = stackalloc short[2048];

Spc? spc = null;
var dialogOpen = false;
ulong total = 0;
while (true)
{
    var lines = 0;
    void WriteLine(string s)
    {
        SDL_RenderDebugText(rendererPtr, 0, lines * 10, s);
        lines++;
    }
    while (SDL_PollEvent(out var @event))
    {
        if (@event.type == (uint)SDL_EventType.SDL_EVENT_QUIT)
        {
            SDL_Quit();
            return;
        }

        if (@event is { type: (uint)SDL_EventType.SDL_EVENT_KEY_DOWN, key.key: (uint) SDL_Keycode.SDLK_RETURN }) 
            spc = null;
    }

    if (spc is null)
    {
        if (!dialogOpen)
        {
            dialogOpen = true;
            SDL_ShowOpenFileDialog((_, fileListPtr, _) =>
            {
                unsafe
                {
                    if (fileListPtr == nint.Zero || *(nint*)fileListPtr == nint.Zero)
                    {
                        var quitEvent = new SDL_Event
                        {
                            quit = new SDL_QuitEvent { type = SDL_EventType.SDL_EVENT_QUIT, timestamp = SDL_GetTicks() }
                        };
                        SDL_PushEvent(ref quitEvent);
                        return;
                    }

                    var firstString = *(nint*)fileListPtr;
                    var file = Marshal.PtrToStringUTF8(firstString)!;
                    using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);
                    spc = Spc.FromSpcFileStream(fileStream);
                    dialogOpen = false;
                }
                
            }, nint.Zero, windowPtr, [], 0, default_location: null, allow_many:false);
        }
    }
    else
    {
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
    }

    SDL_SetRenderDrawColor(rendererPtr, 0, 0, 0, 255);

    SDL_RenderClear(rendererPtr);

    SDL_SetRenderDrawColor(rendererPtr, 255, 255, 255, 255);
    WriteLine("TurtleSpc");

    for (var i = 0; i < buf.Length / 2; i++)
    {
        SDL_RenderPoint(rendererPtr, (i * 640) / (buf.Length / 2), 240 + (buf[2 * i] * 480) / short.MaxValue);
        SDL_RenderPoint(rendererPtr, (i * 640) / (buf.Length / 2), 240 + (buf[2 * i + 1] * 480) / short.MaxValue);
    }

    if (spc is null)
    {
        SDL_RenderPresent(rendererPtr);
        continue;
    }
#if DEBUG
    WriteLine($"PC:{spc.PC:X4} A:{spc.A:X2} X:{spc.X:X2} Y:{spc.Y:X2} SP:{spc.SP:X2} PSW:{(byte)spc.Status:B8} Elapsed: {total/32} ms");

    WriteLine("DSP Registers: ");
    var strb = new StringBuilder();
    for (var i = 0; i < 8; i++)
    {
        strb.Clear();
        strb.Append($"{i << 4:X2}: ");
        for (int j = 0; j < 16; j++)
        {
            strb.Append($"{spc.Dsp!.Read((byte)((i << 4) | j)):X2} ");
        }
        WriteLine(strb.ToString());
    }
#endif
    SDL_RenderPresent(rendererPtr);
}
