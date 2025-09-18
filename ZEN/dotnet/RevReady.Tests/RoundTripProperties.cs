using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using RevReady.Parallel;

public class RoundTripProperties {
  [Property(MaxTest=100)]
  public Property RandomizedSessions(int seed) {
    var rnd = new Random(seed);
    var blobs = Enumerable.Range(0, rnd.Next(4,12)).Select(_ => {
      int n = rnd.Next(1<<12, 1<<17);
      var b = ArrayPool<byte>.Shared.Rent(n);
      rnd.NextBytes(b);
      if (rnd.NextDouble() < 0.3) for (int i=0;i<n;i++) b[i] = (byte)((i*13)&0xFF);
      var arr = new byte[n]; Buffer.BlockCopy(b,0,arr,0,n); ArrayPool<byte>.Shared.Return(b);
      return arr;
    }).ToList();
    return Prop.ForAll(Prop.From(() => true), _ => {
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
      var task = Task.Run(async () => {
        await using var pipe = new ParallelCodec(4, 1<<20);
        foreach (var blob in blobs) await pipe.WriteAsync(blob, cts.Token);
        pipe.Complete();
        var outputs = new System.Collections.Generic.List<byte>();
        await foreach (var m in pipe.GetOutputsAsync(cts.Token)) outputs.AddRange(m.ToArray());
        var original = blobs.SelectMany(x => x).ToArray();
        Assert.Equal(original.Length, outputs.Count);
        Assert.True(original.SequenceEqual(outputs));
      }, cts.Token);
      task.GetAwaiter().GetResult();
    });
  }
}
