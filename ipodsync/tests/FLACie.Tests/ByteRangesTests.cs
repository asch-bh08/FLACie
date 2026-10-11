using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

public class ByteRangesTests
{
    [Theory]
    [InlineData("", 100, true, false, 0, 99)]
    [InlineData("garbage", 100, true, false, 0, 99)]
    [InlineData("bytes=5-3", 100, true, false, 0, 99)]          // not a valid range spec: ignored, the whole body
    [InlineData("items=0-1", 100, true, false, 0, 99)]          // another unit: ignored
    [InlineData("bytes=0-9", 100, true, true, 0, 9)]
    [InlineData("bytes=10-", 100, true, true, 10, 99)]
    [InlineData("bytes=90-500", 100, true, true, 90, 99)]       // end beyond the body is cut to it
    [InlineData("bytes=-10", 100, true, true, 90, 99)]
    [InlineData("bytes=-500", 100, true, true, 0, 99)]          // a suffix longer than the body is the whole body
    [InlineData("bytes=0-1,5-6", 100, true, true, 0, 1)]
    [InlineData("bytes=100-", 100, false, true, 0, 0)]          // starts at the end: 416
    [InlineData("bytes=999999999-", 100, false, true, 0, 0)]
    [InlineData("bytes=-0", 100, false, true, 0, 0)]            // an empty suffix cannot be satisfied
    [InlineData("", 0, true, false, 0, -1)]                     // an empty file is a normal, empty 200
    [InlineData("bytes=0-", 0, false, true, 0, 0)]
    public void Resolve(string header, long size, bool ok, bool partial, long from, long to)
    {
        var r = ByteRanges.Resolve(header, size);
        Assert.Equal((ok, partial), (r.Satisfiable, r.Partial));
        if (ok) Assert.Equal((from, to), (r.From, r.To));
    }
}
