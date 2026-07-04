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

        await WriteMessageAsync(msg);

        using var reg = ct.Register(() =>
        {
            _pending.TryRemove(seq, out _);
            tcs.TrySetCanceled();
        });

        return await tcs.Task;
    }

    private async Task WriteMessageAsync(JsonObject msg)
    {
        var json = msg.ToJsonString();
        var body = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {body.Length}\r\n\r\n";
        await _writer.WriteAsync(header);
        await _writer.BaseStream.WriteAsync(body);
        await _writer.BaseStream.FlushAsync();
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
                Dispatch(json);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
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
    }

    public void Dispose()
    {
        _cts.Cancel();
        _writer.Dispose();
        _reader.Dispose();
    }
}
