using System.IO.Pipelines;
using System.Text;

namespace WebSocketClientServer
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // This is just a stream that has a Thread.Sleep in the overridden Write method.
            using SlowNullStream myStream = new();

            // Concrete type is StreamPipeWriter
            PipeWriter myPipeWriter = PipeWriter.Create(myStream);
            var pipeWriterStream = myPipeWriter.AsStream();
            var bytes = Encoding.UTF8.GetBytes("Hello, World!");

            CancellationTokenSource myCts = new(TimeSpan.FromSeconds(1));

            // This write will take at least ten seconds because of SlowNullStream
            var writeTask = pipeWriterStream.WriteAsync(bytes, myCts.Token);

            // Dispose while a write is in progress, and before the cancellation
            // token is canceled. The application will crash when the
            // CancellationTokenSource is canceled by its cancellation timer.
            pipeWriterStream.Dispose();

            try
            {
                await writeTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"This exception won't be thrown because we've crashed: {ex}");
            }

            Console.WriteLine("This won't happen because we've crashed!");
        }

        public class SlowNullStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;

            public override long Position
            {
                get => 0;
                set => throw new NotSupportedException();
            }

            public override void Flush()
                => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
                => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin)
                => throw new NotSupportedException();

            public override void SetLength(long value)
                => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                // Just make this really slow so that the code is in the
                // right place for a long time.
                Thread.Sleep(10_000);
            }
        }
    }
}