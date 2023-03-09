#!/usr/bin/env dotnet-script
#nullable enable


// This is a reduced model of the real application's interactive prompts.
// We're using async aggressively because the real one runs with unpredictable logic and inconsistent timings.
// This gives us a better approximation of the IO patterns without resorting to actual random delays.
// The sample below does NOT simulate any "success to STDOUT, failure to STDERR" logic that may be used.


// Some helper functions because otherwise this gets REALLY cluttered REALLY fast
async Task<string> ReadLineAsync() => await Console.In.ReadLineAsync() ?? throw new IOException("STDIN is closed!!");
async Task WriteAsync(string line = "") => await Console.Out.WriteAsync(line);
async Task WriteLineAsync(string line = "") => await Console.Out.WriteLineAsync(line);
async Task<string> PromptAsync(string valueName) {
    await WriteAsync($"Please enter {valueName}: ");
    return await ReadLineAsync();
}


// Simulate the fixed prefix/header output
async Task SimHeader()
{
    await WriteLineAsync("Welcome to math!");
    await WriteLineAsync("Please follow the instructions to calculate something.");
    await WriteLineAsync();
}

// Simulate a back-and-forth interaction
async Task SimInteraction()
{
    var num1Str = await PromptAsync("the first number");
    var num1 = int.Parse(num1Str);

    var num2Str = await PromptAsync("the second number");
    var num2 = int.Parse(num2Str);

    var op = await PromptAsync("an operation");
    var response = op switch {
        "+" => $"The result is: {num1 + num2}",
        "-" => $"The result is: {num1 - num2}",
        "*" => $"The result is: {num1 * num2}",
        "/" => $"The result is: {num1 / num2}",
        "%" => $"The result is: {num1 % num2}",
        _ => "Oops! That's not a recognized operation. Please enter one of [+-*/%]."
    };
    await WriteLineAsync(response);
}

// Simulate the fixed trailer/footer output
async Task SimFooter() {
    await WriteLineAsync();
    await WriteLineAsync("Thank you for using Math!");
    await WriteLineAsync("We hope to see you again soon :)");
}


// Run the sim
await SimHeader();
await SimInteraction();
await SimFooter();