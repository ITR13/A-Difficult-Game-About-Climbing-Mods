using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace ITRsSpeedrunTimer;

public static class SocketManager
{
    private static StreamWriter _streamWriter;
    private static StreamReader _streamReader;
    private static bool _communicating = false;
    private static long _timer, _timerMax;

    public static readonly Queue<ServerCommand> Commands = new();
    public static float TimeToSync;

    private static ServerCommand _expectedCommand = ServerCommand.StartTimer;
    private static TimerPhase _timerPhase = TimerPhase.None;

    private static readonly ReadOnlyMemory<char> StartTimerMessage =
        new("starttimer\r\n".ToArray());

    private static readonly ReadOnlyMemory<char> SplitMessage = new("split\r\n".ToArray());
    private static readonly ReadOnlyMemory<char> SkipMessage = new("skipsplit\r\n".ToArray());

    private static readonly ReadOnlyMemory<char> GetCurrentSplitName =
        new("getcurrentsplitname\r\n".ToArray());

    private static readonly ReadOnlyMemory<char> GetCurrentTimerPhase =
        new("getcurrenttimerphase\r\n".ToArray());

    private static readonly ReadOnlyMemory<char> Reset =
        new("reset\r\n".ToArray());

    private static readonly List<string> FinalSplitMessage = new(),
        PauseMessage = new(),
        UnpauseMessage = new(),
        SetTimerMessage = new(),
        UpdateStates = new();

    static SocketManager()
    {
        var un = "un";
        var pause = "pausegametime\r\n";

        PauseMessage.Add(pause);

        UnpauseMessage.Add(un);
        UnpauseMessage.Add(pause);

        SetTimerMessage.Add("setgametime ");
        SetTimerMessage.Add(null);
        SetTimerMessage.Add("\r\n");

        FinalSplitMessage.Add("setgametime ");
        FinalSplitMessage.Add(null);
        FinalSplitMessage.Add("\r\nsplit\r\n");

        UpdateStates.Add("getcurrenttimerphase\r\n");
    }

    private static void Connect()
    {
        if (_communicating) return;
        if (!Plugin.UseServer) return;

        _communicating = true;

        Plugin.Log($"Connecting to server...");
        Stream stream;
        try
        {
            stream = new NamedPipeClientStream("LiveSplit");
        }
        catch (Win32Exception win32Exception)
        {
            Plugin.LogError($"Livesplit might be closed!\n{win32Exception.Message}");
            _timer = DateTime.Now.Ticks + _timerMax * TimeSpan.TicksPerSecond;
            _timerMax = Math.Min(_timerMax + 2, 10);
            _streamWriter = null;
            _streamReader = null;
            return;
        }
        finally
        {
            _communicating = false;
        }

        if (stream is not { CanWrite: true, CanRead: true })
        {
            Plugin.LogError($"Livesplit is closed!");
            _timer = DateTime.Now.Ticks + _timerMax * TimeSpan.TicksPerSecond;
            _timerMax = Math.Min(_timerMax + 2, 10);
            _streamWriter = null;
            _streamReader = null;
            return;
        }

        _streamWriter = new StreamWriter(stream);
        _streamReader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);

        Commands.Clear();
        ReadMessagesLoop(_streamReader);

        _timer = DateTime.Now.Ticks + 1 * TimeSpan.TicksPerSecond;
        _timerMax = 1;
    }

    public static void ThreadedLoop(CancellationToken token)
    {
        var previousTime = -1f;
        while (!token.IsCancellationRequested)
        {
            Thread.Sleep(0);
            try
            {
                CheckAll(ref previousTime, token);
            }
            catch (Exception e)
            {
                Plugin.LogError($"Encountered an error when socketing: {e.Message}\n{e.StackTrace}");
            }
        }
    }

    private static void CheckAll(ref float previousTime, CancellationToken token)
    {
        if (_communicating) return;

        if (_streamWriter?.BaseStream is not { CanWrite: true })
        {
            if (_timer > DateTime.Now.Ticks) return;
            Connect();
            return;
        }

        // Since we compare a float to itself, we don't need to smush the comparison
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (Commands.Count == 0 && TimeToSync != previousTime && _timerPhase != TimerPhase.Ended)
        {
            var text = TimeToSync.ToString(CultureInfo.InvariantCulture);
            // Plugin.Log($"Syncing time: {text}");
            previousTime = TimeToSync;
            SetTimerMessage[^2] = text;
            Command(SetTimerMessage, token);
        }
        else
        {
            while (Commands.TryDequeue(out var command))
            {
                // Plugin.Log($"Command: {command}");
                switch (command)
                {
                    case ServerCommand.StartTimer:
                        CommandAndGetSplitname(StartTimerMessage, token);
                        break;
                    case ServerCommand.SplitFinal:
                        FinalSplit(token);
                        break;
                    case ServerCommand.Reset:
                        TimeToSync = 0;
                        ResetIfRunningOrPaused(token);
                        break;
                    case ServerCommand.UpdateStatus:
                        Command(UpdateStates, token);
                        break;
                    case ServerCommand.Pause:
                        Command(PauseMessage, token);
                        break;
                    case ServerCommand.Unpause:
                        Command(UnpauseMessage, token);
                        break;
                    case ServerCommand.SplitIntro:
                    case ServerCommand.SplitJungle:
                    case ServerCommand.SplitGears:
                    case ServerCommand.SplitPool:
                    case ServerCommand.SplitConstruction:
                    case ServerCommand.SplitCave:
                    case ServerCommand.SplitIce:
                        if (_expectedCommand <= command && _expectedCommand != ServerCommand.StartTimer)
                            CommandAndGetSplitname(SplitMessage, token);
                        break;
                }
            }
        }
    }

    private static async void Command(List<string> command, CancellationToken token)
    {
        _communicating = true;
        try
        {
            await _streamWriter.WriteAsync(string.Join("", command));
        }
        finally
        {
            _communicating = false;
        }
    }

    private static async void CommandAndGetSplitname(ReadOnlyMemory<char> command, CancellationToken token)
    {
        // Plugin.Log($"Sending command: " + new string(command));
        _communicating = true;
        try
        {
            await _streamWriter.WriteAsync(command, token);
            await _streamWriter.WriteAsync(GetCurrentSplitName, token);
        }
        finally
        {
            _communicating = false;
        }
    }

    private static async void FinalSplit(CancellationToken token)
    {
        // Plugin.Log($"Sending final split");
        _communicating = true;
        try
        {
            while (_expectedCommand < ServerCommand.SplitFinal)
            {
                _expectedCommand++;
                await _streamWriter.WriteAsync(SkipMessage, token);
            }

            if (Plugin.UseInGameTime)
            {
                var text = TimeToSync.ToString(CultureInfo.InvariantCulture);
                Plugin.Log($"Final Split: {text}");
                FinalSplitMessage[^2] = text;
                await _streamWriter.WriteAsync(string.Join("", FinalSplitMessage));
            }
            else
            {
                await _streamWriter.WriteAsync(SplitMessage, token);
            }

            await _streamWriter.WriteAsync(GetCurrentTimerPhase, token);
        }
        finally
        {
            _expectedCommand = ServerCommand.StartTimer;
            _communicating = false;
        }
    }

    private static async void ResetIfRunningOrPaused(CancellationToken token)
    {
        // Plugin.Log($"Maybe sending reset");
        _communicating = true;
        _timerPhase = TimerPhase.None;
        try
        {
            await _streamWriter.WriteAsync(GetCurrentTimerPhase, token);
            while (_timerPhase == TimerPhase.None)
            {
                await Task.Delay(1);
            }

            if (_timerPhase is TimerPhase.Running or TimerPhase.Paused)
            {
                await _streamWriter.WriteAsync(Reset, token);
            }
        }
        finally
        {
            _communicating = false;
        }
    }

    private static async void ReadMessagesLoop(StreamReader reader)
    {
        while (reader.BaseStream.CanRead)
        {
            var text = await reader.ReadLineAsync();

            if (text.Length <= 0)
            {
                await Task.Yield();
                continue;
            }

            // Plugin.Log($"`{text}`");

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