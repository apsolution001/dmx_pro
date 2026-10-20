namespace DmxPro.Audio.Analysis;

/// <summary>
/// Power spectrum of a real signal of power-of-two length N, computed with one complex radix-2 FFT of N / 2 points
/// (even samples as real part, odd samples as imaginary part) plus the standard split step. Holds scratch buffers, so
/// one instance must not be used from two threads at once.
/// </summary>
internal sealed class Fft
{
    private readonly int _size;
    private readonly int _half;
    private readonly int[] _bitReverse;
    private readonly double[] _stageCos;
    private readonly double[] _stageSin;
    private readonly double[] _splitCos;
    private readonly double[] _splitSin;
    private readonly double[] _re;
    private readonly double[] _im;

    public Fft(int size)
    {
        if (size < 4 || (size & (size - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "FFT size must be a power of two >= 4.");
        }

        _size = size;
        _half = size / 2;
        var bits = 0;
        while ((1 << bits) < _half)
        {
            bits++;
        }

        _bitReverse = new int[_half];
        for (var i = 0; i < _half; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++)
            {
                r |= ((i >> b) & 1) << (bits - 1 - b);
            }

            _bitReverse[i] = r;
        }

        // Twiddles of the N/2-point complex FFT, laid out stage after stage so the inner loop reads them in order.
        _stageCos = new double[Math.Max(1, _half)];
        _stageSin = new double[Math.Max(1, _half)];
        var offset = 0;
        for (var len = 2; len <= _half; len <<= 1)
        {
            var halfLen = len >> 1;
            for (var k = 0; k < halfLen; k++)
            {
                var angle = -2.0 * Math.PI * k / len;
                _stageCos[offset + k] = Math.Cos(angle);
                _stageSin[offset + k] = Math.Sin(angle);
            }

            offset += halfLen;
        }

        _splitCos = new double[_half + 1];
        _splitSin = new double[_half + 1];
        for (var k = 0; k <= _half; k++)
        {
            var angle = 2.0 * Math.PI * k / size;
            _splitCos[k] = Math.Cos(angle);
            _splitSin[k] = Math.Sin(angle);
        }

        _re = new double[_half];
        _im = new double[_half];
    }

    public int Size => _size;

    /// <summary>
    /// |X[k]|^2 for k = 0..N/2 of the unnormalised DFT X[k] = sum x[n] e^(-2 pi i k n / N).
    /// </summary>
    public void PowerSpectrum(ReadOnlySpan<double> input, Span<double> power)
    {
        if (input.Length != _size || power.Length != _half + 1)
        {
            throw new ArgumentException("Buffers must match the FFT size.");
        }

        var re = _re;
        var im = _im;
        var half = _half;
        for (var n = 0; n < half; n++)
        {
            var target = _bitReverse[n];
            re[target] = input[2 * n];
            im[target] = input[(2 * n) + 1];
        }

        Transform(re, im);

        power[0] = (re[0] + im[0]) * (re[0] + im[0]);
        power[half] = (re[0] - im[0]) * (re[0] - im[0]);
        for (var k = 1; k < half; k++)
        {
            var ar = re[k];
            var ai = im[k];
            var br = re[half - k];
            var bi = -im[half - k];

            // Even part E = (Z[k] + conj Z[M-k]) / 2, odd part O = (Z[k] - conj Z[M-k]) / 2i, X[k] = E + W^k O.
            var er = 0.5 * (ar + br);
            var ei = 0.5 * (ai + bi);
            var or = 0.5 * (ai - bi);
            var oi = -0.5 * (ar - br);
            var c = _splitCos[k];
            var s = _splitSin[k];
            var xr = er + (or * c) + (oi * s);
            var xi = ei + (oi * c) - (or * s);
            power[k] = (xr * xr) + (xi * xi);
        }
    }

    // In-place complex FFT of bit-reversed input.
    private void Transform(double[] re, double[] im)
    {
        var n = _half;

        // First stage: twiddle 1, plain sums and differences.
        for (var a = 0; a < n; a += 2)
        {
            var tr = re[a + 1];
            var ti = im[a + 1];
            re[a + 1] = re[a] - tr;
            im[a + 1] = im[a] - ti;
            re[a] += tr;
            im[a] += ti;
        }

        var cosTable = _stageCos;
        var sinTable = _stageSin;
        var offset = 1;
        for (var len = 4; len <= n; len <<= 1)
        {
            var halfLen = len >> 1;
            for (var start = 0; start < n; start += len)
            {
                for (var k = 0; k < halfLen; k++)
                {
                    var wr = cosTable[offset + k];
                    var wi = sinTable[offset + k];
                    var a = start + k;
                    var b = a + halfLen;
                    var tr = (re[b] * wr) - (im[b] * wi);
                    var ti = (re[b] * wi) + (im[b] * wr);
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }

            offset += halfLen;
        }
    }
}
