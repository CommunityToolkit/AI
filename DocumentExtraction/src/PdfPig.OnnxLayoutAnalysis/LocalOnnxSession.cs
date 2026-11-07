// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML.OnnxRuntime;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal static class LocalOnnxSession
{
    internal static InferenceSession Create(string path, SessionOptions? supplied)
    {
        if (supplied is not null)
        {
            return new(path, supplied);
        }

        using var options = new SessionOptions();
        return new(path, options);
    }

    internal static void Verify(string path, string expectedHash)
    {
        using var file = File.OpenRead(path);
        if (!string.Equals(OnnxCompatibility.Sha256Hex(file), expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The local model '{path}' is not the pinned curated artifact (expected SHA256 {expectedHash}).");
        }
    }

    internal static IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(
        InferenceSession session, NamedOnnxValue input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var options = new RunOptions();
        using var registration = token.Register(() => options.Terminate = true);
        try
        {
            var results = session.Run([input], session.OutputNames, options);
            if (token.IsCancellationRequested)
            {
                results.Dispose();
                token.ThrowIfCancellationRequested();
            }

            return results;
        }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
    }

    internal static void Check(
        IReadOnlyDictionary<string, NodeMetadata> metadata, string name, int[] shape)
    {
        if (!metadata.TryGetValue(name, out var node) || !node.IsTensor || node.ElementType != typeof(float) ||
            node.Dimensions.Length != shape.Length ||
            node.Dimensions.Where((dimension, index) => shape[index] < 0 ? dimension == 0 : dimension != shape[index]).Any())
        {
            throw new InvalidDataException($"Incompatible curated tensor '{name}'; expected float [{string.Join(", ", shape)}].");
        }
    }
}
