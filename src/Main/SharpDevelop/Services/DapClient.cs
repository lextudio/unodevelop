using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace UnoDevelop.Services;

/// <summary>
/// Minimal DAP client over stdin/stdout (Content-Length framing, JSON-RPC style).
/// </summary>
internal sealed class DapClient : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private int _seq;

    public event Action<string, JsonObject?>? EventReceived;

    public DapClient(Stream input, Stream output)
    {
        _writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true };
        _reader = new StreamReader(input, new UTF8Encoding(false));
    }

    public void Start() =>
        Task.Run(ReadLoop, _cts.Token);

    public async Task<JsonObject?> SendRequestAsync(string command, JsonObject? args = null, CancellationToken ct = default)
    {
        await _requestLock.WaitAsync(ct);
        try
        {
            return await SendRequestCoreAsync(command, args, ct);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task<JsonObject?> SendRequestCoreAsync(string command, JsonObject? args = null, CancellationToken ct = default)
    {
        var seq = Interlocked.Increment(ref _seq);
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = tcs;

        var msg = new JsonObject
        {
            ["seq"] = seq,
            ["type"] = "request",
            ["command"] = command
        };
        if (args is not null) msg["arguments"] = args;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        await WriteMessageAsync(msg, timeoutCts.Token);

        using var reg = timeoutCts.Token.Register(() =>
        {
            _pending.TryRemove(seq, out _);
            tcs.TrySetCanceled();
        });

        return await tcs.Task;
    }

    private async Task WriteMessageAsync(JsonObject msg, CancellationToken ct = default)
    {
        var json = msg.ToJsonString();
        Dbg("SEND " + json);
        var body = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {body.Length}\r\n\r\n";
        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteAsync(header.AsMemory(), ct);
            await _writer.BaseStream.WriteAsync(body, ct);
            await _writer.BaseStream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoop()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                // Read headers
                int contentLength = 0;
                while (true)
                {
                    var line = await _reader.ReadLineAsync(_cts.Token);
                    if (line is null) return;
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        contentLength = int.Parse(line.AsSpan("Content-Length:".Length).Trim());
                    else if (line.Length == 0 && contentLength > 0)
                        break;
                }

                // Read body
                var buf = new char[contentLength];
                var read = 0;
                while (read < contentLength)
                    read += await _reader.ReadAsync(buf, read, contentLength - read);

                var json = new string(buf);
                Dbg("RECV " + json);
                Dispatch(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Dbg("READ LOOP ERROR " + ex); }
    }

    private void Dispatch(string json)
    {
        JsonObject? obj;
        try { obj = JsonNode.Parse(json) as JsonObject; }
        catch { return; }
        if (obj is null) return;

        var type = obj["type"]?.GetValue<string>();
        if (type == "response")
        {
            var reqSeq = obj["request_seq"]?.GetValue<int>() ?? 0;
            if (_pending.TryRemove(reqSeq, out var tcs))
                tcs.TrySetResult(obj);
        }
        else if (type == "event")
        {
            var evt = obj["event"]?.GetValue<string>() ?? string.Empty;
            var body = obj["body"] as JsonObject;
            EventReceived?.Invoke(evt, body);
        }
        else if (type == "request")
        {
            _ = RespondToReverseRequestAsync(obj);
        }
    }

    private async Task RespondToReverseRequestAsync(JsonObject request)
    {
        var seq = request["seq"]?.GetValue<int>() ?? 0;
        var command = request["command"]?.GetValue<string>() ?? string.Empty;
        var response = new JsonObject
        {
            ["seq"] = Interlocked.Increment(ref _seq),
            ["type"] = "response",
            ["request_seq"] = seq,
            ["success"] = true,
            ["command"] = command,
            ["body"] = new JsonObject()
        };

        await WriteMessageAsync(response);
    }

    private static void Dbg(string message)
    {
        try { File.AppendAllText("/tmp/unodevelop-dap.log", $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n"); } catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _requestLock.Dispose();
        _writeLock.Dispose();
        try { _writer.Dispose(); } catch { }
        try { _reader.Dispose(); } catch { }
    }
}
