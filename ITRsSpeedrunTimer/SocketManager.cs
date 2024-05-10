using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ITRsSpeedrunTimer;

public static class SocketManager
{
    private static Socket _socket;
    private static bool _communicating = false;
    private static long _timer, _timerMax;

    public static readonly Queue<ServerCommand> Commands = new();
    public static float TimeToSync;

    private static ServerCommand _expectedCommand = ServerCommand.StartTimer;
    private static TimerPhase _timerPhase = TimerPhase.None;

    private static readonly ReadOnlyMemory<byte> StartTimerMessage =
        new("starttimer\r\n".Select(c => (byte)c).ToArray());

    private static readonly ReadOnlyMemory<byte> SplitMessage = new("split\r\n".Select(c => (byte)c).ToArray());
    private static readonly ReadOnlyMemory<byte> SkipMessage = new("skipsplit\r\n".Select(c => (byte)c).ToArray());

    private static readonly ReadOnlyMemory<byte> GetCurrentSplitName =
        new("getcurrentsplitname\r\n".Select(c => (byte)c).ToArray());

    private static readonly ReadOnlyMemory<byte> GetCurrentTimerPhase =
        new("getcurrenttimerphase\r\n".Select(c => (byte)c).ToArray());

    private static readonly ReadOnlyMemory<byte> Reset =
        new("reset\r\n".Select(c => (byte)c).ToArray());

    private static readonly List<ArraySegment<byte>> FinalSplitMessage = new(),
        PauseMessage = new(),
        UnpauseMessage = new(),
        SetTimerMessage = new(),
        UpdateStates = new();

    static SocketManager()
    {
        var encoding = Encoding.ASCII;
        var un = encoding.GetBytes("un");
        var pause = encoding.GetBytes("pausegametime\r\n");

        PauseMessage.Add(pause);

        UnpauseMessage.Add(un);
        UnpauseMessage.Add(pause);

        SetTimerMessage.Add(encoding.GetBytes("setgametime "));
        SetTimerMessage.Add(null);
        SetTimerMessage.Add(encoding.GetBytes("\r\n"));

        FinalSplitMessage.Add(encoding.GetBytes("setgametime "));
        FinalSplitMessage.Add(null);
        FinalSplitMessage.Add(encoding.GetBytes("\r\nsplit\r\n"));
        
        UpdateStates.Add(encoding.GetBytes("getcurrenttimerphase\r\n"));
    }

    private static async void Connect()
    {
        if (_communicating) return;
        if (!Plugin.UseServer) return;

        Disconnect();
        _communicating = true;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        Plugin.Log($"Connecting to server...");
        try
        {
            await _socket.ConnectAsync("localhost", 16834);
        }
        catch (SocketException socketException)
        {
            Plugin.LogError($"Failed to connect to server! Did you remember to start it?\n{socketException.Message}");
            _timer = DateTime.Now.Ticks + _timerMax * TimeSpan.TicksPerSecond;
            _timerMax = Math.Min(_timerMax + 2, 30);
            return;
        }
        finally
        {
            _communicating = false;
        }

        // Plugin.Log($"Starting socket loop...");
        ReadMessagesLoop(_socket);

        _timer = 1;
        _timerMax = 1;
    }

    private static void Disconnect()
    {
        // Plugin.Log($"Disconnecting...");
        if (_socket == null) return;
        if (_socket.Connected) _socket.Disconnect(false);
        _socket = null;
    }

    public static void ThreadedLoop(CancellationToken token)
    {
        var previousTime = -1f;
        while (!token.IsCancellationRequested)
        {
            Thread.Sleep(0);
            try
            {
                CheckAll(ref previousTime);
            }
            catch (Exception e)
            {
                Plugin.LogError($"Encountered an error when socketing: {e.Message}\n{e.StackTrace}");
            }
        }
    }

    private static void CheckAll(ref float previousTime)
    {
        if (_communicating) return;

        if (_socket is not { Connected: true })
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
            SetTimerMessage[^2] = Encoding.ASCII.GetBytes(text);
            Command(SetTimerMessage);
        }
        else
        {
            while (Commands.TryDequeue(out var command))
            {
                // Plugin.Log($"Command: {command}");
                switch (command)
                {
                    case ServerCommand.StartTimer:
                        CommandAndGetSplitname(StartTimerMessage);
                        break;
                    case ServerCommand.SplitFinal:
                        FinalSplit();
                        break;
                    case ServerCommand.Reset:
                        TimeToSync = 0;
                        ResetIfRunningOrPaused();
                        break;
                    case ServerCommand.UpdateStatus:
                        Command(UpdateStates);
                        break;
                    case ServerCommand.Pause:
                        Command(PauseMessage);
                        break;
                    case ServerCommand.Unpause:
                        Command(UnpauseMessage);
                        break;
                    case ServerCommand.SplitIntro:
                    case ServerCommand.SplitJungle:
                    case ServerCommand.SplitGears:
                    case ServerCommand.SplitPool:
                    case ServerCommand.SplitConstruction:
                    case ServerCommand.SplitCave:
                    case ServerCommand.SplitIce:
                        if (_expectedCommand <= command && _expectedCommand != ServerCommand.StartTimer)
                            CommandAndGetSplitname(SplitMessage);
                        break;
                }
            }
        }
    }

    private static async void Command(List<ArraySegment<byte>> command)
    {
        _communicating = true;
        try
        {
            await _socket.SendAsync(command, SocketFlags.None);
        }
        finally
        {
            _communicating = false;
        }
    }

    private static async void CommandAndGetSplitname(ReadOnlyMemory<byte> command)
    {
        // Plugin.Log($"Sending command: " + new string(command.ToArray().Select(c => (char)c).ToArray()));
        _communicating = true;
        try
        {
            await _socket.SendAsync(command, SocketFlags.None);
            await _socket.SendAsync(GetCurrentSplitName, SocketFlags.None);
        }
        finally
        {
            _communicating = false;
        }
    }

    private static async void FinalSplit()
    {
        // Plugin.Log($"Sending final split");
        _communicating = true;
        try
        {
            while (_expectedCommand < ServerCommand.SplitFinal)
            {
                _expectedCommand++;
                await _socket.SendAsync(SkipMessage, SocketFlags.None);
            }

            if (Plugin.UseInGameTime)
            {
                var text = TimeToSync.ToString(CultureInfo.InvariantCulture);
                Plugin.Log($"Final Split: {text}");
                FinalSplitMessage[^2] = Encoding.ASCII.GetBytes(text);
                await _socket.SendAsync(FinalSplitMessage, SocketFlags.None);
            }
            else
            {
                await _socket.SendAsync(SplitMessage, SocketFlags.None);
            }
            
            await _socket.SendAsync(GetCurrentTimerPhase, SocketFlags.None);
        }
        finally
        {
            _expectedCommand = ServerCommand.StartTimer;
            _communicating = false;
        }
    }

    private static async void ResetIfRunningOrPaused()
    {
        // Plugin.Log($"Maybe sending reset");
        _communicating = true;
        _timerPhase = TimerPhase.None;
        try
        {
            await _socket.SendAsync(GetCurrentTimerPhase, SocketFlags.None);
            while (_timerPhase == TimerPhase.None)
            {
                await Task.Yield();
            }

            if (_timerPhase is TimerPhase.Running or TimerPhase.Paused)
            {
                await _socket.SendAsync(Reset, SocketFlags.None);
            }
        }
        finally
        {
            _communicating = false;
        }
    }

    private static async void ReadMessagesLoop(Socket socket)
    {
        using var streamReader = new StreamReader(new NetworkStream(socket, false));
        while (socket.Connected)
        {
            var text = await streamReader.ReadLineAsync();

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