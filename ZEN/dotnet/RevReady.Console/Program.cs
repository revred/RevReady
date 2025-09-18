using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using RevReady.Parallel;

Console.WriteLine("RevReady.Console — Random round-trip (RAW stub)");
var rnd = new Random(1234);
var blobs = Enumerable.Range(0, 16).Select(_ => {
  int n = rnd.Next(4<<10, 128<<10);
  var b = ArrayPool<byte>.Shared.Rent(n);
  rnd.NextBytes(b);
  if (rnd.NextDouble() < 0.3) for (int i=0;i<n;i++) b[i] = (byte)((i*7)&0xFF);
  var arr = new byte[n]; Buffer.BlockCopy(b,0,arr,0,n); ArrayPool<byte>.Shared.Return(b);
  return arr;
}).ToList();
await using var pipe = new ParallelCodec(degreeOfParallelism: 4, batchBytesTarget: 1<<20);
foreach (var blob in blobs) await pipe.WriteAsync(blob);
pipe.Complete();
var outputs = new System.Collections.Generic.List<byte>();
await foreach (var m in pipe.GetOutputsAsync()) outputs.AddRange(m.ToArray());
var original = blobs.SelectMany(x => x).ToArray();
Console.WriteLine($"Original: {original.Length} bytes, Round-trip: {outputs.Count} bytes");
Console.WriteLine(original.SequenceEqual(outputs) ? "OK: identity preserved." : "FAIL: mismatch!");
