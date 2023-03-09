#!/usr/bin/env dotnet-script
#nullable enable

using System.Text.RegularExpressions;

// This is a reduced model of the proposed wrapper around the real application.
// It uses a mid-level abstraction (InteractiveProcess) to wrap the prompt system without including any business logic.
// The sample below does NOT simulate any "success to STDOUT, failure to STDERR" logic that may be used.


// Encapsulates the interaction logic and exposes basic primitives as async functions.
// Other functions of the wrapper are excluded for brevity.
// Cancellation tokens are not utilized but can be easily included.
// This version fully controls the subprocess lifecycle and expects it to exit on dispose.
// The real implementation may need to expose a timeout, cancellation token, or other options to control this behavior.
class InteractiveProcess : IDisposable, IAsyncDisposable
{
    private static readonly Regex NewlineRegex = new("\r?\n", RegexOptions.Compiled);

    private const int MAX_CHUNK_SIZE = 1024;
    private readonly Memory<char> _chunkBuffer = new(new char[MAX_CHUNK_SIZE]);

    private readonly Process _process;

    // If the last call to SkipTo contained extra text after the patterns, then it will be cached here.
    private string? _carryoverChunk;

    public InteractiveProcess(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;

        var proc = Process.Start(startInfo);
        if (proc == null) 
            throw new ApplicationException("Could not start a process based on the provided ProcessStartInfo. Process.Start() returned null.");

        _process = proc;
    }

    public async Task WriteLine(string line) => await _process.StandardInput.WriteLineAsync(line);
    public async Task<string> ReadChunk()
    {
        // If there's a carryover then return that
        if (_carryoverChunk != null) {
            var line = _carryoverChunk;
            _carryoverChunk = null;
            return line;
        }

        // Otherwise, read a fresh chunk
        var numRead = await _process.StandardOutput.ReadAsync(_chunkBuffer);
        if (numRead < 1)
            throw new InvalidOperationException("The process exited without writing any data.");

        // Chop it down to size and return
        return _chunkBuffer
            .Slice(0, numRead)
            .ToString();
    }
    public async Task<string> ReadLine() => await ReadNextLine() ?? throw new InvalidOperationException("The process exited without writing any data.");
    public async Task<string?> ReadNextLine()
    {
        // If there's no carryover chunk, then just get a new line.
        if (_carryoverChunk == null)
        {
            return await _process.StandardOutput.ReadLineAsync();
        }

        // Try to extract a line from carryover
        var newlineMatch = NewlineRegex.Match(_carryoverChunk);

        // No match - carryover contains only part of the next line.
        // We need to read the rest of it from the source output.
        if (!newlineMatch.Success)
        {
            // Get all pieces of the line
            var firstPart = _carryoverChunk;
            var restOfLine = await _process.StandardOutput.ReadLineAsync();

            // Reset the carryover chunk, since we consumed the whole thing
            _carryoverChunk = null;

            // restOfLine can be null, in which case the first part actually IS the entire line
            if (restOfLine == null)
                return firstPart;

            // Otherwise, we concat and return
            return firstPart + restOfLine;
        }
        
        // We matched! Now extract the line and remove it from carryover.
        var newlineStart = newlineMatch.Index;
        var line = _carryoverChunk.Substring(0, newlineStart);

        // Update the carryover
        var newlineEnd = newlineStart + newlineMatch.Length;
        if (newlineEnd < _carryoverChunk.Length)
            _carryoverChunk = _carryoverChunk.Substring(newlineEnd);
        else
            _carryoverChunk = null;

        // Return the line
        return line;
    }

    /// <summary>
    ///   Continually reads output from the process until a specified pattern is printed.
    ///   Input up to and including the pattern is discarded.
    /// </summary>
    /// <param name="pattern">Pattern to match</param>
    public async Task SkipTo(string pattern)
    {
        // Read each chunk of output and check for the pattern.
        // This will start with the last carryover line, if present.
        string? chunk;
        while ((chunk = await ReadChunk()) != null)
        {
            // Check if this line contains the pattern
            var patternIndex = chunk.IndexOf(pattern);
            if (patternIndex > -1) {

                // Cache anything after the pattern to prevent lost output
                var patternEnd = patternIndex + pattern.Length;
                if (chunk.Length > patternEnd) {
                    _carryoverChunk = chunk.Substring(patternEnd);
                }

                // We found it - stop looping
                return;
            }
        }

        throw new ApplicationException($"SkipTo failed - pattern '{pattern}' was not found the process output");
    }

    // IDisposable interface - https://learn.microsoft.com/en-us/dotnet/api/system.idisposable?view=net-7.0
    // Async Disposable pattern - https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync
    // Process class is managed - https://stackoverflow.com/questions/63323973/is-the-process-class-in-c-sharp-an-unmanaged-resource
    #region Dispose Pattern

    bool isDisposed = false;

    public async ValueTask DisposeAsync()
    {
        // Perform async cleanup.
        await DisposeAsyncCore();

        // Dispose of unmanaged resources.
        Dispose(false);

        // Suppress finalization.
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        // Small deviation from the standard Async Dispose pattern:
        //   We use isDisposed here to prevent a buggy caller from calling DisposeAsync() *and* Dispose().
        //   That probably wouldn't cause an issue, but better safe than sorry.
        if (!isDisposed)
        {
            isDisposed = true;

            _carryoverChunk = null;

            // If process is still alive, then we need to synchronously stop it
            if (!_process.HasExited)
            {
                // First, close streams to prevent deadlock
                _process.StandardOutput.Close();
                _process.StandardInput.Close();

                // Then, wait for process exit.
                // Process is expected to end itself if streams are closed.
                await _process.WaitForExitAsync();
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!isDisposed)
        {
            isDisposed = true;

            if (disposing)
            {
                _carryoverChunk = null;

                // If process is still alive, then we need to synchronously stop it
                if (!_process.HasExited)
                {
                    // First, close streams to prevent deadlock
                    _process.StandardOutput.Close();
                    _process.StandardInput.Close();

                    // Then, wait for process exit.
                    // Process is expected to end itself if streams are closed.
                    _process.WaitForExit();
                }
            }
        }
    }

    #endregion
}


// Perform calculations with math.csx to simulate interaction with the real application.
async Task<int> Calculate(int num1, string op, int num2)
{
    // Start subprocess
    await using var proc = new InteractiveProcess(new ProcessStartInfo() {
        FileName = "dotnet-script",
        Arguments = "math.csx"
    });

    // Enter num1
    await proc.SkipTo("Please enter the first number: ");
    await proc.WriteLine(num1.ToString());

    // Enter num2
    await proc.SkipTo("Please enter the second number: ");
    await proc.WriteLine(num2.ToString());

    // Enter operation
    await proc.SkipTo("Please enter an operation: ");
    await proc.WriteLine(op);

    // Read and parse result
    await proc.SkipTo("The result is: ");
    var response = await proc.ReadLine();
    var result = int.Parse(response.Trim());

    return result;
}

// Utility wrappers around math API
async Task<int> Add(int num1, int num2) => await Calculate(num1, "+", num2);
async Task<int> Subtract(int num1, int num2) => await Calculate(num1, "-", num2);
async Task<int> Multiply(int num1, int num2) => await Calculate(num1, "*", num2);
async Task<int> Divide(int num1, int num2) => await Calculate(num1, "/", num2);
async Task<int> Remainder(int num1, int num2) => await Calculate(num1, "%", num2);

// Synthetic compound operations
async Task<int> Modulus(int num1, int num2) {
    // Emulate modulus with remainder - https://stackoverflow.com/questions/1082917/mod-of-negative-number-is-melting-my-brain
    var result = await Remainder(num1, num2);
    if (result < 0) {
        result = await Add(result, num2);
    }
    return result;
} 
async Task<int> Invert(int a, int m)
{
    // Modular Multiplicative Inverse using the Extended Euclidean Algorithm 
    // https://en.wikipedia.org/wiki/Modular_multiplicative_inverse
    // https://en.wikipedia.org/wiki/Extended_Euclidean_algorithm

    var t1 = 0;
    var t2 = 1;
    var r1 = m;
    var r2 = a;

    // Compute inverse
    while (r2 != 0)
    {
        var quot = await Divide(r1, r2);
        
        var t1Temp = t1;
        t1 = t2;
        t2 = await Subtract(t1Temp, await Multiply(quot, t2));

        var r1Temp = r1;
        r1 = r2;
        r2 = await Subtract(r1Temp, await Multiply(quot, r2));
    }

    // Normalize T
    if (t1 < 0)
        t1 = await Add(t1, m);

    // Validate result
    if (r1 > 1)
        throw new ArgumentException($"There is no inverse of {a} modulo {m}");

    return t1;
}
async Task<int> AffineEncrypt(int a, int b, int m, int x)
{
    // Classical Affine Cipher - https://en.wikipedia.org/wiki/Affine_cipher

    if (a < 2) // Technically A must also be coprime with M, but I don't care to implement that check for a simple demo
        throw new ArgumentOutOfRangeException(nameof(a), a, "A must be at least 2");
    if (b < 0) // This can probably be negative - need to check
        throw new ArgumentOutOfRangeException(nameof(b), b, "B must be at least 0");
    if (m < 1)
        throw new ArgumentOutOfRangeException(nameof(m), m, "M must be at least 1");
    if (x > m)
        throw new ArgumentOutOfRangeException(nameof(x), x, "Value to encrypt (x) must not be greater than modulus (m)");
    
    // Formula is (Ax + B) % M
    return await Modulus(await Add(await Multiply(a, x), b), m);
}
async Task<int> AffineDecrypt(int a, int b, int m, int x)
{
    // Classical Affine Cipher - https://en.wikipedia.org/wiki/Affine_cipher

    if (a < 2) // Technically A must also be coprime with M, but I don't care to implement that check for a simple demo
        throw new ArgumentOutOfRangeException(nameof(a), a, "A must be at least 2");
    if (b < 0) // This can probably be negative - need to check
        throw new ArgumentOutOfRangeException(nameof(b), b, "B must be at least 0");
    if (m < 1)
        throw new ArgumentOutOfRangeException(nameof(m), m, "M must be at least 1");
    if (x > m)
        throw new ArgumentOutOfRangeException(nameof(x), x, "Value to encrypt (x) must not be greater than modulus (m)");

    // Formula is A'(x - B) % M
    return await Modulus(await Multiply(await Invert(a, m), await Subtract(x, b)), m);
}

// High-level operations
async Task<string> AffineEncryptString(int a, int b, string plaintext)
{
    var output = new char[plaintext.Length];
    for (var i = 0; i < plaintext.Length; i++)
    {
        var x = plaintext[i] - 'A';
        if (x < 0 || x > 25)
            throw new ArgumentOutOfRangeException(nameof(plaintext), "Plaintext must contain only capital letters.");
        
        var encrypted = await AffineEncrypt(a, b, 26, x);
        output[i] = (char)('A' + encrypted);
    }
    return new String(output);
}
async Task<string> AffineDecryptString(int a, int b, string ciphertext)
{
    var output = new char[ciphertext.Length];
    for (var i = 0; i < ciphertext.Length; i++)
    {
        var x = ciphertext[i] - 'A';
        if (x < 0 || x > 25)
            throw new ArgumentOutOfRangeException(nameof(ciphertext), "Ciphertext must contain only capital letters.");
        
        var decrypted = await AffineDecrypt(a, b, 26, x);
        output[i] = (char)('A' + decrypted);
    }
    return new String(output);
}

// Simulate use of the wrapper
async Task<object> RunSim(string func, IList<string> args)
{
    if (func == "add") return await Add(int.Parse(args[0]), int.Parse(args[1]));
    if (func == "sub") return await Subtract(int.Parse(args[0]), int.Parse(args[1]));
    if (func == "mul") return await Multiply(int.Parse(args[0]), int.Parse(args[1]));
    if (func == "div") return await Divide(int.Parse(args[0]), int.Parse(args[1]));
    if (func == "rem") return await Remainder(int.Parse(args[0]), int.Parse(args[1]));
    if (func == "mod") return await Modulus(int.Parse(args[0]), int.Parse(args[1]));
    if (func == "inv") return await Invert(int.Parse(args[0]), int.Parse(args[1]));
    if (func == "afe") return await AffineEncrypt(int.Parse(args[0]), int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]));
    if (func == "afd") return await AffineDecrypt(int.Parse(args[0]), int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]));
    if (func == "enc") return await AffineEncryptString(int.Parse(args[0]), int.Parse(args[1]), args[2]);
    if (func == "dec") return await AffineDecryptString(int.Parse(args[0]), int.Parse(args[1]), args[2]);
    throw new ArgumentException($"Unknown function '{func}'", nameof(func));
}


// Parse args and run simulation
var result = await RunSim(Args[0].ToLower(), Args.Skip(1).ToList());
Console.WriteLine($"The result is {result}");