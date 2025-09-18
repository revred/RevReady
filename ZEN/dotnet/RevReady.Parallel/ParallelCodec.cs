using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RevReady.Parallel;

[StructLayout(LayoutKind.Sequential)] struct XSpan { public IntPtr Ptr; public uint Len; }
[StructLayout(LayoutKind.Sequential)] struct Desc  { public XSpan In; public uint Algo, Level; public XSpan Out; public uint Flags; }
[StructLayout(LayoutKind.Sequential)] struct Batch { public uint Count; public IntPtr Descs; }
[StructLayout(LayoutKind.Sequential)] struct Res   { public uint Code, Bytes; }

static class Native {
  [LibraryImport("revready", StringMarshalling = StringMarshalling.Utf8)]
  internal static partial Res  rev_reduce_batch(ref Batch b);
  [LibraryImport("revready", StringMarshalling = StringMarshalling.Utf8)]
  internal static partial Res  rev_inflate_batch(ref Batch b);
  [LibraryImport("revready")] internal static partial void   rev_free(IntPtr p);
  [LibraryImport("revready")] internal static partial IntPtr rev_last_error();
  internal static string LastError() => Marshal.PtrToStringAnsi(rev_last_error()) ?? "";
}

public sealed class ParallelCodec : IAsyncDisposable {
  readonly Channel<ReadOnlyMemory<byte>> _in;
  readonly Channel<Memory<byte>> _mid;
  readonly Channel<Memory<byte>> _out;
  readonly int _dop, _batchTarget;
  readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
  readonly CancellationTokenSource _cts = new();
  readonly Task[] _workersCompress;
  readonly Task[] _workersInflate;

  public ParallelCodec(int degreeOfParallelism = 4, int batchBytesTarget = 1<<20) {
    _dop = Math.Max(1, degreeOfParallelism);
    _batchTarget = Math.Max(64<<10, batchBytesTarget);
    _in  = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(8*_dop){ SingleReader:false,SingleWriter:false,FullMode=BoundedChannelFullMode.Wait });
    _mid = Channel.CreateBounded<Memory<byte>>(new BoundedChannelOptions(8*_dop){ SingleReader:false,SingleWriter:false,FullMode=BoundedChannelFullMode.Wait });
    _out = Channel.CreateBounded<Memory<byte>>(new BoundedChannelOptions(8*_dop){ SingleReader:false,SingleWriter:false,FullMode=BoundedChannelFullMode.Wait });
    _workersCompress = Enumerable.Range(0,_dop).Select(_ => Task.Run(WorkerCompress)).ToArray();
    _workersInflate  = Enumerable.Range(0,_dop).Select(_ => Task.Run(WorkerInflate)).ToArray();
  }

  public ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct = default) => _in.Writer.WriteAsync(chunk, ct);
  public void Complete() => _in.Writer.TryComplete();
  public IAsyncEnumerable<Memory<byte>> GetOutputsAsync(CancellationToken ct = default) => _out.Reader.ReadAllAsync(ct);

  async Task WorkerCompress() {
    var descs = new List<Desc>(64);
    var pins = new List<GCHandle>(128);
    var outs = new List<byte[]>(64);
    try {
      int pending = 0;
      while (await _in.Reader.WaitToReadAsync(_cts.Token)) {
        while (_in.Reader.TryRead(out var piece)) {
          var arr = piece.ToArray(); var hIn = GCHandle.Alloc(arr, GCHandleType.Pinned); pins.Add(hIn);
          var outArr = _pool.Rent(arr.Length); var hOut = GCHandle.Alloc(outArr, GCHandleType.Pinned); pins.Add(hOut); outs.Add(outArr);
          descs.Add(new Desc { In = new XSpan{ Ptr = hIn.AddrOfPinnedObject(), Len = (uint)arr.Length },
                               Out= new XSpan{ Ptr = hOut.AddrOfPinnedObject(), Len = (uint)outArr.Length }, Algo=0, Level=0, Flags=0 });
          pending += arr.Length; if (pending >= _batchTarget) { DispatchReduceBatch(descs, pins, outs); pending = 0; }
        }
        if (descs.Count>0) DispatchReduceBatch(descs, pins, outs);
      }
    } catch (OperationCanceledException) { }
  }

  unsafe void DispatchReduceBatch(List<Desc> descs, List<GCHandle> pins, List<byte[]> outs) {
    var span = CollectionsMarshal.AsSpan(descs);
    fixed (Desc* p = span) {
      var b = new Batch { Count = (uint)span.Length, Descs = (IntPtr)p };
      var r = Native.rev_reduce_batch(ref b);
      if (r.Code != 0) throw new InvalidOperationException($"rev_reduce_batch: {Native.LastError()} code={r.Code}");
      for (int i=0;i<span.Length;i++) {
        var d = span[i]; var buf = outs[i];
        _mid.Writer.TryWrite(buf.AsMemory(0, (int)Math.Min(d.Out.Len, (uint)buf.Length)));
      }
    }
    foreach (var h in pins) if (h.IsAllocated) h.Free();
    pins.Clear(); descs.Clear(); outs.Clear();
  }

  async Task WorkerInflate() {
    var descs = new List<Desc>(64);
    var pins = new List<GCHandle>(128);
    var outs = new List<byte[]>(64);
    try {
      int pending = 0;
      while (await _mid.Reader.WaitToReadAsync(_cts.Token)) {
        while (_mid.Reader.TryRead(out var piece)) {
          var arr = piece.ToArray(); var hIn = GCHandle.Alloc(arr, GCHandleType.Pinned); pins.Add(hIn);
          var outArr = _pool.Rent(arr.Length * 2); var hOut = GCHandle.Alloc(outArr, GCHandleType.Pinned); pins.Add(hOut); outs.Add(outArr);
          descs.Add(new Desc { In = new XSpan{ Ptr = hIn.AddrOfPinnedObject(), Len = (uint)arr.Length },
                               Out= new XSpan{ Ptr = hOut.AddrOfPinnedObject(), Len = (uint)outArr.Length }, Algo=0, Level=0, Flags=0 });
          pending += arr.Length; if (pending >= _batchTarget) { DispatchInflateBatch(descs, pins, outs); pending = 0; }
        }
        if (descs.Count>0) DispatchInflateBatch(descs, pins, outs);
      }
    } catch (OperationCanceledException) { }
  }

  unsafe void DispatchInflateBatch(List<Desc> descs, List<GCHandle> pins, List<byte[]> outs) {
    var span = CollectionsMarshal.AsSpan(descs);
    fixed (Desc* p = span) {
      var b = new Batch { Count = (uint)span.Length, Descs = (IntPtr)p };
      var r = Native.rev_inflate_batch(ref b);
      if (r.Code != 0) throw new InvalidOperationException($"rev_inflate_batch: {Native.LastError()} code={r.Code}");
      for (int i=0;i<span.Length;i++) {
        var d = span[i]; var buf = outs[i];
        _out.Writer.TryWrite(buf.AsMemory(0, (int)Math.Min(d.Out.Len, (uint)buf.Length)));
      }
    }
    foreach (var h in pins) if (h.IsAllocated) h.Free();
    pins.Clear(); descs.Clear(); outs.Clear();
  }

  public async ValueTask DisposeAsync() {
    _cts.Cancel();
    try { await Task.WhenAll(_workersCompress); } catch {}
    try { await Task.WhenAll(_workersInflate); } catch {}
    _cts.Dispose(); _mid.Writer.TryComplete(); _out.Writer.TryComplete();
  }
}
