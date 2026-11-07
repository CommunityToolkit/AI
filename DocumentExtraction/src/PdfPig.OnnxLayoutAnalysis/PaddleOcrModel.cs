// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal sealed class PaddleOcrModel : IDisposable
{
    internal const string DetectorHash = "4D97C44A20D30A81AAD087D6A396B08F786C4635742AFC391F6621F5C6AE78AE";
    internal const string RecognizerHash = "5825FC7EBF84AE7A412BE049820B4D86D77620F204A041697B0494669B1742C5";
    private readonly InferenceSession _detector;
    private readonly InferenceSession _recognizer;
    private readonly IReadOnlyList<string> _dictionary;

    internal PaddleOcrModel(string detectorPath, string recognizerPath, SessionOptions? sessionOptions)
    {
        LocalOnnxSession.Verify(detectorPath, DetectorHash);
        LocalOnnxSession.Verify(recognizerPath, RecognizerHash);
        _detector = LocalOnnxSession.Create(detectorPath, sessionOptions);
        InferenceSession? recognizer = null;
        try
        {
            recognizer = LocalOnnxSession.Create(recognizerPath, sessionOptions);
            LocalOnnxSession.Check(_detector.InputMetadata, "x", [-1, 3, -1, -1]);
            LocalOnnxSession.Check(_detector.OutputMetadata, "fetch_name_0", [-1, 1, -1, -1]);
            LocalOnnxSession.Check(recognizer.InputMetadata, "x", [-1, 3, 48, -1]);
            LocalOnnxSession.Check(recognizer.OutputMetadata, "fetch_name_0", [-1, -1, 18385]);
            if (_detector.InputMetadata.Count != 1 || _detector.OutputMetadata.Count != 1 ||
                recognizer.InputMetadata.Count != 1 || recognizer.OutputMetadata.Count != 1 ||
                !recognizer.ModelMetadata.CustomMetadataMap.TryGetValue("character", out string? characters))
            {
                throw new InvalidDataException("Expected pinned PP-OCRv5 mobile graphs and embedded general character dictionary.");
            }

            _dictionary = ReadDictionary(characters);
            _recognizer = recognizer;
        }
        catch
        {
            recognizer?.Dispose();
            _detector.Dispose();
            throw;
        }
    }

    internal IReadOnlyList<PdfPigOcrRegion> Extract(
        int pageNumber, PageRaster raster, CancellationToken token, List<PdfPigOcrProbabilityAdjustment>? adjustments = null)
    {
        var image = OcrImageProcessing.PrepareDetector(raster, token);
        IReadOnlyList<OcrQuad> quads;
        using (var result = LocalOnnxSession.Run(_detector, NamedOnnxValue.CreateFromTensor("x", image), token))
        {
            var probabilities = result.Single().AsTensor<float>();
            var audit = new OcrProbabilityAudit();
            quads = OcrImageProcessing.DecodeDetector(probabilities,
                raster.Width, raster.Height, image.Dimensions[3], image.Dimensions[2], token, audit);
            if (audit.Observe(pageNumber, "detector", null, probabilities.Dimensions.ToArray()) is { } observation)
            {
                adjustments?.Add(observation);
            }
        }

        var observations = new List<PdfPigOcrRegion>(quads.Count);
        for (int regionIndex = 0; regionIndex < quads.Count; regionIndex++)
        {
            token.ThrowIfCancellationRequested();
            var quad = quads[regionIndex];
            var crop = OcrImageProcessing.Rectify(raster, quad, token);
            var input = OcrImageProcessing.PrepareRecognizer(crop, token);
            using var result = LocalOnnxSession.Run(_recognizer, NamedOnnxValue.CreateFromTensor("x", input), token);
            var probabilities = result.Single().AsTensor<float>();
            var audit = new OcrProbabilityAudit();
            var (text, confidence) = Decode(probabilities, _dictionary, token, audit);
            if (audit.Observe(pageNumber, "recognizer", regionIndex, probabilities.Dimensions.ToArray()) is { } observation)
            {
                adjustments?.Add(observation);
            }
            observations.Add(new(pageNumber, raster, quad, crop, text, confidence));
        }

        return observations.AsReadOnly();
    }

    internal static IReadOnlyList<string> ReadDictionary(string characters)
    {
        string normalized = characters.Replace("\r\n", "\n").TrimEnd('\n');
        var entries = normalized.Split('\n');
        string digest = OnnxCompatibility.Sha256Hex(Encoding.UTF8.GetBytes(normalized + "\n"));
        if (entries.Length != 18383 || entries.Any(string.IsNullOrEmpty) ||
            digest != "D1979E9F794C464C0D2E0B70A7FE14DD978E9DC644C0E71F14158CDF8342AF1B")
        {
            throw new InvalidDataException("The recognizer's embedded character dictionary is not the pinned official PP-OCRv5 dictionary.");
        }

        return Array.AsReadOnly(new[] { "" }.Concat(entries).Append(" ").ToArray());
    }

    internal static (string Text, float Confidence) Decode(
        Tensor<float> probabilities, IReadOnlyList<string> dictionary, CancellationToken token, OcrProbabilityAudit? audit = null)
    {
        token.ThrowIfCancellationRequested();
        if (probabilities.Dimensions.Length != 3 || probabilities.Dimensions[0] != 1 ||
            probabilities.Dimensions[1] is < 1 or > 512 || probabilities.Dimensions[2] != dictionary.Count ||
            dictionary.Count is < 2 or > 18385)
        {
            throw new InvalidDataException("PP-OCRv5 recognition returned invalid [1,T,classes] dimensions or exceeded the 512-step budget.");
        }

        var text = new StringBuilder();
        audit ??= new OcrProbabilityAudit();
        double confidence = 0;
        int emitted = 0, previous = -1;
        for (int step = 0; step < probabilities.Dimensions[1]; step++)
        {
            token.ThrowIfCancellationRequested();
            int best = 0;
            float maximum = -1;
            for (int index = 0; index < dictionary.Count; index++)
            {
                float value;
                try
                {
                    value = audit.Normalize(probabilities[0, step, index], step * dictionary.Count + index);
                }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException(
                        $"PP-OCRv5 recognition returned invalid probability at step {step}, class {index}. {error.Message}", error);
                }

                if (value > maximum)
                {
                    maximum = value;
                    best = index;
                }
            }

            if (best != previous && best != 0)
            {
                text.Append(dictionary[best]);
                confidence += maximum;
                emitted++;
            }

            previous = best;
        }

        return (text.ToString(), emitted == 0 ? 0 : (float)(confidence / emitted));
    }

    public void Dispose()
    {
        _recognizer.Dispose();
        _detector.Dispose();
    }
}
