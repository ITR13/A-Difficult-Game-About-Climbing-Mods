using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ITRsSpeedrunTimer;

public static class SocketManager
{
    private static NamedPipeClientStream _stream;
    private static StreamWriter _streamWriter;
    private static StreamReader _streamReader;

    private static ServerCommand _expectedCommand = ServerCommand.StartTimer;
    private static TimerPhase _timerPhase = TimerPhase.None;

    private static readonly ReadOnlyMemory<char> StartTimerMessage = new("starttimer\n".ToArray());
    private static readonly ReadOnlyMemory<char> SplitMessage = new("split\n".ToArray());
    private static readonly ReadOnlyMemory<char> SkipMessage = new("skipsplit\n".ToArray());
    private static readonly ReadOnlyMemory<char> GetCurrentSplitNameMessage = new("getcurrentsplitname\n".ToArray());
    private static readonly ReadOnlyMemory<char> GetCurrentTimerPhase = new("getcurrenttimerphase\n".ToArray());
    private static readonly ReadOnlyMemory<char> Reset = new("reset\n".ToArray());

    private static readonly List<string> SplitWithTimeMessage = new(),
        PauseMessage = new(),
        UnpauseMessage = new(),
        SetTimerMessage = new(),
        UpdateStates = new();

    private static bool _communicating = false;

    static SocketManager()
    {
        var un = "un";
        var pause = "pausegametime\n";

        PauseMessage.Add(pause);

        UnpauseMessage.Add(un);
        UnpauseMessage.Add(pause);

        SetTimerMessage.Add("setgametime ");
        SetTimerMessage.Add(null);
        SetTimerMessage.Add("\n");

        SplitWithTimeMessage.Add("setgametime ");
        SplitWithTimeMessage.Add(null);
        SplitWithTimeMessage.Add("\nsplit\n");

        UpdateStates.Add("getcurrenttimerphase\n");
    }

    private static readonly CancellationTokenSource Source = new();

    public static void Start()
    {
        var token = Source.Token;
        var thread = new Thread(ThreadLoop);
        thread.Start();
        token.Register(
            () =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    if (!thread.IsAlive) break;
                    Thread.Sleep(1);
                }

                if (thread.IsAlive)
                {
                    Plugin.LogError($"Aborting thread!");
                    thread.Abort();

                    Thread.Sleep(5000);
                }
            }
        );
    }

    private static async void ThreadLoop()
    {
        var token = Source.Token;
        while (!token.IsCancellationRequested)
        {
            await Connect(token);
            if (token.IsCancellationRequested) return;

            var timeSinceStartup = (int)(Time.realtimeSinceStartup * 1000);
            if (timeSinceStartup < 5000)
            {
                await Task.Delay(5000 - timeSinceStartup, token);
            }

            if (token.IsCancellationRequested) return;
            _ = ReadMessagesLoop(token, _stream);

            CommandQueue.Clear();
            while (!token.IsCancellationRequested && _stream.IsConnected)
            {
                if (CommandQueue.TryDequeue(out var result))
                {
                    Command_Internal(result.Item1, result.Item2, token);
                }
                else if (SyncTime != null)
                {
                    SyncTime_Internal(SyncTime.Value, token);
                    SyncTime = null;
                }
                else
                {
                    Thread.Sleep(1);
                }

                await Task.Yield();
            }

            Plugin.Log("Attempting to close stream...");
            _ = PleaseCloseOrCauseAMemoryLeakIDontCareAtThisPoint(_stream, _streamWriter, _streamReader);
        }
    }

    public static float? SyncTime { private get; set; }
    private static readonly Queue<(ServerCommand, float)> CommandQueue = new();

    private static async Task PleaseCloseOrCauseAMemoryLeakIDontCareAtThisPoint(NamedPipeClientStream stream, StreamWriter streamWriter, StreamReader streamReader)
    {
        await Task.Yield();
        await streamWriter.DisposeAsync();
        streamReader.Dispose();
        stream.ReadTimeout = 1;
        await stream.DisposeAsync();
    }

    public static void Command(ServerCommand command, float time)
    {
        CommandQueue.Enqueue((command, time));
    }

    public static void Stop()
    {
        Plugin.Log("Closing named pipe");
        Source.Cancel();
    }

    private static async Task Connect(CancellationToken token)
    {
        _communicating = true;
        while (!token.IsCancellationRequested)
        {
            Plugin.Log($"Attempting to connect to LiveSplit...");
            try
            {
                _stream = new NamedPipeClientStream(
                    ".",
                    "LiveSplit",
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough
                );
                await _stream.ConnectAsync(token);

                if (token.IsCancellationRequested) return;
                _stream.ReadMode = PipeTransmissionMode.Byte;

                await _stream.WriteAsync("getcurrenttimerphas\ngetcurrentsplitname"u8.ToArray(), token);

                _streamWriter = new StreamWriter(_stream, Encoding.UTF8, 1024, true);
                _streamReader = new StreamReader(_stream, Encoding.UTF8, false, 1024, true);
                break;
            }
            catch (Win32Exception e)
            {
                Plugin.LogError($"Failed to connect to livesplit\n{e}");
                await Task.Delay(15000, token);
            }
        }

        if (token.IsCancellationRequested) return;

        Plugin.Log($"Connected to LiveSplit!");
        _communicating = false;
    }

    private static async void SyncTime_Internal(float time, CancellationToken parentToken)
    {
        if (_communicating || _timerPhase != TimerPhase.Running) return;
        var text = time.ToString(CultureInfo.InvariantCulture);
        SetTimerMessage[^2] = text;
        _communicating = true;
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        var token = tokenSource.Token;
        tokenSource.CancelAfter(5);
        await RunCommand(SetTimerMessage, token);
        _communicating = false;
    }

    private static async void Command_Internal(ServerCommand command, float time, CancellationToken parentToken)
    {
        if (_communicating)
        {
            Plugin.Log($"Wanting to send {command}, but waiting for communication to finish");

            var startTime = DateTime.Now.Ticks;
            while (_communicating)
            {
                if (DateTime.Now.Ticks > startTime + 5000000)
                {
                    Plugin.LogError($"Can't send {command} because we're stuck communicating");
                    return;
                }

                await Task.Yield();
            }
        }

        Plugin.Log($"Sending {command}, expecting {_expectedCommand}");
        _communicating = true;

        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        var token = tokenSource.Token;
        tokenSource.CancelAfter(5000);
        try
        {
            switch (command)
            {
                case ServerCommand.StartTimer:
                    await RunSplit(ServerCommand.StartTimer, 0, token);
                    break;
                case ServerCommand.Reset:
                    await ResetIfRunningOrPaused(token);
                    break;
                case ServerCommand.UpdateStatus:
                    await RunCommand(UpdateStates, token);
                    break;
                case ServerCommand.Pause:
                    await RunCommand(PauseMessage, token);
                    break;
                case ServerCommand.Unpause:
                    await RunCommand(UnpauseMessage, token);
                    break;
                case ServerCommand.SplitFinal:
                case ServerCommand.SplitIntro:
                case ServerCommand.SplitJungle:
                case ServerCommand.SplitGears:
                case ServerCommand.SplitPool:
                case ServerCommand.SplitConstruction:
                case ServerCommand.SplitCave:
                case ServerCommand.SplitIce:
                    if (_expectedCommand <= command && _expectedCommand != ServerCommand.StartTimer)
                        await RunSplit(command, time, token);
                    break;
            }
        }
        finally
        {
            _communicating = false;
        }

        if (token.IsCancellationRequested)
        {
            Plugin.LogError($"Timed out while trying to write command {command}");
        }
    }

    private static async Task RunCommand(List<string> command, CancellationToken token)
    {
        await WriteAsync(new ReadOnlyMemory<char>(string.Join("", command).ToCharArray()), token);
    }

    private static async Task RunSplit(ServerCommand split, float time, CancellationToken token)
    {
        while (_expectedCommand < split)
        {
            _expectedCommand++;
            await WriteAsync(SkipMessage, token);
        }

        if (split == ServerCommand.StartTimer)
        {
            await WriteAsync(StartTimerMessage, token);
        }
        else if (Plugin.UseInGameTime)
        {
            var text = time.ToString(CultureInfo.InvariantCulture);
            Plugin.Log($"Split: {text}");
            SplitWithTimeMessage[^2] = text;
            await WriteAsync(new ReadOnlyMemory<char>(string.Join("", SplitWithTimeMessage).ToCharArray()), token);
        }
        else
        {
            await WriteAsync(SplitMessage, token);
        }

        if (split != ServerCommand.SplitFinal)
        {
            await GetSplitName(token);
        }
        else
        {
            _expectedCommand = ServerCommand.StartTimer;
        }

        await GetTimerPhase(token);
    }

    private static async Task ResetIfRunningOrPaused(CancellationToken token)
    {
        await GetTimerPhase(token);
        if (_timerPhase is TimerPhase.Running or TimerPhase.Paused)
        {
            await WriteAsync(Reset, token);
        }
    }

    private static async Task GetSplitName(CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        _expectedCommand = ServerCommand.UpdateStatus;
        await WriteAsync(GetCurrentSplitNameMessage, token);

        var startTime = DateTime.Now.Ticks;
        while (_expectedCommand == ServerCommand.UpdateStatus && !token.IsCancellationRequested)
        {
            await Task.Yield();
            if (DateTime.Now.Ticks <= startTime + 10000000) continue;
            Plugin.LogError("Failed to get split name from LiveSplit!");
            break;
        }
    }

    private static async Task GetTimerPhase(CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        _timerPhase = TimerPhase.None;
        await WriteAsync(GetCurrentTimerPhase, token);

        var startTime = DateTime.Now.Ticks;
        while (_timerPhase == TimerPhase.None && !token.IsCancellationRequested)
        {
            await Task.Yield();
            if (DateTime.Now.Ticks <= startTime + 10000000) continue;
            Plugin.LogError("Failed to get timerphase from LiveSplit!");
            break;
        }
    }

    private static async Task WriteAsync(ReadOnlyMemory<char> toWrite, CancellationToken token)
    {
        await _streamWriter.WriteAsync(toWrite, token);
        _streamWriter.Flush();
    }

    private static async Task ReadMessagesLoop(CancellationToken token, NamedPipeClientStream stream)
    {
        while (!token.IsCancellationRequested && stream.IsConnected)
        {
            Plugin.Log("Waiting for text...");
            var text = await _streamReader.ReadLineAsync();
            Plugin.Log($"Read `{text}`");
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (text.Length <= 3 && text[0] == '-')
            {
                continue;
            }

            switch (text.Trim())
            {
                case "-":
                    break;
                case "Intro":
                    _expectedCommand = ServerCommand.SplitIntro;
                    break;
                case "Jungle":
                    _expectedCommand = ServerCommand.SplitJungle;
                    break;
                case "Gears":
                    _expectedCommand = ServerCommand.SplitGears;
                    break;
                case "Pool":
                    _expectedCommand = ServerCommand.SplitPool;
                    break;
                case "Construction":
                    _expectedCommand = ServerCommand.SplitConstruction;
                    break;
                case "Cave":
                    _expectedCommand = ServerCommand.SplitCave;
                    break;
                case "Ice":
                    _expectedCommand = ServerCommand.SplitIce;
                    break;
                case "Ending":
                    _expectedCommand = ServerCommand.SplitFinal;
                    break;
                case "Running":
                    _timerPhase = TimerPhase.Running;
                    break;
                case "Paused":
                    _timerPhase = TimerPhase.Paused;
                    break;
                case "Ended":
                    _timerPhase = TimerPhase.Ended;
                    break;
                case "NotRunning":
                    _timerPhase = TimerPhase.NotRunning;
                    break;
                default:
                    _expectedCommand = ServerCommand.SplitFinal;
                    break;
            }
        }
    }
}