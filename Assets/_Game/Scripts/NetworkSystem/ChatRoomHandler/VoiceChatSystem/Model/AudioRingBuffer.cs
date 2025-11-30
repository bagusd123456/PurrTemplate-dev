using System;

public class AudioRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _capacity;
    private int _writeHead;
    private int _readHead;
    
    // Public tracker for how many samples are currently buffered
    public int Count { get; private set; }

    public AudioRingBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new float[capacity];
        _writeHead = 0;
        _readHead = 0;
        Count = 0;
    }

    /// <summary>
    /// Writes a chunk of data into the ring buffer using fast Array.Copy
    /// </summary>
    public void Write(float[] input, int length)
    {
        if (length > _capacity - Count)
        {
            // Optional: Handle overflow (e.g., clear buffer or log warning)
            // For voice, we usually just accept the glitch or ensure buffer is huge.
            // Resetting is safest to prevent desync.
            Clear(); 
        }

        // Calculate how much space is left at the end of the array before wrapping
        int spaceAtEnd = _capacity - _writeHead;

        if (length <= spaceAtEnd)
        {
            // Case A: No wrapping needed
            Array.Copy(input, 0, _buffer, _writeHead, length);
            _writeHead += length;
        }
        else
        {
            // Case B: We need to wrap around to the start
            // Copy until the end
            Array.Copy(input, 0, _buffer, _writeHead, spaceAtEnd);
            
            // Copy the rest to the beginning
            int remaining = length - spaceAtEnd;
            Array.Copy(input, spaceAtEnd, _buffer, 0, remaining);
            
            _writeHead = remaining;
        }

        if (_writeHead >= _capacity) _writeHead = 0;
        Count += length;
    }

    /// <summary>
    /// Reads a chunk of data from the ring buffer into the output array
    /// </summary>
    public void Read(float[] output, int length)
    {
        if (length > Count) throw new Exception("Buffer Underflow");

        int spaceAtEnd = _capacity - _readHead;

        if (length <= spaceAtEnd)
        {
            // Case A: No wrapping needed
            Array.Copy(_buffer, _readHead, output, 0, length);
            _readHead += length;
        }
        else
        {
            // Case B: Wrapped around
            // Read until end
            Array.Copy(_buffer, _readHead, output, 0, spaceAtEnd);

            // Read from start
            int remaining = length - spaceAtEnd;
            Array.Copy(_buffer, 0, output, spaceAtEnd, remaining);

            _readHead = remaining;
        }

        if (_readHead >= _capacity) _readHead = 0;
        Count -= length;
    }

    public void Clear()
    {
        _writeHead = 0;
        _readHead = 0;
        Count = 0;
    }
}