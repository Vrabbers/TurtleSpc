using System.Runtime.InteropServices;
using System.Text;

using System.Buffers;
using System.Runtime.CompilerServices;

using TurtleSpc;

using SDL;
using static SDL.SDL3;

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
static unsafe SDL_AppResult SDL_AppInit(nint* appState, int argc, byte** argv)
{
    SDL_SetAppMetadata("TurtleSpc", "1.0", "io.github.vrabbers.turtlespc");

    if (!SDL_Init(SDL_InitFlags.SDL_INIT_AUDIO | SDL_InitFlags.SDL_INIT_VIDEO))
    {
        throw new ApplicationException("Could not initialize SDL");
    }

    SDL_SetHint(SDL_HINT_RENDER_VSYNC, "1");

    if (!SDL_CreateWindowAndRenderer("TurtleSpc"u8, 640, 480, 0, windowPtr, rendererPtr))
    {
        throw new ApplicationException("Could not initialize SDL window");
    }

    var spec = new SDL_AudioSpec
    {
        channels = 2,
        format = SDL_AudioFormat.SDL_AUDIO_S16LE,
        freq = 32000
    };

    stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, &AudioCallback, 0);

    if (stream == null)
    {
        throw new ApplicationException("Could not initialize SDL window");
    }

    SDL_ResumeAudioStreamDevice(stream);

    return SDL_AppResult.SDL_APP_CONTINUE;
}

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
static unsafe SDL_AppResult SDL_AppIterate(nint appState)
{
    var lines = 0;

    void WriteLine(string s)
    {
        SDL_RenderDebugText(renderer, 0, lines * 10, s);
        lines++;
    }

    SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);

    SDL_RenderClear(renderer);

    SDL_SetRenderDrawColor(renderer, 255, 255, 255, 255);
    WriteLine("TurtleSpc");

    SDL_LockAudioStream(stream);
    try
    {
        for (var i = 0; i < buf.Length / 2; i++)
        {
            SDL_SetRenderDrawColor(renderer, 100, 100, 255, 255);
            SDL_RenderPoint(renderer, (i * 640) / (buf.Length / 2), 240 + (buf[2 * i] * 480) / short.MaxValue);
            
            SDL_SetRenderDrawColor(renderer, 255, 100, 100, 255);
            SDL_RenderPoint(renderer, (i * 640) / (buf.Length / 2), 240 + (buf[2 * i + 1] * 480) / short.MaxValue);
        }
    }
    finally
    {
        SDL_UnlockAudioStream(stream);
    }

    SDL_SetRenderDrawColor(renderer, 255, 255, 255, 255);

#if DEBUG
    if (spc is not null)
    {
        WriteLine($"PC:{spc.PC:X4} A:{spc.A:X2} X:{spc.X:X2} Y:{spc.Y:X2} SP:{spc.SP:X2} PSW:{(byte)spc.Status:B8} Elapsed: {total / 32} ms");

        WriteLine("DSP Registers: ");
        var sb = new StringBuilder();
        for (var i = 0; i < 8; i++)
        {
            sb.Clear();
            sb.Append($"{i << 4:X2}: ");

            for (int j = 0; j < 16; j++)
            {
                sb.Append($"{spc.Dsp!.Read((byte)((i << 4) | j)):X2} ");
            }

            WriteLine(sb.ToString());
        }
    }
#endif

    SDL_RenderPresent(renderer);
    return SDL_AppResult.SDL_APP_CONTINUE;
}

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
static unsafe SDL_AppResult SDL_AppEvent(nint appState, SDL_Event* @event)
{
    if (@event->type == (uint)SDL_EventType.SDL_EVENT_QUIT)
    {
        return SDL_AppResult.SDL_APP_SUCCESS;
    }

    if (*@event is { type: (uint)SDL_EventType.SDL_EVENT_KEY_DOWN, key.key: SDL_Keycode.SDLK_RETURN })
    {
        SDL_LockAudioStream(stream);
        try
        {
            spc = null;
        }
        finally
        {
            SDL_UnlockAudioStream(stream);
        }
    }

    if (spc is null && !dialogOpen)
    {
        dialogOpen = true;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        static void DialogCallback(nint userData, byte** fileList, int filter)
        {
            if (fileList == null || *(nint*)fileList == nint.Zero)
            {
                var quitEvent = new SDL_Event
                {
                    quit = new SDL_QuitEvent { type = SDL_EventType.SDL_EVENT_QUIT, timestamp = SDL_GetTicks() }
                };

                SDL_PushEvent(&quitEvent);
                return;
            }

            var firstString = *(nint*)fileList;
            var file = Marshal.PtrToStringUTF8(firstString)!;

            using var fileStream = File.OpenRead(file);
            spc = Spc.FromSpcFileStream(fileStream);

            dialogOpen = false;
        }

        SDL_ShowOpenFileDialog(&DialogCallback, nint.Zero, window, null, 0, default_location: (byte*)null, allow_many: false);
    }

    return SDL_AppResult.SDL_APP_CONTINUE;
}

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
static unsafe void SDL_AppQuit(nint appState, SDL_AppResult result)
{
    return;
}

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
static unsafe void AudioCallback(nint userData, SDL_AudioStream* stream, int additionalAmount, int totalAmount)
{
    if (spc is null) return;

    if (additionalAmount == 0) return;

    ArrayPool<short>.Shared.Return(buf);
    buf = ArrayPool<short>.Shared.Rent(int.Max(2048, additionalAmount));

    for (var i = 0; i < buf.Length / 2; i++)
    {
        (buf[2 * i], buf[2 * i + 1]) = spc.OneSample();
        total++;
    }

    fixed (short* pBuf = buf)
    {
        SDL_PutAudioStreamData(stream, (nint)pBuf, buf.Length * sizeof(short));
    }
}

unsafe
{
    return SDL_EnterAppMainCallbacks(0, null,
        &SDL_AppInit,
        &SDL_AppIterate,
        &SDL_AppEvent,
        &SDL_AppQuit);
}
unsafe partial class Program
{
    private static Spc? spc;

    [FixedAddressValueType]
    private static SDL_Window* window;

    private static SDL_Window** windowPtr
    {
        get
        {
            fixed (SDL_Window** windowPtr = &window)
            {
                return windowPtr;
            }
        }
    }

    [FixedAddressValueType]
    private static SDL_Renderer* renderer;

    private static SDL_Renderer** rendererPtr
    {
        get
        {
            fixed (SDL_Renderer** rendererPtr = &renderer)
            {
                return rendererPtr;
            }
        }
    }

    private static SDL_AudioStream* stream;

    private static bool dialogOpen;
    private static int total;

    private static short[] buf = ArrayPool<short>.Shared.Rent(0);
}