using System.Buffers;
using System.Diagnostics;
using Melville.Parsing.SequenceReaders;
using Melville.Parsing.Streams.Bases;

namespace Melville.JpegLibrary.PipeAmdStreamAdapters;

internal abstract class RentedArrayReadingStream : DefaultBaseStream, ISequenceSource
{
    private readonly byte[] data;
    private bool returned;
    private int length;
    private int position;
    public RentedArrayReadingStream(byte[] data, int length) : base(true, false, false)
    {
        this.data = data;
        this.length = length;
        Debug.Assert(this.data.Length >= this.length);
    }

    public ReadOnlySequence<byte> AsSequence() => new ReadOnlySequence<byte>(data.AsMemory(position, length-position));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        // The rented array can be longer than the data: read only the first `length` bytes.
        var ret = CopyBytes(data.AsSpan(position, length - position), buffer.Span);
        position += ret;
        return new(ret);
    }

    protected abstract int CopyBytes(Span<byte> source, Span<byte> destination);

    protected override void Dispose(bool disposing)
    {
        // Stream.Dispose can run more than once; returning the array twice would give it to two renters.
        if (disposing && !returned)
        {
            returned = true;
            ArrayPool<byte>.Shared.Return(data);
        }
    }
}